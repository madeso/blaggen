using System.Collections.Immutable;
using System.Text;

namespace Blaggen;


public class TemplateDictionary
{
    public ImmutableHashSet<string> Extensions { get; }
    private ImmutableDictionary<string, Func<Generate.PageData, string>> LoadedTemplates { get; } // can't use FileInfo as a key
    public Func<Generate.PageData, string>? GetTemplateOrNull(FileInfo file) =>  LoadedTemplates.TryGetValue(file.FullName, out var contents) ? contents : null;


    private TemplateDictionary(ImmutableDictionary<string, Func<Generate.PageData, string>> td, ImmutableHashSet<string> ex)
    {
        Extensions = ex;
        LoadedTemplates = td;
    }


    public static async Task<TemplateDictionary> Load(Run run, VfsRead vfs, DirectoryInfo root, DirectoryInfo templateFolder, DirectoryInfo partialFolder)
    {
        // todo(Gustav): warn if template files are missing
        var templateFiles = vfs.GetFilesRec(templateFolder)
            .Where(f => f.Name.Contains(Constants.MUSTACHE_TEMPLATE_POSTFIX))
            .ToImmutableArray();

        var unloaded = templateFiles.Select(
            async file => new
            {
                File = file,
                Parsed = await Template.Parse(file, vfs, Template.DefaultFunctions(), partialFolder, Generate.MakePageDataDef())
            }
        );
        var loaded = (await Task.WhenAll(unloaded))
            .ToImmutableArray();

        var errors = loaded.SelectMany(x => x.Parsed.Item2);
        foreach (var error in errors)
        {
            run.WriteError($"{error.Location.File}({error.Location.Line}:{error.Location.Offset}): {error.Message}");
        }

        var dict = loaded
                .Where(x => x.Parsed.Item2.IsEmpty)
                .ToImmutableDictionary(x => x.File.FullName, x => x.Parsed.Item1)
            ;

        var extensions = templateFiles.Select(file => file.Extension.ToLowerInvariant()).ToImmutableHashSet();
        return new TemplateDictionary(dict, extensions);
    }
}



public static class Input
{
    public const string SOURCE_START = "```json";
    public const string SOURCE_END = "```";
    public const string FRONTMATTER_SEP = "***"; // markdown hline


    public static DirectoryInfo? FindRoot(VfsRead vfs, DirectoryInfo? start)
    {
        var current = start;

        while (current != null && vfs.Exists(current.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION)) == false)
        {
            current = current.Parent;
        }

