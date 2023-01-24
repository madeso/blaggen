using Markdig;
using Markdig.Renderers;
using Spectre.Console;
using Spectre.Console.Cli;
using Stubble.Core;
using Stubble.Core.Builders;
using Stubble.Core.Exceptions;
using Stubble.Core.Settings;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


// ----------------------------------------------------------------------------------------------------------------------------
// commandline handling and main runners

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<InitSiteCommand>("init");
            config.AddCommand<NewPostCommand>("new");
            config.AddCommand<GenerateCommand>("generate");
        });
        return await app.RunAsync(args);
    }
}

[Description("Generate a new site in the curent directory")]
internal sealed class InitSiteCommand : AsyncCommand<InitSiteCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var run = new Run();
        var vfs = new VfsWrite();

        var existingSite = SiteData.FindRootFromCurentDirectory();
        if (existingSite != null)
        {
            run.WriteError($"Site already exists at {existingSite.FullName}");
            return -1;
        }

        var site = new SiteData { Name = "My new blog" };
        var json = JsonUtil.Write(site);
        var path = Path.Join(Environment.CurrentDirectory, SiteData.PATH);
        await vfs.WriteAllTextAsync(new FileInfo(path), json);

        // todo(Gustav): generate basic templates
        return 0;
    }
}

[Description("Generate a new page")]
internal sealed class NewPostCommand : AsyncCommand<NewPostCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Relative path to post")]
        [CommandArgument(1, "<path>")]
        public string Path { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var run = new Run();
        var vfs = new VfsRead();
        var vfsWrite = new VfsWrite();

        var path = new FileInfo(args.Path);
        if (path.Exists) { run.WriteError($"Post {path} already exit"); return -1; }

        var pathDir = path.Directory;
        if (pathDir == null) { run.WriteError($"Post {path} isn't rooted"); return -1; }

        var root = SiteData.FindRoot(pathDir);
        if (root == null) { run.WriteError("Unable to find root"); return -1; }

        var site = await Input.LoadSiteData(run, vfs, root);
        if (site == null) { return -1; }

        // todo(Gustav): create _index.md for each directory depending on setting
        var contentFolder = Input.GetContentDirectory(root);
        var relative = Path.GetRelativePath(contentFolder.FullName, path.FullName);
        if (relative.Contains("..")) { run.WriteError($"Post {pathDir} must be a subpath of {contentFolder}"); return -1; }

        var postNameBase = Path.GetFileNameWithoutExtension(path.Name);
        if(postNameBase == Constants.INDEX_NAME)
        {
            postNameBase = path.Directory!.Name;
        }
        var title = site.CultureInfo.TextInfo.ToTitleCase(postNameBase.Replace('-', ' ').Replace('_', ' '));
        var frontmatter = JsonUtil.Write(new FrontMatter { Title = title });
        var content = $"{Input.SOURCE_START}\n{frontmatter}\n{Input.SOURCE_END}\n{Input.FRONTMATTER_SEP}\n# {title}";

        path.Directory!.Create();
        await vfsWrite.WriteAllTextAsync(path, content);

        Debug.Assert(run.ErrorCount == 0);
        AnsiConsole.MarkupLineInterpolated($"Wrote [blue]${path.FullName}[/]");
        return 0;
    }
}

[Description("Genrate or publish the site")]
internal sealed class GenerateCommand : AsyncCommand<GenerateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var ret = 0;
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                ret = await Run(ctx, args);
            });
        return ret;
    }

    private async Task<int> Run(StatusContext ctx, Settings args)
    {
        var run = new Run();
        var vfs = new VfsRead();
        var vfsWrite = new VfsWrite();

        var root = SiteData.FindRootFromCurentDirectory();
        if (root == null) { run.WriteError("Unable to find root"); return -1; }

        var timeStart = DateTime.Now;

        ctx.Status("Parsing directory");
        var site = await Input.LoadSite(run, vfs, root, new Markdown());
        if (site == null) { return -1; }

        var publicDir = root.GetDir("public");
        var templates = await Templates.Load(run, vfs, root);
        var unloadedPartials = root.GetDir("partials").EnumerateFiles()
            .Select(async file => new { Name = Path.GetFileNameWithoutExtension(file.Name), Content = await vfs.ReadAllTextAsync(file)})
            ;
        var partials = (await Task.WhenAll(unloadedPartials))
            .Select(d => new KeyValuePair<string, object>(d.Name, new Func<object>(() => d.Content)))
            .ToImmutableArray()
            ;

        ctx.Status("Writing data to disk");
        var pagesGenerated = await Generate.WriteSite(run, vfsWrite, site, publicDir, templates, partials);
        // todo(Gustav): copy static files

        var timeEnd = DateTime.Now;
        var timeTaken = timeEnd - timeStart;
        AnsiConsole.MarkupLineInterpolated($"Wrote [green]{pagesGenerated}[/] files in [blue]{timeTaken}[/]");

        return run.ErrorCount > 0 ? -1 : 0;
    }
}

