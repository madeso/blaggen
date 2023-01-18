using Spectre.Console;
using Spectre.Console.Cli;
using Stubble.Core.Builders;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata.Ecma335;
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
        site.Sources.Add("post", "posts");
        site.Generators.Add(new ListGenerator { Source = "post", Summary="Posts", Single = "post.mustache.html", List= "list-post.mustache.html", Extension="html" });
        site.Indices.Add(new IndexGenerator { Sources = new string[] { "post" } ,Template = "index.mustache.html", Dest = "index.html" });
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
        [Description("Type of post to create")]
        [CommandArgument(0, "<type>")]
        public string Type { get; init; } = string.Empty;

        [Description("Post title")]
        [CommandArgument(1, "<title>")]
        public string Title { get; init; } = string.Empty;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var run = new Run();
        var root = SiteData.FindRoot();
        if(root == null) { run.WriteError("Unable to find root"); return -1; }

        var site = Logic.LoadSiteData(run, root);
        if(site == null) { return -1; }

        if(site.Sources.TryGetValue(args.Type, out var folder) == false)
        {
            run.WriteError($"Invalid type {args.Type}, must be one of: {site.Sources.Keys}");
            return -1;
        }

        var postFolder = Logic.SubDir(Logic.GetContentDirectory(root), folder);
        if(postFolder.Exists == false) { postFolder.Create(); }

        var frontmatter = JsonUtil.Write(new FrontMatter { Title = args.Title });
        var fileName = Logic.MakeSafe(args.Title) + ".md";
        var content = $"{Logic.SOURCE_START}\n{frontmatter}\n{Logic.SOURCE_END}\n{Logic.FRONTMATTER_SEP}\n# {args.Title}";
        var path = Path.Join(postFolder.FullName, fileName);
        File.WriteAllText(path, content);

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

        var site = Logic.LoadSite(run, root);
        if (site == null) { return -1; }

        site.Data.Validate(run);
        if(run.ErrorCount > 0) { return -1; }

        // todo(Gustav): generate
        var publicDir = Logic.SubDir(root, "public");
        var templateDir = Logic.SubDir(root, "templates");
        var stubble = new StubbleBuilder().Build();
        var partials = Logic.SubDir(root, "partials").EnumerateFiles()
            .Select(file => new {Name=Path.GetFileNameWithoutExtension(file.Name), Content=File.ReadAllText(file.FullName) })
            .Select(d => new KeyValuePair<string, object>(d.Name, new Func<object>(() => d.Content)))
            .ToImmutableArray()
            ;
        static void AddRange(Dictionary<string, object> data, IEnumerable<KeyValuePair<string, object>> list) {
            foreach(var (k,v) in list) { data.Add(k,v); }
        }
        static void AddCommonData(Dictionary<string, object> data, string title, string summary, string url) {
            data.Add("title", title);
            data.Add("summary", summary);
            data.Add("url", url);
        }

        var pagesGenerated = 0;
        var timeStart = DateTime.Now;

        string DateToString(DateTime dt)
        {
            return dt.ToLongDateString();
        }

        foreach (var gen in site.Data.Generators)
        {
            if(site.Posts.TryGetValue(gen.Source, out var postList) == false)
            {
                run.WriteError($"(bug) missing {gen.Source}");
                continue;
            }

            if(site.Data.Sources.TryGetValue(gen.Source, out var sourceDir) == false)
            {
                run.WriteError($"(bug) missing {gen.Source} in source list");
                continue;
            }

            var postDir = Logic.SubDir(publicDir, sourceDir); // ie. public/posts

            if(string.IsNullOrWhiteSpace(gen.Single) == false)
            {
                var templatePath = Logic.DirFile(templateDir, gen.Single);
                var templateSource = Logic.LoadFile(run, templatePath);
                if(templateSource == null)
                {
                    run.WriteError($"Missing template file {templatePath}");
                }
                else
                {
                    foreach(var post in postList)
                    {
                        // todo(Gustav): check file date and only generate if needed
                        var filename = $"{post.FilenameWithoutExtension}.{gen.Extension}";
                        var html = Markdig.Markdown.ToHtml(post.Markdown);
                        Dictionary<string, object> data = new();
                        data.Add("content", html);
                        AddCommonData(data, post.Front.Title, post.Front.Summary, $"{site.Data.Url}/{sourceDir}/{filename}");
                        data.Add("date", DateToString(post.Front.Date));
                        AddRange(data, partials);
                        // todo(Gustav): add more data

                        var renderedPage = stubble.Render(templateSource, data);
                        var path = Logic.DirFile(postDir, filename);
                        File.WriteAllText(path, renderedPage);
                        AnsiConsole.MarkupLineInterpolated($"Generated {path} for {gen}");
                        pagesGenerated += 1;
                    }
                }
            }

            if(string.IsNullOrEmpty(gen.List) == false)
            {
                var templatePath = Logic.DirFile(templateDir, gen.List);
                var templateSource = Logic.LoadFile(run, templatePath);
                if (templateSource == null)
                {
                    run.WriteError($"Missing template file {templatePath}");
                }
                else
                {
                    // todo(Gustav): add pagination
                    Dictionary<string, object> data = new();
                    var filename = $"{sourceDir}.{gen.Extension}";
                    AddCommonData(data, $"{sourceDir} - {site.Data.Name}", gen.Summary, $"{site.Data.Url}/{filename}");
                    data.Add("pages", postList.Select(post => new {
                            title = post.Front.Title,
                            date=DateToString(post.Front.Date),
                            link=$"{sourceDir}/{post.FilenameWithoutExtension}.{gen.Extension}",
                            summary = post.Front.Summary
                        }).ToArray());
                    AddRange(data, partials);

                    var renderedPage = stubble.Render(templateSource, data);
                    var path = Logic.DirFile(publicDir, filename);
                    File.WriteAllText(path, renderedPage);
                    AnsiConsole.MarkupLineInterpolated($"Generated {path} for {gen}");
                    pagesGenerated += 1;
                }
            }
        }

        // todo(Gustav): index generators
        // todo(Gustav): tag generator

        var timeEnd = DateTime.Now;
        var timeTaken = timeEnd - timeStart;
        AnsiConsole.MarkupLineInterpolated($"Wrote [green]{pagesGenerated}[/] files in [blue]{timeTaken}[/]");
        return run.ErrorCount > 0 ? -1 : 0;
    }
}