        return current;
    }


    private static Post? ParsePost(Run run, IEnumerable<string> lines, FileInfo file, ImmutableArray<string> relativePath, IDocumentParser markdown)
    {
        var (frontmatter, markdownContent) = ParsePostToTuple(run, lines, file);

        if (frontmatter == null)
        {
            return null;
        }

        return CreatePost(file, relativePath, markdown, markdownContent, frontmatter);
    }


    public static (FrontMatter? frontmatter, string markdownContent) ParsePostToTuple(Run run,
        IEnumerable<string> lines,
        FileInfo file)
    {
        var (frontmatterSource, markdownSource) = ParseGenericPostData(lines, file, FRONTMATTER_SEP,
            lt => lt is SOURCE_START or SOURCE_END, skips: 0);

        var frontmatter = JsonUtil.Parse<FrontMatter>(run, file, frontmatterSource);
        return (frontmatter, markdownSource);
    }

    public static string PostToFileData(Post post)
    {
        var json = JsonUtil.Write(post.Front);
        return string.Join('\n', SOURCE_START, json, SOURCE_END, FRONTMATTER_SEP, post.Markdown);
    }


    internal static Post CreatePost(FileInfo file, ImmutableArray<string> relativePath, IDocumentParser markdown, string markdownContent,
        FrontMatter frontmatter)
    {
        var markdownDocument = markdown.Parse(markdownContent);
        var markdownHtml = markdownDocument.ToHtml();
        var markdownText = markdownDocument.ToPlainText();

        if (string.IsNullOrEmpty(frontmatter.Summary))
        {
            // hacky way to generate a summary
            const string ELLIPSIS = "...";
            const int WORDS_IN_AUTO_SUMMARY = 25;

            var linesWithoutEndingDot = markdownText
                .Split('\n', StringSplitOptions.TrimEntries)
                .Select(x => x.TrimEnd('.').Trim()); // split into lines and remove ending dot
            // todo(Gustav): normalize whitespace
            var sentences = string.Join(". ", linesWithoutEndingDot); // join into a long string again with a dot at the end
            var summary = string.Join(' ', sentences.Split(' ').Take(WORDS_IN_AUTO_SUMMARY)) + ELLIPSIS;
            frontmatter.Summary = summary.Length < markdownText.Length
                    ? summary
                    : markdownText
                ;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);

        return new Post(Guid.NewGuid(), nameWithoutExtension == Constants.INDEX_NAME,
            relativePath.Add(nameWithoutExtension), frontmatter, file, nameWithoutExtension, markdownHtml, markdownText, markdownContent);
    }


    internal static (string frontmatter, string markdown) ParseGenericPostData(IEnumerable<string> lines, FileInfo file, string contentSeparator, Func<string, bool> frontMatterIgnores, int skips)
    {
        var frontmatterJson = new StringBuilder();
        var markdownContent = new StringBuilder();
        var parsingFrontmatter = true;

        foreach (var line in lines.Skip(skips))
        {
            if (parsingFrontmatter)
            {
                var lt = line.Trim();
                if (lt.Contains(contentSeparator))
                {
                    parsingFrontmatter = false;
                    continue;
                }

                if (frontMatterIgnores(lt))
                {
                    continue;
                }

                frontmatterJson.AppendLine(line);
            }
            else
            {
                markdownContent.AppendLine(line);
            }
        }

        return (frontmatterJson.ToString(), markdownContent.ToString());
    }


    public static async Task<SiteData?> LoadSiteData(Run run, VfsRead vfs, DirectoryInfo root)
    {
        var path = root.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION);
        return await JsonUtil.Load<SiteData>(run, vfs, path);
    }


    public static async Task<Site?> LoadSite(Run run, VfsRead vfs, DirectoryInfo root)
    {
        var data = await LoadSiteData(run, vfs, root);
        if (data == null) { return null; }

        IDocumentParser markdown = data.UseMarkdeep ? new MarkdeepParser() : new MarkdownParser();

        var content = await LoadDir(run, vfs, Constants.GetContentDirectory(root), ImmutableArray.Create<string>(), true, markdown);
        if (content == null) { return null; }

        return new Site(data, content);
    }


    private static async Task<Dir?> LoadDir(Run run, VfsRead vfs, DirectoryInfo root, ImmutableArray<string> relativePaths, bool isContentFolder, IDocumentParser markdown)
    {
        var name = root.Name;
        var relativePathsIncludingSelf = isContentFolder ? relativePaths : relativePaths.Add(name);

        var markdownFiles = vfs.GetFiles(root).Where(f => f.Extension == ".md");
        var postFiles = await LoadPosts(run, vfs, markdownFiles, relativePathsIncludingSelf, markdown).ToListAsync();
        var dirs = await LoadDirsWithoutNulls(run, vfs, vfs.GetDirectories(root), relativePathsIncludingSelf, markdown).ToListAsync();

        // remove dirs that only contain a index
        var dirsAsPosts = dirs.Where(dir => dir.Posts.Length == 1 && dir.Posts[0].Name == Constants.INDEX_NAME).ToImmutableArray();
        var dirsToRemove = dirsAsPosts.Select(dir => dir.Id).ToImmutableHashSet();
        dirs.RemoveAll(dir => dirsToRemove.Contains(dir.Id));

        // "move" those index pages one level up and promote to regular pages
        var additionalPosts = dirsAsPosts.Select(dir => dir.Posts[0])
            .Select(post => new {Post=post, Name=post.SourceFile.Directory?.Name})
            .Where(data => data.Name != null, data => run.WriteError($"{data.Post.Name} is missing a directory: {data.Post.SourceFile}"))
            .Select(data => data.Post with { IsIndex = false, RelativePath = data.Post.RelativePath.PopBack(), Name = data.Name! })
            ;

        // todo(Gustav): if dir is missing a entry, optionally add a empty _index page

        // todo(Gustav): figure out a better Dir title
        var posts = postFiles.Concat(additionalPosts).OrderByDescending(p => p.Front.Date).ToImmutableArray();
        return new Dir(Guid.NewGuid(), name, name, posts, dirs.ToImmutableArray());

        static async IAsyncEnumerable<Dir> LoadDirsWithoutNulls(Run run, VfsRead vfs, IEnumerable<DirectoryInfo> dirs, ImmutableArray<string> relativePaths, IDocumentParser markdown)
        {
            foreach (var d in dirs)
            {
                if (d == null) { continue; }

                var dir = await LoadDir(run, vfs, d, relativePaths, false, markdown);
                if (dir == null) { continue; }

                yield return dir;
            }
        }

        static async IAsyncEnumerable<Post> LoadPosts(Run run, VfsRead vfs, IEnumerable<FileInfo> files, ImmutableArray<string> relativePaths, IDocumentParser markdown)
        {
            foreach (var f in files)
            {
                var post = ParsePost(run, (await vfs.ReadAllTextAsync(f)).Split('\n'), f, relativePaths, markdown);
                if (post == null) { continue; }

                yield return post;
            }
        }
    }
}
