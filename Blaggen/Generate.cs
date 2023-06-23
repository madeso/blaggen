using System.Collections.Immutable;
using System.Security.Cryptography.X509Certificates;
using System.Web;

namespace Blaggen;

public static class Generate
{
    // data to mustache
    public record SummaryForPost(
        string title,
        string time_short,
        string time_long,
        string name,
        string summary,
        string full_html,
        string full_text
        );
    public record RootLink(string Name, string Url, bool IsSelected);
    public record PageData(Site Site, ImmutableArray<RootLink> Roots, ImmutableArray<SummaryForPost> Pages,
            string title,
            string summary,
            string url,
            string content_html,
            string content_text,
            string time_short,
            string time_long
        );

    public static PageData MakePageData(Site Site, Post Post, ImmutableArray<RootLink> Roots,
        ImmutableArray<SummaryForPost> Pages)
    {
        return new PageData(Site, Roots, Pages,
                Post.Front.Title,
                Post.Front.Summary,
                GenerateAbsoluteUrl(Site, Post),
                Post.MarkdownHtml,
                Post.MarkdownPlainText,
                Site.Data.ShortDateToString(Post.Front.Date),
                Site.Data.LongDateToString(Post.Front.Date)
            );
    }


    public record PageToWrite
    (
        ImmutableArray<DirectoryInfo> TemplateFolders,
        Post Post,
        ImmutableArray<SummaryForPost> Summaries,
        DirectoryInfo DestDir
    );

    public static SummaryForPost MakeSummaryForPost(Post Post, Site Site)
    {
        return new SummaryForPost(
                title: Post.Front.Title,
                time_short: Site.Data.ShortDateToString(Post.Front.Date),
                time_long: Site.Data.LongDateToString(Post.Front.Date),
                name: Post.Name,
                summary: Post.Front.Summary,
                full_html: Post.MarkdownHtml,
                full_text: Post.MarkdownPlainText
            );
    }

    private static Template.Definition<SummaryForPost> MakeSummaryForPostDef() => new Template.Definition<SummaryForPost>()
        .AddVar("title", s => s.title)
        .AddVar("time_short", s => s.time_short)
        .AddVar("time_long", s => s.time_long)
        .AddVar("name", s => s.name)
        .AddVar("summary", s => s.summary)
        .AddVar("full_html", s => s.full_html)
        .AddVar("full_text", s => s.full_text)
    ;

    
    private static Template.Definition<RootLink> MakeRootLinkDef() => new Template.Definition<RootLink>()
        .AddVar("Name", link => link.Name)
        .AddVar("Url", link => link.Url)
        .AddBool("IsSelected", link => link.IsSelected)
    ;

    // todo(Gustav): add more data
    // todo(Gustav): generate full url
    public static Template.Definition<PageData> MakePageDataDef() => new Template.Definition<PageData>()
        .AddVar("title", page => page.title)
        .AddVar("summary", page => page.summary)
        .AddVar("url", page => page.url)
        .AddVar("content_html", page => page.content_html)
        .AddVar("content_text", page => page.content_text)
        .AddVar("time_short", page => page.time_short)
        .AddVar("time_long", page => page.time_long)
        .AddList("pages", page=>page.Pages, MakeSummaryForPostDef())
        .AddList("roots", page=>page.Roots, MakeRootLinkDef())
    ;


    public static IEnumerable<PageToWrite> ListPagesForSite(Site site, DirectoryInfo publicDir, DirectoryInfo templates)
    {
        var owners = ImmutableArray.Create<Dir>();
        return ListPagesInDir(site, site.Root, publicDir, templates, owners, true);

        static IEnumerable<PageToWrite> ListPagesInDir(Site site, Dir dir,
            DirectoryInfo targetDir, DirectoryInfo templates,
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
                .Select(post => MakeSummaryForPost(post, site))
                .ToImmutableArray();
            foreach (var post in dir.Posts)
            {
                // todo(Gustav): paginate index using Chunk(size)
                var destDir = post.IsIndex ? targetDir : targetDir.GetDir(post.Name);
                yield return new PageToWrite(templateFolders, post, summaries, destDir);
            }
        }
    }