// ----------------------------------------------------------------------------------------------------------------------------
// Data and basic functions

class IndexGenerator
{
    [JsonPropertyName("source")]
    public string[] Sources { get; set; } = Array.Empty<string>();

    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("dest")]
    public string Dest { get; set; } = string.Empty;
}

class ListGenerator
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = string.Empty;

    [JsonPropertyName("single")]
    public string Single { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("list")]
    public string List { get; set; } = string.Empty;

    public override string ToString() => $"{Source} {Extension}";
}

class SiteData
{
    [JsonPropertyName("name")]
    public string Name{ get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string BaseUrl { get; set; } = string.Empty;
    public string Url => BaseUrl.EndsWith('/') ? BaseUrl.TrimEnd('/') : BaseUrl;

    [JsonPropertyName("sources")]
    public Dictionary<string, string> Sources { get; set; } = new();

    [JsonPropertyName("indices")]
    public List<IndexGenerator> Indices{ get; set; } = new();

    [JsonPropertyName("generators")]
    public List<ListGenerator> Generators { get; set; } = new();

    public const string PATH = "site.blaggen.json";

    public void Validate(Run run)
    {
        foreach(var index in Indices)
        {
            var invalids = index.Sources.Where(src => Sources.ContainsKey(src) == false);
            foreach(var i in invalids)
            {
                run.WriteError($"Index generator {index.Template} references missing {i}");
            }
        }

        foreach (var gen in Generators)
        {
            if (Sources.ContainsKey(gen.Source) != true)
            {
                run.WriteError($"Page generator {gen.Single}/{gen.List} references missing {gen.Source}");
            }
        }
    }

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
}

record Site (SiteData Data, ImmutableDictionary<string, ImmutableArray<Post>> Posts);

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

record Post(FrontMatter Front, string FilenameWithoutExtension, string Markdown);

// ----------------------------------------------------------------------------------------------------------------------------
// App logic

internal static class Logic
{
    public const string SOURCE_START = "```json";
    public const string SOURCE_END = "```";
    public const string FRONTMATTER_SEP = "***"; // markdown hline