class VfsRead
{
    internal async Task<string> ReadAllTextAsync(FileInfo fullName)
    {
        return await File.ReadAllTextAsync(fullName.FullName);
    }
}

class VfsWrite
{
    public async Task WriteAllTextAsync(FileInfo path, string contents)
    {
        await File.WriteAllTextAsync(path.FullName, contents);
    }
}

class Templates
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

        var templateFiles = TemplateFolder.EnumerateFiles("*.*", SearchOption.AllDirectories)
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

    internal string RenderMustache(Run run, string template, FileInfo templateFile, Generate.PageData data, Site site)
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

    internal string? GetTemplateOrNull(FileInfo file)
    {
        if(TemplateDict.TryGetValue(file.FullName, out var contents))
        {
            return contents;
        }
        return null;
    }
}

class Markdown
{
    private MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    internal Markdig.Syntax.MarkdownDocument Parse(string content)
    {
        return Markdig.Markdown.Parse(content);
    }

    internal string ToHtml(Markdig.Syntax.MarkdownDocument document)
    {
        return document.ToHtml(pipeline);
    }

    internal string ToPlainText(Markdig.Syntax.MarkdownDocument document)
    {
        // stolen from Markdig implementation of ToPlainText since that isn't exposed
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer)
        {
            EnableHtmlForBlock = false,
            EnableHtmlForInline = false,
            EnableHtmlEscape = false,
        };
        pipeline.Setup(renderer);

        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }
}

// ----------------------------------------------------------------------------------------------------------------------------
// Data and basic functions

class SiteData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("culture")]
    public string Culture { get; set; } = "en-US";

    [JsonPropertyName("short_date_format")]
    public string ShortDateFormat { get; set; } = "g";

    [JsonPropertyName("long_date_format")]
    public string LongDateFormat { get; set; } = "G";

    [JsonPropertyName("url")]
    public string BaseUrl { get; set; } = string.Empty;
    public string Url => BaseUrl.EndsWith('/') ? BaseUrl.TrimEnd('/') : BaseUrl;

    public const string PATH = "site.blaggen.json";

    // find root that contains the root file (or null)
    public static DirectoryInfo? FindRootFromCurentDirectory()
    {
        return FindRoot(new DirectoryInfo(Environment.CurrentDirectory));
    }

    public static DirectoryInfo? FindRoot(DirectoryInfo? start)
    {
        DirectoryInfo? current = start;

        while (current != null && current.GetFile(PATH).Exists == false)
        {
            current = current.Parent;
        }

        return current;
    }

    public CultureInfo CultureInfo
    {
        get
        {
            return new CultureInfo(Culture, false);
        }
    }

    public string ShortDateToString(DateTime dt)
    {
        return dt.ToString(ShortDateFormat, this.CultureInfo);
    }

    public string LongDateToString(DateTime dt)
    {
        return dt.ToString(ShortDateFormat, this.CultureInfo);
    }
}

class FrontMatter
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; } = DateTime.Now;

    [JsonPropertyName("tags")]
    public HashSet<string> Tags { get; set; } = new();
}

// todo(Gustav): add associated files to be generated...
record Post(Guid Id, bool IsIndex, ImmutableArray<string> RelativePath, FrontMatter Front, FileInfo SourceFile, string Name, string MarkdownHtml, string MarkdownPlainText);
record Dir(Guid Id, string Name, ImmutableArray<Post> Posts, ImmutableArray<Dir> Dirs);
record Site(SiteData Data, Dir Root);

