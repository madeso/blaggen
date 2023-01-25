using Spectre.Console;
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

        this.Extensions = ex;
        this.TemplateDict = td;
    }

    public static async Task<Templates> Load(Run run, VfsRead vfs, DirectoryInfo root)
    {
        var TemplateFolder = root.GetDir("templates");
        var ContentFolder = Input.GetContentDirectory(root);

        var templateFiles = vfs.GetFilesRec(TemplateFolder)
            .Where(f => f.Name.Contains(Constants.MUSTACHE_TEMPLATE_POSTFIX))
            .ToImmutableArray();

        var unloadedFiles = templateFiles
            .Select(async file => new { File = file, Contents = await file.LoadFileOrNull(run, vfs) });
        var td = (await Task.WhenAll(unloadedFiles))
            .Where(x => x.Contents != null)
            .ToImmutableDictionary(x => x.File.FullName, x => x.Contents!)
            ;
        var ext = templateFiles.Select(file => file.Extension.ToLowerInvariant()).ToImmutableHashSet();
        return new Templates(TemplateFolder, ContentFolder, td, ext);
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

    private static async Task<Post?> ParsePost(Run run, VfsRead vfs, FileInfo file, ImmutableArray<string> relativePath, Markdown markdown)
    {
        var lines = (await vfs.ReadAllTextAsync(file)).Split('\n');

        var frontmatterJson = new StringBuilder();
        var markdownContent = new StringBuilder();
        var parsingFrontmatter = true;

        foreach (var line in lines)
        {
            if (parsingFrontmatter)
            {
                var lt = line.Trim();
                if (lt.Contains(FRONTMATTER_SEP)) { parsingFrontmatter = false; continue; }
                if (lt == SOURCE_START || lt == SOURCE_END) { continue; }
                frontmatterJson.AppendLine(line);
            }
            else
            {
                markdownContent.AppendLine(line);
            }
        }

        var frontmatter = JsonUtil.Parse<FrontMatter>(run, file, frontmatterJson.ToString());
        if (frontmatter == null) { return null; }

        var markdownDocument = markdown.Parse(markdownContent.ToString());
        var markdownHtml = markdown.ToHtml(markdownDocument);
        var markdownText = markdown.ToPlainText(markdownDocument);

        if (string.IsNullOrEmpty(frontmatter.Summary))
        {
            // hacky way to generate a summary
            const string ELLIPSIS = "...";
            const int WORDS_IN_AUTO_SUMMARY = 25;

            var linesWithoutEndingDot = markdownText
                .Split('\n', StringSplitOptions.TrimEntries).Select(x => x.TrimEnd('.').Trim()); // split into lines and remove ending dot
            // todo(Gustav): normalize whitespace
            var sentances = string.Join(". ", linesWithoutEndingDot); // join into a long string again with a dot at the end
            var summary = string.Join(' ', sentances.Split(' ').Take(WORDS_IN_AUTO_SUMMARY)) + ELLIPSIS;
            frontmatter.Summary = summary.Length < markdownText.Length
                ? summary
                : markdownText
                ;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);

        return new Post(Guid.NewGuid(), nameWithoutExtension == Constants.INDEX_NAME, relativePath.Add(nameWithoutExtension), frontmatter, file, nameWithoutExtension, markdownHtml, markdownText);
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

    record PostWithOptionalName(Post Post, string? Name);

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

        var posts = postFiles.Concat(additionalPosts).OrderByDescending(p => p.Front.Date).ToImmutableArray();
        return new Dir(Guid.NewGuid(), name, posts, dirs.ToImmutableArray());

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
                if (f == null) { continue; }

                var post = await ParsePost(run, vfs, f, relativePaths, markdown);
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