    public static Post? ParsePost(Run run, FileInfo file)
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

        if(string.IsNullOrEmpty(frontmatter.Summary))
        {
            // hacky way to generate a summary
            var linesWithoutEndingDot = content.Replace("#", "") // remove headers
                .Split('\n', StringSplitOptions.TrimEntries).Select(x => x.TrimEnd('.').Trim()); // split into lines and remove ending dot
            var markless = string.Join(". ", linesWithoutEndingDot); // join into a long string again with a dot at the end
            frontmatter.Summary = string.Join(' ', markless.Split(' ').Take(25)) + "...";
        }

        return new Post(frontmatter, Path.GetFileNameWithoutExtension(file.Name), content);
    }

    public static SiteData? LoadSiteData(Run run, DirectoryInfo root)
    {
        var path = Path.Join(root.FullName, SiteData.PATH);
        return JsonUtil.Load<SiteData>(run, path);
    }

    public static Site? LoadSite(Run run, DirectoryInfo root)
    {
        var data = LoadSiteData(run, root);
        if(data == null) { return null; }

        var content = GetContentDirectory(root);

        var posts = data.Sources
            .Select(kvp => new KeyValuePair<string, ImmutableArray<Post>>
                (kvp.Key, LoadPosts(run, SubDir(content, kvp.Value)).OrderByDescending(p => p.Front.Date).ToImmutableArray())
            )
            .ToImmutableDictionary()
            ;

        return new Site(data, posts);

        static IEnumerable<Post> LoadPosts(Run run, DirectoryInfo dir)
        {
            var files = dir.GetFiles("*.md", SearchOption.AllDirectories);
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
        return SubDir(root, "content");
    }

    internal static DirectoryInfo SubDir(DirectoryInfo dir, string sub)
    {
        return dir.CreateSubdirectory(sub);
    }

    internal static object MakeSafe(string str)
    {
        // algorithm inspired by the description of the doxygen version
        // https://stackoverflow.com/a/30490482
        var buf = "";

        foreach (var c in str)
        {
            buf += c switch
            {
                // '0' .. '9'
                '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9' or
                // 'a'..'z'
                'a' or 'b' or 'c' or 'd' or 'e' or 'f' or 'g' or 'h' or 'i' or 'j' or
                'k' or 'l' or 'm' or 'n' or 'o' or 'p' or 'q' or 'r' or 's' or 't' or
                'u' or 'v' or 'w' or 'x' or 'y' or 'z' or
                // other safe characters...
                // is _ considered safe? we only care about one way translation
                // so it should be safe.... right?
                '-' or '_'
                => $"{c}",
                ' ' => '_',
                // 'A'..'Z'
                // 'A'..'Z'
                'A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G' or 'H' or 'I' or 'J' or
                'K' or 'L' or 'M' or 'N' or 'O' or 'P' or 'Q' or 'R' or 'S' or 'T' or
                'U' or 'V' or 'W' or 'X' or 'Y' or 'Z'
                => $"_{Char.ToLowerInvariant(c)}",
                _ => $"_{(int)c}"
            };
        }

        return buf;
    }

    internal static string? LoadFile(Run run, string path)
    {
        if (File.Exists(path) == false) { return null; } // ugly!
        return File.ReadAllText(path);
    }

    internal static string DirFile(DirectoryInfo dir, string file)
    {
        return Path.Join(dir.FullName, file);
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
