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
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


// ----------------------------------------------------------------------------------------------------------------------------
// commandline handling and main runners

var app = new CommandApp();
app.Configure(config =>
{
    config.AddCommand<InitSiteCommand>("init");
    config.AddCommand<NewPostCommand>("new");
    config.AddCommand<GenerateCommand>("generate");
});
return app.Run(args);


[Description("Generate a new site in the curent directory")]
internal sealed class InitSiteCommand : Command<InitSiteCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var site = new SiteData { Name= "My new blog" };
        var json = JsonUtil.Write(site);
        var path = Path.Join(Environment.CurrentDirectory, SiteData.PATH);
        File.WriteAllText(path, json);

        // todo(Gustav): generate basic templates
        return 0;
    }
}

[Description("Generate a new page")]
internal sealed class NewPostCommand : Command<NewPostCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Relative path to post")]
        [CommandArgument(1, "<path>")]
        public string Path { get; init; } = string.Empty;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var run = new Run();
        var root = SiteData.FindRoot(); // todo(Gustav): figure out root based on entered path, not current directory
        if(root == null) { run.WriteError("Unable to find root"); return -1; }

        var site = Input.LoadSiteData(run, root);
        if(site == null) { return -1; }

        var path = new FileInfo(args.Path);
        if (path.Exists) { run.WriteError($"Post {path} already exit"); return -1; }

        // todo(Gustav): create _index.md for each directory depending on setting
        var contentFolder = Input.GetContentDirectory(root);
        var relative = Path.GetRelativePath(contentFolder.FullName, path.FullName);
        if(relative.Contains("..")) { run.WriteError($"Post {path} must be a subpath of {contentFolder}"); return -1; }

        var title = site.CultureInfo.TextInfo.ToTitleCase(Path.GetFileNameWithoutExtension(path.Name));
        var frontmatter = JsonUtil.Write(new FrontMatter { Title = title });
        var content = $"{Input.SOURCE_START}\n{frontmatter}\n{Input.SOURCE_END}\n{Input.FRONTMATTER_SEP}\n# {title}";
        File.WriteAllText(path.FullName, content);

        Debug.Assert(run.ErrorCount == 0);
        return 0;
    }
}

[Description("Genrate or publish the site")]
internal sealed class GenerateCommand : Command<GenerateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var run = new Run();

        var root = SiteData.FindRoot();
        if (root == null) { run.WriteError("Unable to find root"); return -1; }

        var site = Input.LoadSite(run, root);
        if (site == null) { return -1; }

        var publicDir = root.GetDir("public");
        var templates = new Templates(run, root);
        var partials = root.GetDir("partials").EnumerateFiles()
            .Select(file => new {Name=Path.GetFileNameWithoutExtension(file.Name), Content=File.ReadAllText(file.FullName) })
            .Select(d => new KeyValuePair<string, object>($"partial_{d.Name}", new Func<object>(() => d.Content)))
            .ToImmutableArray()
            ;
        
        var timeStart = DateTime.Now;

        var pagesGenerated = Generate.WriteSite(run, site, publicDir, templates, partials);
        // todo(Gustav): copy static files

        var timeEnd = DateTime.Now;
        var timeTaken = timeEnd - timeStart;
        AnsiConsole.MarkupLineInterpolated($"Wrote [green]{pagesGenerated}[/] files in [blue]{timeTaken}[/]");

        return run.ErrorCount > 0 ? -1 : 0;
    }
}

class Templates
{
    public Templates(Run run, DirectoryInfo root)
    {
        TemplateFolder = root.GetDir("templates");
        ContentFolder = Input.GetContentDirectory(root);
        stubble = new StubbleBuilder().Build();

        var templateFiles = TemplateFolder.EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(f => f.Name.Contains(Constants.MUSTACHE_TEMPLATE_POSTFIX))
            .ToImmutableArray();

        this.Extensions = templateFiles.Select(file => file.Extension.ToLowerInvariant()).ToImmutableHashSet();
        this.TemplateDict = templateFiles
            .Select(file => new { File = file, Contents = file.LoadFileOrNull(run) })
            .Where(x => x.Contents != null)
            .ToImmutableDictionary(x => x.File, x => x.Contents!)
            ;
    }

