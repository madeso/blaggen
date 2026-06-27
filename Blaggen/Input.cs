using System.Collections.Immutable;
using System.Text;

namespace Blaggen;


internal class TemplateDictionary
{
    private TemplateDictionary()
    {
    }


    internal static async Task<TemplateDictionary?> Load(Run run, VfsRead vfs, DirectoryInfo root, DirectoryInfo templateFolder, DirectoryInfo partialFolder)
    {
        // todo(Gustav): warn if template files are missing
        var template_files = vfs.GetFilesRec(templateFolder)
            .Where(f => f.Name.Contains(""))
            .ToImmutableArray();

        var unloaded = template_files.Select(
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

        var extensions = template_files.Select(file => file.Extension.ToLowerInvariant()).ToImmutableHashSet();
        return new TemplateDictionary();
    }
}



internal static class Input
{
    internal const string SOURCE_START = "```json";
    internal const string SOURCE_END = "```";
    internal const string FRONTMATTER_SEP = "***"; // markdown hline


    internal static DirectoryInfo? FindRoot(VfsRead vfs, DirectoryInfo? start)
    {
        var current = start;

        while (current != null && vfs.Exists(current.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION)) == false)
        {
            current = current.Parent;
        }

        return current;
    }

    internal static (FrontMatter? frontmatter, string markdownContent) ParsePostToTuple(Run run, IEnumerable<string> lines, FileInfo file)
    {
        var (frontmatter_source, markdown_source) = ParseGenericPostData(lines, file, FRONTMATTER_SEP,
            lt => lt is SOURCE_START or SOURCE_END, skips: 0);

        var frontmatter = JsonUtil.Parse<FrontMatter>(run, file, frontmatter_source);
        return (frontmatter, markdown_source);
    }

    internal static string PostToFileData(Post post)
    {
        var json = JsonUtil.Write(post.Front);
        return string.Join('\n', SOURCE_START, json, SOURCE_END, FRONTMATTER_SEP, post.Markdown);
    }

    internal static string GenerateSummary(string text)
    {
        // hacky way to generate a summary
        const string ELLIPSIS = "...";
        const int WORDS_IN_AUTO_SUMMARY = 25;

        var lines_without_ending_dot = text
            .Split('\n', StringSplitOptions.TrimEntries)
            .Select(x => x.TrimEnd('.').Trim()); // split into lines and remove ending dot
        // todo(Gustav): normalize whitespace
        var sentences = string.Join(". ", lines_without_ending_dot); // join into a long string again with a dot at the end
        var summary = string.Join(' ', sentences.Split(' ').Take(WORDS_IN_AUTO_SUMMARY)) + ELLIPSIS;
        return summary.Length < text.Length
                ? summary
                : text
            ;
    }


    internal static (string frontmatter, string markdown) ParseGenericPostData(IEnumerable<string> lines, FileInfo file, string content_separator, Func<string, bool> front_matter_ignores, int skips)
    {
        var frontmatter = new StringBuilder();
        var markdown = new StringBuilder();
        var is_parsing_frontmatter = true;

        foreach (var line in lines.Skip(skips))
        {
            if (is_parsing_frontmatter)
            {
                var lt = line.Trim();
                if (lt.Contains(content_separator))
                {
                    is_parsing_frontmatter = false;
                    continue;
                }

                if (front_matter_ignores(lt))
                {
                    continue;
                }

                frontmatter.AppendLine(line);
            }
            else
            {
                markdown.AppendLine(line);
            }
        }

        return (frontmatter.ToString(), markdown.ToString());
    }


    internal static async Task<SiteConfig?> LoadSiteConfig(Run run, VfsRead vfs, DirectoryInfo root)
    {
        var path = root.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION);
        return await JsonUtil.Load<SiteConfig>(run, vfs, path);
    }


    internal static async Task<Site?> LoadEntireSite(Run run, VfsRead vfs, DirectoryInfo root)
    {
        var config = await LoadSiteConfig(run, vfs, root);
        if (config == null) { return null; }

        var markdown = new MarkdownParser();

        var content = await LoadDir(run, vfs, Constants.GetContentDirectory(root), [], markdown);
        if (content == null) { return null; }

        return new Site(config, content);
    }


    private static async Task<Section?> LoadDir(Run run, VfsRead vfs, DirectoryInfo root, ImmutableArray<string> relative_paths, MarkdownParser markdown)
    {
        // todo(Gustav): implement
        return null;
    }
}