    public static async Task<int> WritePages(ImmutableArray<PageToWrite> pageToWrites, ImmutableArray<GroupPage> tags, Run run, VfsWrite vfsWrite,
        Site site, DirectoryInfo publicDir, TemplateDictionary templates)
    {
        var roots = pageToWrites
            .Where(x => GetRelativePath(publicDir, x).Count() == 1)
            .ToImmutableArray()
            ;

        // create all directories first, to avoid race conditions
        var dirsToWrite = pageToWrites.Select(page => page.DestDir).Concat(tags.Select(group => group.DestDir)).Distinct().ToImmutableArray();
        foreach (var dir in dirsToWrite)
        {
            dir.Create();
        }

        // todo(Gustav): write tags!
        var writePostTasks = pageToWrites.Select(page =>
            WritePost(run, vfsWrite, site, roots, templates, page, publicDir));
        var writeTagTasks = tags.Select(tag => WriteTags(run, vfsWrite, site, roots, templates, tag, publicDir));
        
        var counts = await Task.WhenAll(writePostTasks.Concat(writeTagTasks));
        return counts.Sum();
    }


    private static IEnumerable<string> GetRelativePath(DirectoryInfo publicDir, PageToWrite x)
    {
        var rel = Path.GetRelativePath(publicDir.FullName, x.DestDir.FullName);
        var split = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fin = split.Where(dir => dir != ".");
        return fin;
    }

    private static string GenerateAbsoluteUrl(Site site, Post post)
    {
        var rel = post.IsIndex ? post.RelativePath.PopBack() : post.RelativePath;
        var relative = string.Join('/', rel.Add("index"));
        return $"{site.Data.Url}/{relative}";
    }

    private static async Task<int> WriteTags(Run run, VfsWrite vfs, Site site, ImmutableArray<PageToWrite> roots,
        TemplateDictionary templates,
        GroupPage tag, DirectoryInfo publicDir)
    {
        var rootLinks = roots
                .Select(x => new RootLink(x.Post.Front.Title,
                    ConcatWithIndex(GetRelativePath(tag.DestDir, x)),
                    false
                ))
                .ToImmutableArray()
            ;
        var pagesGenerated = 0;
        var data = new PageData(site, rootLinks, tag.Children, tag.Display, "[summary]", "[url]", tag.Html, tag.Text, "[no date]", "[no date]");
        var templateName = Constants.DIR_TEMPLATE;

        foreach (var ext in templates.Extensions)
        {
            var templateFiles = tag.TemplateFolders
                    .Select(dir => dir.GetFile(templateName + ext))
                    .Select(file => new { File = file, Content = templates.GetTemplateOrNull(file) })
                    .ToImmutableArray()
                ;

            var path = new FileInfo(tag.Destination.FullName + ext);
            var selected = templateFiles.FirstOrDefault(file => file.Content != null);
            if (selected == null)
            {
                var tried = string.Join(' ', templateFiles.Select(x => DisplayNameForFile(x.File)));
                run.WriteError($"Unable to generate tag {tag.Display} to {DisplayNameForFile(path)} for {ext}, tried to use: {tried}");
                continue;
            }

            var renderedPage = selected.Content!(data);
            await vfs.WriteAllTextAsync(path, renderedPage);
            run.Status($"Generated {DisplayNameForFile(path)} from tag {tag.Display} and {DisplayNameForFile(selected.File)}");
            pagesGenerated += 1;
        }

        return pagesGenerated;

        static string ConcatWithIndex(IEnumerable<string> rel)
        {
            return string.Join("/", rel.Concat(new[] { "index.html" }));
        }
    }