    private readonly StubbleVisitorRenderer stubble;

    public DirectoryInfo TemplateFolder { get; }
    public DirectoryInfo ContentFolder { get; }
    public ImmutableHashSet<string> Extensions { get; }
    public ImmutableDictionary<FileInfo, string> TemplateDict { get; }

    internal string RenderMustache(Run run, string template, FileInfo templateFile, Dictionary<string, object> data, Site site)
    {
        // todo(Gustav): switch to compiled patterns? config?
        try
        {
            var settings = new RenderSettings
            {
                ThrowOnDataMiss = true, CultureInfo = site.Data.CultureInfo,
            };
            return stubble.Render(template, data, settings);
        }
        catch(StubbleException err)
        {
            run.WriteError($"{templateFile.FullName}: {err.Message}");

            // todo(Gustav): can we switch settings and render a invalid page here? is it worthwhile?
            return "";
        }
    }
}

// ----------------------------------------------------------------------------------------------------------------------------
// Data and basic functions

class SiteData
{
    [JsonPropertyName("name")]
    public string Name{ get; set; } = string.Empty;

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
    public static DirectoryInfo? FindRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(Environment.CurrentDirectory);

        while (current != null && File.Exists(Path.Join(current.FullName, PATH)) == false)
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
    public HashSet<string> Tags {get; set;} = new();
}

