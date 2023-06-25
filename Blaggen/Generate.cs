using System.Collections.Immutable;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using System.Xml.Linq;
using Microsoft.VisualBasic;
using static Blaggen.Generate;

namespace Blaggen;

public static class Generate
{
    // data to mustache
    public record SummaryForPost(
        string Title,
        string TimeShort,
        string TimeLong,
        string Name,
        string Summary,
        string FullHtml,
        string FullText,
        string RelativeUrl
        );
    public record RootLink(string Name, string Url, bool IsSelected);
    public record RootLinkData(string Name, FileInfo File, string RootGroup);
    public record PageData(Site Site, ImmutableArray<RootLink> Roots, ImmutableArray<SummaryForPost> Pages,
            string Title,
            string Summary,
            string Url,
            string ContentHtml,
            string ContentText,
            string TimeShort,
            string TimeLong
        );

    public static PageData MakePageData(Site site, Post post, ImmutableArray<RootLink> roots,
        ImmutableArray<SummaryForPost> pages)
    {
        return new PageData(site, roots, pages,
                post.Front.Title,
                post.Front.Summary,
                GenerateAbsoluteUrl(site, post),
                post.MarkdownHtml,
                post.MarkdownPlainText,
                site.Data.ShortDateToString(post.Front.Date),
                site.Data.LongDateToString(post.Front.Date)
            );
    }


    public record PageToWrite
    (
        ImmutableArray<DirectoryInfo> TemplateFolders,
        Post Post,
        ImmutableArray<SummaryForPost> Summaries,
        FileInfo DestFile,
        string RootGroup,
        int Depth
    );

    public static SummaryForPost MakeSummaryForPost(Post post, Site site)
    {
        return new SummaryForPost(
                Title: post.Front.Title,
                TimeShort: site.Data.ShortDateToString(post.Front.Date),
                TimeLong: site.Data.LongDateToString(post.Front.Date),
                Name: post.Name,
                Summary: post.Front.Summary,
                FullHtml: post.MarkdownHtml,
                FullText: post.MarkdownPlainText,
                RelativeUrl: $"{post.Name}/index.html"
            );
    }

    private static Template.Definition<SummaryForPost> MakeSummaryForPostDef() => new Template.Definition<SummaryForPost>()
        .AddVar("title", s => s.Title)
        .AddVar("time_short", s => s.TimeShort)
        .AddVar("time_long", s => s.TimeLong)
        .AddVar("name", s => s.Name)
        .AddVar("summary", s => s.Summary)
        .AddVar("full_html", s => s.FullHtml)
        .AddVar("full_text", s => s.FullText)
        .AddVar("url", s => s.RelativeUrl)
    ;

    
    private static Template.Definition<RootLink> MakeRootLinkDef() => new Template.Definition<RootLink>()
        .AddVar("Name", link => link.Name)
        .AddVar("Url", link => link.Url)
        .AddBool("IsSelected", link => link.IsSelected)
    ;

    // todo(Gustav): add more data
    // todo(Gustav): generate full url
    public static Template.Definition<PageData> MakePageDataDef() => new Template.Definition<PageData>()
        .AddVar("title", page => page.Title)
        .AddVar("summary", page => page.Summary)
        .AddVar("url", page => page.Url)
        .AddVar("content_html", page => page.ContentHtml)
        .AddVar("content_text", page => page.ContentText)
        .AddVar("time_short", page => page.TimeShort)
        .AddVar("time_long", page => page.TimeLong)
        .AddList("pages", page=>page.Pages, MakeSummaryForPostDef())
        .AddList("roots", page=>page.Roots, MakeRootLinkDef())
    ;


