using Spectre.Console;
using System.Collections.Immutable;
using System.Text;

namespace Blaggen;

public static class Generate
{
    public class SummaryForPost
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

    public record RootLink(string Name, string Url, bool IsSelected);

    public class PageData
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

    public static async Task<int> WriteSite(Run run, VfsWrite vfs, Site site, DirectoryInfo publicDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials)
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
            for (int i = 0; i < steps; i += 1)
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
