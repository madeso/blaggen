using System.Collections.Immutable;

namespace Blaggen;

public static class Generate
{
    // data to mustache
    public record SummaryForPost(Post Post, Site Site);
    public record RootLink(string Name, string Url, bool IsSelected);
    public record PageData(Site Site, Post Post, ImmutableArray<RootLink> Roots, ImmutableArray<SummaryForPost> Pages);


    public record PageToWrite
    (
        ImmutableArray<DirectoryInfo> TemplateFolders,
        Post Post,
        ImmutableArray<SummaryForPost> Summaries,
        DirectoryInfo DestDir
    );

    private static Template.Definition<SummaryForPost> MakeSummaryForPostDef() => new Template.Definition<SummaryForPost>()
        .AddVar("title", s => s.Post.Front.Title)
        .AddVar("time_short", s => s.Site.Data.ShortDateToString(s.Post.Front.Date))
        .AddVar("time_long", s => s.Site.Data.LongDateToString(s.Post.Front.Date))
        .AddVar("name", s => s.Post.Name)
        .AddVar("summary", s => s.Post.Front.Summary)
        .AddVar("full_html", s => s.Post.MarkdownHtml)
        .AddVar("full_text", s => s.Post.MarkdownPlainText)
    ;

    
    private static Template.Definition<RootLink> MakeRootLinkDef() => new Template.Definition<RootLink>()
        .AddVar("Name", link => link.Name)
        .AddVar("Url", link => link.Url)
        .AddBool("IsSelected", link => link.IsSelected)
    ;

    // todo(Gustav): add more data
    // todo(Gustav): generate full url
    public static Template.Definition<PageData> MakePageDataDef() => new Template.Definition<PageData>()
        .AddVar("title", page => page.Post.Front.Title)
        .AddVar("summary", page => page.Post.Front.Summary)
        .AddVar("url", page => GenerateAbsoluteUrl(page.Site, page.Post))
        .AddVar("content_html", page => page.Post.MarkdownHtml)
        .AddVar("content_text", page => page.Post.MarkdownPlainText)
        .AddVar("time_short", page => page.Site.Data.ShortDateToString(page.Post.Front.Date))
        .AddVar("time_long", page => page.Site.Data.LongDateToString(page.Post.Front.Date))
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
                .Select(post => new SummaryForPost(post, site))
                .ToImmutableArray();
            foreach (var post in dir.Posts)
            {
                // todo(Gustav): paginate index using Chunk(size)
                var destDir = post.IsIndex ? targetDir : targetDir.GetDir(post.Name);
                yield return new PageToWrite(templateFolders, post, summaries, destDir);
            }
        }
    }


    public static async Task<int> WritePages(ImmutableArray<PageToWrite> pageToWrites, Run run, VfsWrite vfsWrite,
        Site site, DirectoryInfo publicDir, TemplateDictionary templates)
    {
        var roots = pageToWrites
            .Where(x => GetRelativePath(publicDir, x).Count() == 1)
            .ToImmutableArray()
            ;

        // create all directories first, to avoid race conditions
        foreach (var dir in pageToWrites.Select(page => page.DestDir).Distinct())
        {
            dir.Create();
        }

        var writeTasks = pageToWrites.Select(page =>
            WritePost(run, vfsWrite, site, roots, templates, page, publicDir));
        
        var counts = await Task.WhenAll(writeTasks);
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


    private static async Task<int> WritePost(Run run, VfsWrite vfs, Site site, ImmutableArray<PageToWrite> roots,
        TemplateDictionary templates,
        PageToWrite page, DirectoryInfo publicDir)
    {
        var rootLinks = roots
                .Select(x => new RootLink(x.Post.Front.Title,
                    ConcatWithIndex(GetRelativePath(page.DestDir, x)),
                    ConcatWithIndex(GetRelativePath(publicDir, x)) == ConcatWithIndex(GetRelativePath(publicDir, page).Take(1))
                ))
                .ToImmutableArray()
            ;
        var pagesGenerated = 0;
        var data = new PageData(site, page.Post, rootLinks, page.Summaries);
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
}