// todo(Gustav): add associated files to be generated...
record Post(Guid Id, bool IsIndex, FrontMatter Front, FileInfo SourceFile, string Name, string MarkdownHtml, string MarkdownPlainText);
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

    private static Post? ParsePost(Run run, FileInfo file)
    {
        var lines = File.ReadLines(file.FullName).ToImmutableArray();

        var frontmatterJson = new StringBuilder();
        var frontMaterLines = 0;
        foreach(var line in lines)
        {
            frontMaterLines += 1;
            var lt = line.Trim();
            if (lt.Contains(FRONTMATTER_SEP)) { break; }
            if (lt == SOURCE_START || lt == SOURCE_END) { continue; }
            frontmatterJson.AppendLine(line);
        }

        var content = string.Join('\n', lines.Skip(frontMaterLines));
        var frontmatter = JsonUtil.Parse<FrontMatter>(run, file.FullName, frontmatterJson.ToString());
        if(frontmatter == null) { return null; }

        var markdownHtml = Markdig.Markdown.ToHtml(content);
        var markdownText = Markdig.Markdown.ToPlainText(content);

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

        return new Post(Guid.NewGuid(), nameWithoutExtension == Constants.INDEX_NAME, frontmatter, file, nameWithoutExtension, markdownHtml, markdownText);
    }

    public static SiteData? LoadSiteData(Run run, DirectoryInfo root)
    {
        var path = Path.Join(root.FullName, SiteData.PATH);
        return JsonUtil.Load<SiteData>(run, path);
    }

    public static Site? LoadSite(Run run, DirectoryInfo root)
    {
        var data = LoadSiteData(run, root);
        if (data == null) { return null; }

        var content = LoadDir(run, GetContentDirectory(root));
        if (content == null) { return null; }

        return new Site(data, content);
    }

    record PostWithOptionalName(Post Post, string? Name);

    private static Dir? LoadDir(Run run, DirectoryInfo root)
    {
        var postFiles = LoadPosts(run, root.GetFiles("*.md", SearchOption.TopDirectoryOnly));
        var dirs = LoadDirsWithoutNulls(run, root.GetDirectories()).ToList();

        var dirsAsPosts = dirs.Where(dir => dir.Posts.Length == 1 && dir.Posts[0].Name == Constants.INDEX_NAME).ToImmutableArray();
        
        var dirsToRemove = dirsAsPosts.Select(dir => dir.Id).ToHashSet();
        dirs.RemoveAll(dir => dirsToRemove.Contains(dir.Id));

        var additionalPostSrcs = dirsAsPosts.Select(dir => dir.Posts[0])
            .Select(post => new PostWithOptionalName(post, post.SourceFile.DirectoryName))
            .ToImmutableArray();
        foreach(var data in additionalPostSrcs.Where(data => data.Name == null))
        {
            run.WriteError($"{data.Post.Name} is missing a directory: {data.Post.SourceFile}");
        }
        var additionalPosts = additionalPostSrcs
            .Where(data=>data.Name != null)
            .Select(data => new Post(data.Post.Id, false, data.Post.Front, data.Post.SourceFile, data.Post.SourceFile.DirectoryName!, data.Post.MarkdownHtml, data.Post.MarkdownPlainText))
            ;

        // todo(Gustav): if dir is missing a entry, optionally add a empty _index page

        var posts = postFiles.Concat(additionalPosts).OrderByDescending(p => p.Front.Date).ToImmutableArray();

        return new Dir(Guid.NewGuid(), root.Name, posts, dirs.ToImmutableArray());

        static IEnumerable<Dir> LoadDirsWithoutNulls(Run run, IEnumerable<DirectoryInfo> dirs)
        {
            foreach (var d in dirs)
            {
                if (d == null) { continue; }

                var dir = LoadDir(run, d);
                if (dir == null) { continue; }

                yield return dir;
            }
        }

        static IEnumerable<Post> LoadPosts(Run run, IEnumerable<FileInfo> files)
        {
            foreach(var f in files)
            {
                if (f == null) { continue; }

                var post = ParsePost(run, f);
                if(post == null) { continue; }

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
    private static void AddCommonData(Dictionary<string, object> data, string title, string summary, string url)
    {
        data.Add("title", title);
        data.Add("summary", summary);
        data.Add("url", url);
    }

    
    private static ImmutableArray<KeyValuePair<string, object>> SummaryForPost(Post post, Site site)
    {
        return ImmutableArray.Create<KeyValuePair<string, object>>
        (
            new KeyValuePair<string, object>("title", post.Front.Title),
            new KeyValuePair<string, object>("time_short", site.Data.ShortDateToString(post.Front.Date)),
            new KeyValuePair<string, object>("time_long", site.Data.LongDateToString(post.Front.Date)),
            // new("link", $"{sourceDir}/{post.FilenameWithoutExtension}.{extension}"),
            new KeyValuePair<string, object>("summary", post.Front.Summary),
            new KeyValuePair<string, object>("full_html", post.MarkdownHtml),
            new KeyValuePair<string, object>("full_text", post.MarkdownPlainText)
        );
}

    internal static int WriteSite(Run run, Site site, DirectoryInfo publicDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials)
    {
        var owners = ImmutableArray.Create<Dir>();
        return WriteDir(run, site, site.Root, publicDir, templates, partials, owners, true);
    }

    private static int WriteDir(Run run, Site site, Dir dir, DirectoryInfo targetDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials, ImmutableArray<Dir> owners, bool isRoot)
    {
        int count = 0;
        var ownersWithSelf = isRoot ? owners : owners.Add(dir); // if this is root, don't add the "content" folder
        var templateFolders = GenerateTemplateFolders(templates, ownersWithSelf);

        foreach (var subdir in dir.Dirs)
        {
            count += WriteDir(run, site, subdir, targetDir.GetDir(subdir.Name), templates, partials, ownersWithSelf, false);
        }

        var summaries = dir.Posts.Select(post => SummaryForPost(post, site)).ToImmutableArray();
        foreach (var post in dir.Posts)
        {
            // todo(Gustav): paginate index using Chunk(size)
            count += WritePost(run, site, templateFolders, post, summaries, targetDir.GetDir(post.Name), templates, partials);
        }

        return count;
    }

    private static int WritePost(Run run, Site site, ImmutableArray<DirectoryInfo> templateFolders, Post post, ImmutableArray<ImmutableArray<KeyValuePair<string, object>>> summaries, DirectoryInfo destDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials)
    {
        Dictionary<string, object> data = new();
        
        data.Add("content_html", post.MarkdownHtml);
        data.Add("content_text", post.MarkdownPlainText);
        // todo(Gustav): generate full url
        AddCommonData(data, post.Front.Title, post.Front.Summary, string.Empty); // $"{site.Data.Url}/{sourceDir}/{filename}"
        data.Add("time_short", site.Data.ShortDateToString(post.Front.Date));
        data.Add("time_long", site.Data.LongDateToString(post.Front.Date));
        data.AddRange(partials);
        // todo(Gustav): add more data

        return GenerateAll(site, run, destDir, templates, templateFolders, post, data);
    }

    record FileWithOptionalContent(FileInfo File, string? Content);

    private static string DisplayNameForFile(FileInfo file) => Path.GetRelativePath(Environment.CurrentDirectory, file.FullName);

    private static int GenerateAll(Site site, Run run, DirectoryInfo destDir, Templates templates, ImmutableArray<DirectoryInfo> templateFolders, Post post, Dictionary<string, object> data)
    {
        int pagesGenerated = 0;
        var templateName = post.IsIndex ? Constants.DIR_TEMPLATE : Constants.POST_TEMPLATE;
        
        foreach (var ext in templates.Extensions)
        {
            var templateFiles = templateFolders
                .Select(dir => dir.GetFile(templateName + ext))
                .Select(file => new FileWithOptionalContent(file, file.LoadFileSilentOrNull()))
                .ToImmutableArray()
                ;

            var path = destDir.GetFile("index" + ext);
            var selected = templateFiles.Where(file => file.Content != null).FirstOrDefault();
            if(selected == null)
            {
                var tried = string.Join(' ', templateFiles.Select(x => DisplayNameForFile(x.File)));
                run.WriteError($"Unable to generate {DisplayNameForFile(post.SourceFile)} to {DisplayNameForFile(path)} for {ext}, tried to use: {tried}");
                continue;
            }

            destDir.Create();
            
            var renderedPage = templates.RenderMustache(run, selected.Content!, selected.File, data, site);
            File.WriteAllText(path.FullName, renderedPage);
            AnsiConsole.MarkupLineInterpolated($"Generated {DisplayNameForFile(path)} from {DisplayNameForFile(post.SourceFile)} and {DisplayNameForFile(selected.File)}");
            pagesGenerated += 1;
        }
        return pagesGenerated;
    }

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

    internal static string? LoadFileOrNull(this FileInfo path, Run run)
    {
        try { return File.ReadAllText(path.FullName); }
        catch(Exception x)
        {
            run.WriteError($"Failed to load {path.FullName}: {x.Message}");
            return null;
        }
    }

    internal static string? LoadFileSilentOrNull(this FileInfo path)
    {
        try { return File.ReadAllText(path.FullName); }
        catch
        {
            return null;
        }
    }
}

public static class DictionaryExtensions
{
    public static void AddRange<K, V>(this Dictionary<K, V> data, IEnumerable<KeyValuePair<K, V>> list)
        where K: class
        where V: class
    { foreach (var (k, v) in list) { data.Add(k, v); } }
}

public static class IterTools
{
    // returns: initial+p0, initial+p0+p1, initial+p0+p1+p2 ...
    public static IEnumerable<R> Accumulate<T, R>(this IEnumerable<T> src, R initial, Func<T, R, R> add)
    {
        var current = initial;
        foreach(var t in src)
        {
            current = add(t, current);
            yield return current;
        }
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

    public static T? Parse<T>(Run run, string file, string content)
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

    internal static T? Load<T>(Run run, string path)
        where T : class
    {
        var content = File.ReadAllText(path);
        return Parse<T>(run, path, content);
    }

    internal static string Write<T>(T self)
    {
        return JsonSerializer.Serialize<T>(self, Options);
    }
}
