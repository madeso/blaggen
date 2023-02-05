using Stubble.Core;
using Stubble.Core.Builders;
using Stubble.Core.Exceptions;
using Stubble.Core.Settings;
using System.Collections.Immutable;
using System.Text;

namespace Blaggen;


public class Templates
{
    private Templates(DirectoryInfo tf, DirectoryInfo cf, ImmutableDictionary<string, string> td, ImmutableHashSet<string> ex)
    {
        TemplateFolder = tf;
        ContentFolder = cf;
        stubble = new StubbleBuilder().Build();

        Extensions = ex;
        TemplateDict = td;
    }

    public static async Task<Templates> Load(Run run, VfsRead vfs, DirectoryInfo root)
    {
        var templateFolder = CalculateTemplateDirectory(root);
        var contentFolder = Input.GetContentDirectory(root);

        var templateFiles = vfs.GetFilesRec(templateFolder)
            .Where(f => f.Name.Contains(Constants.MUSTACHE_TEMPLATE_POSTFIX))
            .ToImmutableArray();

        var unloadedFiles = templateFiles
            .Select(async file => new { File = file, Contents = await file.LoadFileOrNull(run, vfs) });
        var td = (await Task.WhenAll(unloadedFiles))
            .Where(x => x.Contents != null)
            .ToImmutableDictionary(x => x.File.FullName, x => x.Contents!)
            ;
        var ext = templateFiles.Select(file => file.Extension.ToLowerInvariant()).ToImmutableHashSet();
        return new Templates(templateFolder, contentFolder, td, ext);
    }

    public static DirectoryInfo CalculateTemplateDirectory(DirectoryInfo root)
    {
        return root.GetDir("templates");
    }

    private readonly StubbleVisitorRenderer stubble;

    public DirectoryInfo TemplateFolder { get; }
    public DirectoryInfo ContentFolder { get; }
    public ImmutableHashSet<string> Extensions { get; }
    public ImmutableDictionary<string, string> TemplateDict { get; } // can't use FileInfo as a key

    public string RenderMustache(Run run, string template, FileInfo templateFile, Generate.PageData data, Site site)
    {
        // todo(Gustav): switch to compiled patterns? config?
        try
        {
            var settings = new RenderSettings
            {
                ThrowOnDataMiss = true,
                CultureInfo = site.Data.CultureInfo,
            };
            return stubble.Render(template, data, settings);
        }
        catch (StubbleException err)
        {
            run.WriteError($"{templateFile.FullName}: {err.Message}");

            // todo(Gustav): can we switch settings and render a invalid page here? is it worthwhile?
            return "";
        }
    }

    public string? GetTemplateOrNull(FileInfo file)
    {
        if (TemplateDict.TryGetValue(file.FullName, out var contents))
        {
            return contents;
        }
        return null;
    }
}



public static class Input
{
    public const string SOURCE_START = "```json";
    public const string SOURCE_END = "```";
    public const string FRONTMATTER_SEP = "***"; // markdown hline


    public static DirectoryInfo? FindRoot(VfsRead vfs, DirectoryInfo? start)
    {
        DirectoryInfo? current = start;

        while (current != null && vfs.Exists(current.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION)) == false)
        {
            current = current.Parent;
        }

        return current;
    }

    private static Post? ParsePost(Run run, IEnumerable<string> lines, FileInfo file, ImmutableArray<string> relativePath, Markdown markdown)
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

    internal static Post CreatePost(FileInfo file, ImmutableArray<string> relativePath, Markdown markdown, string markdownContent,
        FrontMatter frontmatter)
    {
        var markdownDocument = markdown.Parse(markdownContent);
        var markdownHtml = markdown.ToHtml(markdownDocument);
        var markdownText = markdown.ToPlainText(markdownDocument);

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
            relativePath.Add(nameWithoutExtension), frontmatter, file, nameWithoutExtension, markdownHtml, markdownText);
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

    public static async Task<Site?> LoadSite(Run run, VfsRead vfs, DirectoryInfo root, Markdown markdown)
    {
        var data = await LoadSiteData(run, vfs, root);
        if (data == null) { return null; }

        var content = await LoadDir(run, vfs, GetContentDirectory(root), ImmutableArray.Create<string>(), true, markdown);
        if (content == null) { return null; }

        return new Site(data, content);
    }

    private record PostWithOptionalName(Post Post, string? Name);

    private static async Task<Dir?> LoadDir(Run run, VfsRead vfs, DirectoryInfo root, ImmutableArray<string> relativePaths, bool isContentFolder, Markdown markdown)
    {
        var name = root.Name;
        var relativePathsIncludingSelf = isContentFolder ? relativePaths : relativePaths.Add(name);

        var postFiles = await LoadPosts(run, vfs, vfs.GetFiles(root).Where(f => f.Extension == ".md"), relativePathsIncludingSelf, markdown).ToListAsync();
        var dirs = await LoadDirsWithoutNulls(run, vfs, vfs.GetDirectories(root), relativePathsIncludingSelf, markdown).ToListAsync();

        // remove dirs that only contain a index
        var dirsAsPosts = dirs.Where(dir => dir.Posts.Length == 1 && dir.Posts[0].Name == Constants.INDEX_NAME).ToImmutableArray();
        var dirsToRemove = dirsAsPosts.Select(dir => dir.Id).ToHashSet();
        dirs.RemoveAll(dir => dirsToRemove.Contains(dir.Id));

        // "move" those index pages one level up and promote to regular pages
        var additionalPostSrcs = dirsAsPosts.Select(dir => dir.Posts[0])
            .Select(post => new PostWithOptionalName(post, post.SourceFile.Directory?.Name))
            .ToImmutableArray();
        foreach (var data in additionalPostSrcs.Where(data => data.Name == null))
        { run.WriteError($"{data.Post.Name} is missing a directory: {data.Post.SourceFile}"); }
        var additionalPosts = additionalPostSrcs
            .Where(data => data.Name != null)
            .Select(data => new Post(data.Post.Id, false, data.Post.RelativePath.PopBack(), data.Post.Front, data.Post.SourceFile, data.Name!, data.Post.MarkdownHtml, data.Post.MarkdownPlainText))
            ;

        // todo(Gustav): if dir is missing a entry, optionally add a empty _index page

        // todo(Gustav): figure out a better Dir title
        var posts = postFiles.Concat(additionalPosts).OrderByDescending(p => p.Front.Date).ToImmutableArray();
        return new Dir(Guid.NewGuid(), name, name, posts, dirs.ToImmutableArray());

        static async IAsyncEnumerable<Dir> LoadDirsWithoutNulls(Run run, VfsRead vfs, IEnumerable<DirectoryInfo> dirs, ImmutableArray<string> relativePaths, Markdown markdown)
        {
            foreach (var d in dirs)
            {
                if (d == null) { continue; }

                var dir = await LoadDir(run, vfs, d, relativePaths, false, markdown);
                if (dir == null) { continue; }

                yield return dir;
            }
        }

        static async IAsyncEnumerable<Post> LoadPosts(Run run, VfsRead vfs, IEnumerable<FileInfo> files, ImmutableArray<string> relativePaths, Markdown markdown)
        {
            foreach (var f in files)
            {
                var post = ParsePost(run, (await vfs.ReadAllTextAsync(f)).Split('\n'), f, relativePaths, markdown);
                if (post == null) { continue; }

                yield return post;
            }
        }
    }

    public static DirectoryInfo GetContentDirectory(DirectoryInfo root)
    {
        return root.GetDir("content");
    }
}