// ----------------------------------------------------------------------------------------------------------------------------
// App logic

internal static class Constants
{
    public const string MUSTACHE_TEMPLATE_POSTFIX = ".mustache";

    public const string DIR_TEMPLATE = "_dir" + MUSTACHE_TEMPLATE_POSTFIX;
    public const string POST_TEMPLATE = "_post" + MUSTACHE_TEMPLATE_POSTFIX;


    public const string INDEX_NAME = "_index";
}

internal static class Input
{
    public const string SOURCE_START = "```json";
    public const string SOURCE_END = "```";
    public const string FRONTMATTER_SEP = "***"; // markdown hline

    private static async Task<Post?> ParsePost(Run run, VfsRead vfs, FileInfo file, ImmutableArray<string> relativePath, Markdown markdown)
    {
        var lines = (await vfs.ReadAllTextAsync(file)).Split('\n');

        var frontmatterJson = new StringBuilder();
        var markdownContent = new StringBuilder();
        var parsingFrontmatter = true;
        
        foreach (var line in lines)
        {
            if(parsingFrontmatter)
            {
                var lt = line.Trim();
                if (lt.Contains(FRONTMATTER_SEP)) { parsingFrontmatter = false;  continue; }
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
        var path = root.GetFile(SiteData.PATH);
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

        var postFiles = await LoadPosts(run, vfs, root.GetFiles("*.md", SearchOption.TopDirectoryOnly), relativePathsIncludingSelf, markdown).ToListAsync();
        var dirs = await LoadDirsWithoutNulls(run, vfs, root.GetDirectories(), relativePathsIncludingSelf, markdown).ToListAsync();

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
            .Select(data => new Post(data.Post.Id, false, data.Post.RelativePath.PopBack(),  data.Post.Front, data.Post.SourceFile, data.Name!, data.Post.MarkdownHtml, data.Post.MarkdownPlainText))
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

public static class Generate
{
    internal class SummaryForPost
    {
        public readonly string title;
        public readonly string time_short;
        public readonly string time_long;
        public readonly string name;
        public readonly string summary;
        public readonly string full_html;
        public readonly string full_text;

        public SummaryForPost(Post post, Site site)
        {
            this.title = post.Front.Title;
            this.time_short = site.Data.ShortDateToString(post.Front.Date);
            this.time_long = site.Data.LongDateToString(post.Front.Date);
            this.name = post.Name;
            this.summary = post.Front.Summary;
            this.full_html = post.MarkdownHtml;
            this.full_text = post.MarkdownPlainText;
        }
    }

    internal record RootLink(string Name, string Url, bool IsSelected);

    internal class PageData
    {
        public readonly string title;
        public readonly string summary;
        public readonly string url;

        public readonly string content_html;
        public readonly string content_text;
        public readonly string time_short;
        public readonly string time_long;
        public readonly Dictionary<string, object> partial;
        public readonly List<SummaryForPost> pages;
        public readonly List<RootLink> roots;

        public PageData(Site site, Post post, ImmutableArray<RootLink> rootLinks, Dictionary<string, object> partials,
            List<SummaryForPost> summaries)
        {
            this.title = post.Front.Title;
            this.summary = post.Front.Summary;
            this.roots = rootLinks.ToList();

            var rel = post.IsIndex ? post.RelativePath.PopBack() : post.RelativePath;
            var relative = string.Join('/', rel.Add("index"));
            this.url = $"{site.Data.Url}/{relative}";
            content_html = post.MarkdownHtml;
            content_text = post.MarkdownPlainText;
            time_short = site.Data.ShortDateToString(post.Front.Date);
            time_long = site.Data.LongDateToString(post.Front.Date);

            this.partial = partials;
            this.pages = summaries;
        }
        // todo(Gustav): add more data
        // todo(Gustav): generate full url
    }

    internal static async Task<int> WriteSite(Run run, VfsWrite vfs, Site site, DirectoryInfo publicDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials)
    {
        var owners = ImmutableArray.Create<Dir>();
        var roots = site.Root.Dirs
            .Select(dir => new RootLink(dir.Name, $"{dir.Name}/index.html", false)).Concat(site.Root.Posts
            .Select(fil => new RootLink(fil.Name, $"{fil.Name}/index.html", false))).ToImmutableArray()
            ;
        return await WriteDir(run, vfs, site, roots, site.Root, publicDir, templates, partials, owners, true);
    }

    private static async Task<int> WriteDir(Run run, VfsWrite vfs, Site site, ImmutableArray<RootLink> rootLinksBase, Dir dir,
        DirectoryInfo targetDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials,
        ImmutableArray<Dir> owners, bool isRoot)
    {
        int count = 0;
        var ownersWithSelf = isRoot ? owners : owners.Add(dir); // if this is root, don't add the "content" folder
        var templateFolders = GenerateTemplateFolders(templates, ownersWithSelf);

        foreach (var subdir in dir.Dirs)
        {
            var rootLinks = isRoot ? rootLinksBase.Select(x => IsSelected(x, subdir.Name)).ToImmutableArray() : rootLinksBase;
            count += await WriteDir(run, vfs, site, rootLinks, subdir, targetDir.GetDir(subdir.Name), templates, partials, ownersWithSelf, false);
        }

        var summaries = dir.Posts.Where(post => post.IsIndex == false).Select(post => new SummaryForPost(post, site)).ToImmutableArray();
        foreach (var post in dir.Posts)
        {
            var rootLinks = isRoot ? rootLinksBase.Select(x => IsSelected(x, post.Name)).ToImmutableArray() : rootLinksBase;
            var extraSteps = 0;
            var rootLinksWithLinks = rootLinks.Select(x => StepBack(x, owners.Length + extraSteps)).ToImmutableArray();
            // todo(Gustav): paginate index using Chunk(size)
            count += await WritePost(run, vfs, site, rootLinksWithLinks, templateFolders, post, summaries, targetDir, templates, partials);
        }

        return count;

        static RootLink IsSelected(RootLink r, string s)
        {
            var isSelected = r.Name == s;
            return new RootLink(r.Name, isSelected ? $"../{r.Url}" : r.Url, isSelected);
        }

        static RootLink StepBack(RootLink r, int steps)
        {
            var sb = new StringBuilder();
            for(int i=0; i<steps; i+=1)
            {
                sb.Append("../");
            }
            return new RootLink(r.Name, $"{sb}{r.Url}", r.IsSelected);
        }
    }

    private static async Task<int> WritePost(Run run, VfsWrite vfs, Site site, ImmutableArray<RootLink> rootLinks,
        ImmutableArray<DirectoryInfo> templateFolders, Post post, ImmutableArray<SummaryForPost> summaries,
        DirectoryInfo postsDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials)
    {
        int pagesGenerated = 0;
        var data = new PageData(site, post, rootLinks, partials.ToDictionary(k => k.Key, k => k.Value), summaries.ToList());
        var templateName = post.IsIndex ? Constants.DIR_TEMPLATE : Constants.POST_TEMPLATE;
        var destDir = post.IsIndex ? postsDir : postsDir.GetDir(post.Name);

        foreach (var ext in templates.Extensions)
        {
            var templateFiles = templateFolders
                .Select(dir => dir.GetFile(templateName + ext))
                .Select(file => new FileWithOptionalContent(file, templates.GetTemplateOrNull(file)))
                .ToImmutableArray()
                ;

            var path = destDir.GetFile("index" + ext);
            var selected = templateFiles.Where(file => file.Content != null).FirstOrDefault();
            if (selected == null)
            {
                var tried = string.Join(' ', templateFiles.Select(x => DisplayNameForFile(x.File)));
                run.WriteError($"Unable to generate {DisplayNameForFile(post.SourceFile)} to {DisplayNameForFile(path)} for {ext}, tried to use: {tried}");
                continue;
            }

            destDir.Create();

            var renderedPage = templates.RenderMustache(run, selected.Content!, selected.File, data, site);
            await vfs.WriteAllTextAsync(path, renderedPage);
            AnsiConsole.MarkupLineInterpolated($"Generated {DisplayNameForFile(path)} from {DisplayNameForFile(post.SourceFile)} and {DisplayNameForFile(selected.File)}");
            pagesGenerated += 1;
        }

        return pagesGenerated;
    }

    record FileWithOptionalContent(FileInfo File, string? Content);

    private static string DisplayNameForFile(FileInfo file) => Path.GetRelativePath(Environment.CurrentDirectory, file.FullName);


    private static ImmutableArray<DirectoryInfo> GenerateTemplateFolders(Templates templates, IEnumerable<Dir> owners)
    {
        var root = new DirectoryInfo[] { templates.TemplateFolder };
        var children = owners
            // template paths going from current dir to root
            .Accumulate(ImmutableArray.Create<string>(), (dir, arr) => arr.Add(dir.Name))
            .Reverse()
            // convert to actual directory
            .Select(arr => templates.TemplateFolder.GetSubDirs(arr))
            ;
        var ret = children.Concat(root).ToImmutableArray();
        return ret;
    }
}

// ----------------------------------------------------------------------------------------------------------------------------
// Util stuff

public class Run
{
    public int ErrorCount { get; set; } = 0;

    internal void WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]ERROR[/]: {message}");
        ErrorCount += 1;
    }
}

public static class FileExtensions
{
    internal static DirectoryInfo GetDir(this DirectoryInfo dir, string sub)
    {
        return new DirectoryInfo(Path.Join(dir.FullName, sub));
    }

    internal static DirectoryInfo GetSubDirs(this DirectoryInfo dir, IEnumerable<string> sub)
    {
        return sub.Aggregate(dir, (current, name) => current.GetDir(name));
    }

    internal static FileInfo GetFile(this DirectoryInfo dir, string file)
    {
        return new FileInfo(Path.Join(dir.FullName, file));
    }

    internal async static Task<string?> LoadFileOrNull(this FileInfo path, Run run, VfsRead vfs)
    {
        try { return await vfs.ReadAllTextAsync(path); }
        catch (Exception x)
        {
            run.WriteError($"Failed to load {path.FullName}: {x.Message}");
            return null;
        }
    }

    internal static async Task<string?> LoadFileSilentOrNull(this FileInfo path, VfsRead vfs)
    {
        try { return await vfs.ReadAllTextAsync(path); }
        catch
        {
            return null;
        }
    }
}

public static class DictionaryExtensions
{
    public static void AddRange<K, V>(this Dictionary<K, V> data, IEnumerable<KeyValuePair<K, V>> list)
        where K : class
        where V : class
    { foreach (var (k, v) in list) { data.Add(k, v); } }
}

public static class ImmutableArrayExtensions
{
    public static ImmutableArray<T> PopBack<T>(this ImmutableArray<T> data)
    {
        if(data.Length == 0) { return data; }
        var ret = data.RemoveAt(data.Length-1);
        return ret;
    }
}

public static class IterTools
{
    // returns: initial+p0, initial+p0+p1, initial+p0+p1+p2 ...
    public static IEnumerable<R> Accumulate<T, R>(this IEnumerable<T> src, R initial, Func<T, R, R> add)
    {
        var current = initial;
        foreach (var t in src)
        {
            current = add(t, current);
            yield return current;
        }
    }

    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> asyncEnumerable)
    {
        if (null == asyncEnumerable)
            throw new ArgumentNullException(nameof(asyncEnumerable));

        var list = new List<T>();
        await foreach (var t in asyncEnumerable)
        {
            list.Add(t);
        }

        return list;
    }
}


public static class JsonUtil
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T? Parse<T>(Run run, FileInfo file, string content)
        where T : class
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<T>(content, Options);
            if (loaded == null) { throw new Exception("internal error"); }
            return loaded;
        }
        catch (JsonException err)
        {
            run.WriteError($"Unable to parse json {file}: {err.Message}");
            return null;
        }
    }

    internal static async Task<T?> Load<T>(Run run, VfsRead vfs, FileInfo path)
        where T : class
    {
        var content = await vfs.ReadAllTextAsync(path);
        return Parse<T>(run, path, content);
    }

    internal static string Write<T>(T self)
    {
        return JsonSerializer.Serialize<T>(self, Options);
    }
}
