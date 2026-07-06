using System.Collections.Immutable;
using System.Text;

namespace Blaggen;

internal record TemplateFolder(Func<Generate.TemplatePostData, string>? Post, Func<Generate.TemplateSectionData, string>? Section);

internal class TemplateDictionary
{
    private readonly Dictionary<string, TemplateFolder> templates;

    private TemplateDictionary(Dictionary<string, TemplateFolder> templates)
    {
        this.templates = templates;
    }

    public T? GetProp<T>(ImmutableArray<string> dirs, Func<TemplateFolder, T> selector) where T : class?
    {
        return templates.TryGetValue(KeyFrom(dirs), out var group) ? selector(group) : null;
    }

    internal static async Task<TemplateDictionary?> Load(Run run, VfsRead vfs, DirectoryInfo root, DirectoryInfo templateFolder, DirectoryInfo partialFolder)
    {
        var ret = new Dictionary<string, TemplateFolder>();
        await LoadTemplateRecursive(templateFolder, []);
        return new TemplateDictionary(ret);

        async Task LoadTemplateRecursive(DirectoryInfo dir, ImmutableArray<string> pattern)
        {
            var post = await LoadSingleTemplate(Constants.TEMPLATE_POST, dir, Generate.MakePostData());
            var section = await LoadSingleTemplate(Constants.TEMPLATE_SECTION, dir, Generate.MakeSectionData());

            if (post != null || section != null)
            {
                ret.Add(KeyFrom(pattern), new TemplateFolder(post, section));
            }

            foreach (var d in vfs.GetDirectories(dir))
            {
                await LoadTemplateRecursive(d, pattern.Add(d.Name));
            }
        }

        async Task<Func<T, string>?> LoadSingleTemplate<T>(string name, DirectoryInfo dir, Template.Definition<T> def) where T : class
        {
            var post_file = dir.GetFile(name + ".html");
            if (vfs.Exists(post_file) == false) return null;

            var (func, errors) = await Template.Parse(post_file, vfs, Template.DefaultFunctions(), partialFolder, def);

            foreach (var error in errors)
            {
                run.WriteError($"{error.Location.File}({error.Location.Line}:{error.Location.Offset}): {error.Message}");
            }

            return func;
        }
    }

    private static string KeyFrom(ImmutableArray<string> pattern)
    {
        return string.Join('/', pattern);
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
        var section = content.Section;
        if (section == null)
        {
            run.WriteError($"Root only contains a post");
            return null;
        }

        return new Site(config, section);
    }

    private record ParsedPost(Post Post, bool PromoteThisPost);

    private record SectionOrPost(Section? Section, Post? Post);

    private static async Task<SectionOrPost> LoadDir(Run run, VfsRead vfs, DirectoryInfo root, ImmutableArray<string> relative_paths, MarkdownParser markdown_parser)
    {
        var section_name = root.Name;

        var files_async = vfs.GetFiles(root).Select(async f =>
        {
            var lines = (await vfs.ReadAllTextAsync(f)).Split('\n');
            var (fm, markdown_source) = ParsePostToTuple(run, lines, f);
            if (fm == null) return null;

            var file_name_without_extension = Path.GetFileNameWithoutExtension(f.Name);
            var is_index = file_name_without_extension == Constants.SECTION_INDEX_NAME_NO_EXT;
            var is_promoted = file_name_without_extension == Constants.TURN_DIR_INTO_POST_NAME_NO_EXT;

            var post = new Post(is_index ? "index" : file_name_without_extension, is_index ? PostType.Section : PostType.Post, fm, f, markdown_source);
            return new ParsedPost(post, is_promoted);
        }).ToImmutableArray();
        var dirs_async = vfs.GetDirectories(root).Select(async d =>
            await LoadDir(run, vfs, d, [..relative_paths.Append(d.Name)], markdown_parser)
        ).ToImmutableArray();

        await Task.WhenAll(files_async.Select(x => (Task)x).Concat(dirs_async.Select(x => (Task)x)));

        var promoted = new List<Post?>();
        var dirs = dirs_async.Select(x => x.Result)
            .Where(x => x.Section != null, x => promoted.Add(x.Post))
            .Select(x => x.Section).NonNull()
            .ToImmutableArray();
        var files_all = files_async.Select(x => x.Result).NonNull()
            .Concat(promoted.NonNull().Select(x => new ParsedPost(x, false)))
            .ToImmutableArray();

        var has_promoted = files_all.Any(x => x.PromoteThisPost);
        if (files_all.Length == 1 && has_promoted)
        {
            // only has one post and it is promoted
            return new SectionOrPost(null, files_all[0].Post with {Name = section_name});
        }

        if (has_promoted)
        {
            var msg = PostsToMessage(files_all.Where(x => x.PromoteThisPost).Select(x => x.Post));
            run.WriteInfo($"Detected promoted post [blue]{msg}[/]but dir has many posts, promotion ignored");
        }

        var index_posts = new List<Post>();
        var files = files_all.Select(x => x.Post).Where(x => x.Type == PostType.Post, index_posts.Add)
            .ToImmutableArray();

        if (index_posts.Count > 1)
        {
            var msg = PostsToMessage(index_posts);
            run.WriteInfo($"Detected too many index posts: [blue]{msg}[/], ignored all but first");
        }

        return new SectionOrPost(new Section(section_name, index_posts.FirstOrDefault(), files, dirs, root), null);
    }

    private static string PostsToMessage(IEnumerable<Post> posts)
    {
        var promoted_files = posts.Select(x => x.SourceFile.ToString());
        var promoted_message = string.Join(", ", promoted_files);
        return promoted_message;
    }

    public static Generate.TemplateSectionData TemplateDataFromSection(Site site, Section section, Post section_post)
    {
        throw new NotImplementedException();
    }

    public static Generate.TemplatePostData TemplateDataFromPost(Site site, Post post)
    {
        throw new NotImplementedException();
    }
}
