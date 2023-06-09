﻿using System.Collections.Immutable;

namespace Blaggen;

public static class Generate
{
    // data to mustache
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

    internal static Template.Definition<SummaryForPost> MakeSummaryForPostDef() => new Template.Definition<SummaryForPost>()
        .AddVar("title", summary => summary.title)
        .AddVar("time_short", summary => summary.time_short)
        .AddVar("time_long", summary => summary.time_long)
        .AddVar("name", summary => summary.name)
        .AddVar("summary", summary => summary.summary)
        .AddVar("full_html", summary => summary.full_html)
        .AddVar("full_text", summary => summary.full_text)
    ;


    // data to mustache
    public record RootLink(string Name, string Url, bool IsSelected);
    internal static Template.Definition<RootLink> MakeRootLinkDef() => new Template.Definition<RootLink>()
        .AddVar("Name", link => link.Name)
        .AddVar("Url", link => link.Url)
        .AddBool("IsSelected", link => link.IsSelected)
    ;


    // data to mustache
    public class PageData
    {
        public readonly string title;
        public readonly string summary;
        public readonly string url;

        public readonly string content_html;
        public readonly string content_text;
        public readonly string time_short;
        public readonly string time_long;
        public readonly List<SummaryForPost> pages;
        public readonly List<RootLink> roots;

        public PageData(Site site, Post post, ImmutableArray<RootLink> rootLinks,
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
            
            this.pages = summaries;
        }
        // todo(Gustav): add more data
        // todo(Gustav): generate full url
    }

    internal static Template.Definition<PageData> MakePageDataDef() => new Template.Definition<PageData>()
        .AddVar("title", page => page.title)
        .AddVar("summary", page => page.summary)
        .AddVar("url", page => page.url)
        .AddVar("content_html", page => page.content_html)
        .AddVar("content_text", page => page.content_text)
        .AddVar("time_short", page => page.time_short)
        .AddVar("time_long", page => page.time_long)
        .AddList("pages", page=>page.pages, MakeSummaryForPostDef())
        .AddList("roots", page=>page.roots, MakeRootLinkDef())
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


    public record PageToWrite
    (
        ImmutableArray<DirectoryInfo> TemplateFolders,
        Post Post,
        ImmutableArray<SummaryForPost> Summaries,
        DirectoryInfo DestDir
    );


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
        var data = new PageData(site, page.Post, rootLinks, page.Summaries.ToList());
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