    private static async Task<int> WritePost(Run run, VfsWrite vfs, Site site, ImmutableArray<PageToWrite> roots,
        TemplateDictionary templates,
        PageToWrite page, DirectoryInfo publicDir)
    {
        var rootLinks = roots
                .Select(x => new RootLink(x.Post.Front.Title,
                    ConcatWithIndex(GetRelativePath(page.DestDir, x)),
                    string.Join("", GetRelativePath(publicDir, x)) == string.Join("", GetRelativePath(publicDir, page).Take(1))
                ))
                .ToImmutableArray()
            ;
        var pagesGenerated = 0;
        var data = MakePageData(site, page.Post, rootLinks, page.Summaries);
        var templateName = page.Post.IsIndex ? Constants.DIR_TEMPLATE : Constants.POST_TEMPLATE;

        foreach (var ext in templates.Extensions)
        {
            var templateFiles = page.TemplateFolders
                .Select(dir => dir.GetFile(templateName + ext))
                .Select(file => new {File=file, Content = templates.GetTemplateOrNull(file)})
                .ToImmutableArray()
                ;

            var path = page.DestDir.GetFile("index" + ext);
            var selected = templateFiles.FirstOrDefault(file => file.Content != null);
            if (selected == null)
            {
                var tried = string.Join(' ', templateFiles.Select(x => DisplayNameForFile(x.File)));
                run.WriteError($"Unable to generate {DisplayNameForFile(page.Post.SourceFile)} to {DisplayNameForFile(path)} for {ext}, tried to use: {tried}");
                continue;
            }

            var renderedPage = selected.Content!(data);
            await vfs.WriteAllTextAsync(path, renderedPage);
            run.Status($"Generated {DisplayNameForFile(path)} from {DisplayNameForFile(page.Post.SourceFile)} and {DisplayNameForFile(selected.File)}");
            pagesGenerated += 1;
        }

        return pagesGenerated;

        static string ConcatWithIndex(IEnumerable<string> rel)
        {
            return string.Join("/", rel.Concat(new[] { "index.html" }));
        }
    }


    private static string DisplayNameForFile(FileInfo file) => Path.GetRelativePath(Environment.CurrentDirectory, file.FullName);


    private static ImmutableArray<DirectoryInfo> GenerateTemplateFolders(DirectoryInfo templateFolder, IEnumerable<Dir> owners)
    {
        var root = new[] { templateFolder };
        var children = owners
            // template paths going from current dir to root
            .Accumulate(ImmutableArray.Create<string>(), (dir, arr) => arr.Add(dir.Name))
            .Reverse()
            // convert to actual directory
            .Select(arr => templateFolder.GetSubDirs(arr))
            ;
        var ret = children.Concat(root).ToImmutableArray();
        return ret;
    }

    // todo(Gustav): rename tag concept to group
    public record GroupPage(ImmutableArray<DirectoryInfo> TemplateFolders, DirectoryInfo DestDir, string Display,
        FileInfo Destination, string Html, string Text, ImmutableArray<SummaryForPost> Children);
    public static ImmutableArray<GroupPage> CollectTagPages(Site site, DirectoryInfo publicDir, DirectoryInfo templates, ImmutableArray<PageToWrite> pages)
    {
        var tags = pages
            // flatten all pages to just types->tags
            .SelectMany(page => page.Post.Front.TagData)
            // group on type
            .GroupBy(x => x.Key, (key, items) => new
            {
                Display = key,
                Relative = HttpUtility.UrlEncode(key),
                // flatten all tags to a single set
                Items = items.SelectMany(x => x.Value)
                    .Distinct()
                    .Select(x => new
                    {
                        Display = x,
                        Relative = HttpUtility.UrlEncode(x),
                        Posts = pages
                            .Select(page => new { page.Post, Tags = page.Post.Front.TagData.TryGetValue(x, out var val) ? val : null })
                            .Where(post => post.Tags != null)
                            .Select(post => new { post.Post, Tags = post.Tags! })
                            .ToImmutableArray()
                    })
                    .ToImmutableArray()
            })
            .ToImmutableArray()
            ;

        var templateFolders = ImmutableArray.Create(templates);

        // generate a list of pages: tags/authors/etc
        var groupPages = tags.Select(tag =>
                new GroupPage(templateFolders, publicDir, tag.Display, publicDir.GetFile(tag.Relative),
                    $"<h1>{tag.Display}</h1", tag.Display,
                    tag.Items.Select(x => new SummaryForPost(x.Display, "", "", x.Relative, "", "", "")).ToImmutableArray()
                )).ToArray();

        // generate each page: bruce willis/whatever/etc
        var eachTagPage = tags.SelectMany(group => group.Items.Select(tag =>
            new GroupPage(ImmutableArray.Create(templates.GetSubDirs(tag.Relative), templates),
                publicDir.GetDir(group.Relative), tag.Display, publicDir.GetDir(group.Relative).GetFile(tag.Relative),
                $"<h1>{tag.Display}</h1>", tag.Display,
                tag.Posts.Where(p=>p.Tags.Contains(tag.Display)).Select(p=>MakeSummaryForPost(p.Post, site)).ToImmutableArray()
                )
        )).ToArray();
        
        return groupPages.Concat(eachTagPage).ToImmutableArray();
    }
}
