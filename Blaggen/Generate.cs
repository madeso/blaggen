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

    public static IEnumerable<PageToWrite> ListPagesForSite(Site site, DirectoryInfo publicDir, Templates templates)
    {
        var owners = ImmutableArray.Create<Dir>();
        return ListPagesInDir(site, site.Root, publicDir, templates, owners, true);
    }

    private static IEnumerable<PageToWrite> ListPagesInDir(Site site, Dir dir,
        DirectoryInfo targetDir, Templates templates,
        ImmutableArray<Dir> owners, bool isRoot)
    {
        var ownersWithSelf = isRoot ? owners : owners.Add(dir); // if this is root, don't add the "content" folder
        var templateFolders = GenerateTemplateFolders(templates, ownersWithSelf);

        var pages = dir.Dirs.SelectMany(subdir =>
            ListPagesInDir(site, subdir, targetDir.GetDir(subdir.Name), templates, ownersWithSelf, false));
        foreach (var p in pages)
        {
            yield return p;
        }

        var summaries = dir.Posts.Where(post => post.IsIndex == false)
            .Select(post => new SummaryForPost(post, site))
            .ToImmutableArray();
        foreach (var post in dir.Posts)
        {
            // todo(Gustav): paginate index using Chunk(size)
            var destDir = post.IsIndex ? targetDir : targetDir.GetDir(post.Name);
            yield return new PageToWrite(templateFolders, post, summaries, destDir);
        }
    }

    public static async Task<int> WritePages(ImmutableArray<PageToWrite> pageToWrites, Run run, VfsWrite vfsWrite,
        Site site,
        DirectoryInfo publicDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials)
    {
        var count = 0;

        var roots = pageToWrites
            .Where(x => GetRelativePath(publicDir, x).Count() == 1)
            .ToImmutableArray()
            ;

        foreach (var page in pageToWrites)
        {
            var rootLinks = roots
                .Select(x => new RootLink(x.Post.Front.Title, GetIndexPath(GetRelativePath(page.DestDir, x)), GetIndexPath(GetRelativePath(publicDir, x)) == GetIndexPath(GetRelativePath(publicDir, page).Take(1))))
                .ToImmutableArray()
                ;
            count += await WritePost(run, vfsWrite, site, rootLinks, page.TemplateFolders, page.Post,
                page.Summaries, page.DestDir, templates, partials);
        }

        return count;

        static IEnumerable<string> GetRelativePath(DirectoryInfo publicDir, PageToWrite x)
        {
            var rel = Path.GetRelativePath(publicDir.FullName, x.DestDir.FullName);
            var split = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fin = split.Where(dir => dir != ".");
            return fin;
        }

        static string GetIndexPath(IEnumerable<string> rel)
        {
            return string.Join("/", rel.Concat(new[] { "index.html" }));
        }
    }

    public record PageToWrite
    (
        ImmutableArray<DirectoryInfo> TemplateFolders,
        Post Post,
        ImmutableArray<SummaryForPost> Summaries,
        DirectoryInfo DestDir
    );

    private static async Task<int> WritePost(Run run, VfsWrite vfs, Site site, ImmutableArray<RootLink> rootLinks,
        ImmutableArray<DirectoryInfo> templateFolders, Post post, ImmutableArray<SummaryForPost> summaries,
        DirectoryInfo destDir, Templates templates, ImmutableArray<KeyValuePair<string, object>> partials)
    {
        var pagesGenerated = 0;
        var data = new PageData(site, post, rootLinks, partials.ToDictionary(k => k.Key, k => k.Value), summaries.ToList());
        var templateName = post.IsIndex ? Constants.DIR_TEMPLATE : Constants.POST_TEMPLATE;

        foreach (var ext in templates.Extensions)
        {
            var templateFiles = templateFolders
                .Select(dir => dir.GetFile(templateName + ext))
                .Select(file => new FileWithOptionalContent(file, templates.GetTemplateOrNull(file)))
                .ToImmutableArray()
                ;

            var path = destDir.GetFile("index" + ext);
            var selected = templateFiles.FirstOrDefault(file => file.Content != null);
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
        var root = new[] { templates.TemplateFolder };
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