    public static IEnumerable<PageToWrite> ListPagesForSite(Site site, DirectoryInfo publicDir, DirectoryInfo templates)
    {
        var owners = ImmutableArray.Create<Dir>();
        return ListPagesInDir(site, site.Root, publicDir, templates, owners, 0, string.Empty);

        static IEnumerable<PageToWrite> ListPagesInDir(Site site, Dir dir,
            DirectoryInfo targetDir, DirectoryInfo templates,
            ImmutableArray<Dir> owners, int depth, string rootGroup)
        {
            var ownersWithSelf = depth==0 ? owners : owners.Add(dir); // if this is root, don't add the "content" folder
            var rg = depth==0 ? dir.Name : rootGroup;
            var templateFolders = GenerateTemplateFolders(templates, ownersWithSelf);

            var pages = dir.Dirs.SelectMany(subdir =>
                ListPagesInDir(site, subdir, targetDir.GetDir(subdir.Name), templates, ownersWithSelf, depth+1, rg));
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
                var d = post.IsIndex ? depth-1 : depth;
                var rrg = depth == 0 && post.IsIndex == false ? post.Name : rootGroup;
                yield return new PageToWrite(templateFolders, post, summaries, destDir.GetFile("index.html"), rrg, d);
            }
        }
    }


    public static ImmutableArray<RootLinkData> CollectRoots(ImmutableArray<PageToWrite> pageToWrites, IEnumerable<GroupPage> groups)
    {
        var pageTags = pageToWrites
            .Where(x => x.Depth == 0)
            .Select(x => new RootLinkData(x.Post.Front.Title, x.DestFile, x.RootGroup))
            ;

        var groupPages = groups
            .Where(x => x.IsRoot)
            .Select(x => new RootLinkData(x.Display, x.Destination, x.GroupName))
            ;

        // todo(Gustav): sort these?
        return pageTags.Concat(groupPages)
            .ToImmutableArray();
    }


    public static async Task<int> WriteAllPages(ImmutableArray<RootLinkData> roots, ImmutableArray<PageToWrite> pageToWrites, ImmutableArray<GroupPage> tags, Run run, VfsWrite vfsWrite,
        Site site, DirectoryInfo publicDir, TemplateDictionary templates)
    {
        // create all directories first, to avoid race conditions
        var dirsToWrite = pageToWrites.Select(page => page.DestFile.Directory).Where(dir => dir != null).Select(dir => dir!)
            .Concat(tags.Select(group => group.DestDir)).Distinct().ToImmutableArray();
        foreach (var dir in dirsToWrite)
        {
            dir.Create();
        }
        
        var writePostTasks = pageToWrites.Select(page =>
            WritePost(run, vfsWrite, site, roots, templates, page, publicDir));
        var writeTagTasks = tags.Select(tag => WriteTags(run, vfsWrite, site, roots, templates, tag, publicDir));
        
        var counts = await Task.WhenAll(writePostTasks.Concat(writeTagTasks));
        return counts.Sum();
    }


    private static string GetRelativePath(DirectoryInfo publicDir, FileInfo x)
    {
        var rel = Path.GetRelativePath(publicDir.FullName, x.FullName);
        var split = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fin = split.Where(dir => dir != ".");
        return string.Join("/", fin);
    }

    private static string GenerateAbsoluteUrl(Site site, Post post)
    {
        var rel = post.IsIndex ? post.RelativePath.PopBack() : post.RelativePath;
        var relative = string.Join('/', rel.Add("index"));
        return $"{site.Data.Url}/{relative}";
    }

    private static async Task<int> WriteTags(Run run, VfsWrite vfs, Site site, ImmutableArray<RootLinkData> roots,
        TemplateDictionary templates,
        GroupPage tag, DirectoryInfo publicDir)
    {
        var pagesGenerated = 0;
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

            var rootLinks = GenerateRootLinks(roots, tag.Destination, tag.GroupName, ext);
            var data = new PageData(site, rootLinks, tag.Children //.Select(x => new SummaryForPost(x.title, x.time_short, x.time_long, x.name, x.summary, x.full_html, x.full_text))
                , tag.Display, "[summary]", "[url]", tag.Html, tag.Text, "[no date]", "[no date]");
            var renderedPage = selected.Content!(data);
            await vfs.WriteAllTextAsync(path, renderedPage);
            run.Status($"Generated {DisplayNameForFile(path)} from tag {tag.Display} and {DisplayNameForFile(selected.File)}");
            pagesGenerated += 1;
        }

        return pagesGenerated;
    }

    private static async Task<int> WritePost(Run run, VfsWrite vfs, Site site, ImmutableArray<RootLinkData> roots,
        TemplateDictionary templates,
        PageToWrite page, DirectoryInfo publicDir)
    {
        var pagesGenerated = 0;
        var templateName = page.Post.IsIndex ? Constants.DIR_TEMPLATE : Constants.POST_TEMPLATE;

        foreach (var ext in templates.Extensions)
        {
            var templateFiles = page.TemplateFolders
                .Select(dir => dir.GetFile(templateName + ext))
                .Select(file => new {File=file, Content = templates.GetTemplateOrNull(file)})
                .ToImmutableArray()
                ;

            var path = page.DestFile.ChangeExtension(ext);
            var selected = templateFiles.FirstOrDefault(file => file.Content != null);
            if (selected == null)
            {
                var tried = string.Join(' ', templateFiles.Select(x => DisplayNameForFile(x.File)));
                run.WriteError($"Unable to generate {DisplayNameForFile(page.Post.SourceFile)} to {DisplayNameForFile(path)} for {ext}, tried to use: {tried}");
                continue;
            }

            var rootLinks = GenerateRootLinks(roots, page.DestFile, page.RootGroup, ext);
            var data = MakePageData(site, page.Post, rootLinks, page.Summaries);
            var renderedPage = selected.Content!(data);
            await vfs.WriteAllTextAsync(path, renderedPage);
            run.Status($"Generated {DisplayNameForFile(path)} from {DisplayNameForFile(page.Post.SourceFile)} and {DisplayNameForFile(selected.File)}");
            pagesGenerated += 1;
        }

        return pagesGenerated;
    }

    private static ImmutableArray<RootLink> GenerateRootLinks(ImmutableArray<RootLinkData> roots, FileInfo destFile, string rootGroup, string extension)
    {
        return roots
            .Select(x => new RootLink(x.Name,
                GetRelativePath(destFile.Directory!, x.File.ChangeExtension(extension)),
                x.RootGroup == rootGroup)
            )
            .ToImmutableArray();
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
        FileInfo Destination, string Html, string Text, ImmutableArray<SummaryForPost> Children, bool IsRoot, string GroupName);
    public static ImmutableArray<GroupPage> CollectTagPages(Site site, DirectoryInfo publicDir, DirectoryInfo templates, ImmutableArray<PageToWrite> pages)
    {
        var tags = pages
            // flatten all pages to just names->values
            .SelectMany(page => page.Post.Front.TagData)
            // group on name
            .GroupBy(x => x.Key, (groupName, values) => new
            {
                Display = groupName,
                Relative = HttpUtility.UrlEncode(groupName),
                // flatten all values to a single set
                Items = values.SelectMany(x => x.Value)
                    .Distinct()
                    .Select(groupValue => new
                    {
                        Display = groupValue,
                        Relative = HttpUtility.UrlEncode(groupValue),
                        Posts = PagesWithTags(pages, groupName, groupValue)
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
                    tag.Items.Select(x => new SummaryForPost(x.Display, "", "", x.Relative, $"{x.Posts.Length}", "", "", $"{tag.Relative}/{x.Relative}.html")).ToImmutableArray(), true, tag.Relative
                )).ToArray();

        // generate each page: bruce willis/whatever/etc
        var eachTagPage = tags.SelectMany(group => group.Items.Select(tag =>
            new GroupPage(ImmutableArray.Create(templates.GetSubDirs(tag.Relative), templates),
                publicDir.GetDir(group.Relative), tag.Display, publicDir.GetDir(group.Relative).GetFile(tag.Relative),
                $"<h1>{tag.Display}</h1>", tag.Display,
                tag.Posts
                    .Select(p=>MakeSummaryForPost(p, site)).ToImmutableArray(), false, group.Relative
                )
        )).ToArray();
        
        return groupPages.Concat(eachTagPage).ToImmutableArray();

        static ImmutableArray<Post> PagesWithTags(ImmutableArray<PageToWrite> pages, string groupName, string groupValue)
        {
            var ret = pages
                .Select(page => new { page.Post, Values = page.Post.Front.TagData.TryGetValue(groupName, out var val) ? val : null })
                .Where(post => post.Values != null && post.Values.Contains(groupValue))
                .Select(p => p.Post)
                .ToImmutableArray();
            return ret;
        }
    }
}
