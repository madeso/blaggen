using System.Collections.Immutable;
using System.Globalization;

namespace Blaggen;

internal static class Generate
{
    // data to mustache
    internal record TemplatePostData(Site Site, Post Post);

    internal record Context();

    internal record TemplateSectionData(Site Site, Section Section, WriteInfo Write)
    {
        internal Post Post { get; } = Section.Post ??
                                      new Post("index", PostType.Section, new FrontMatter(), new FileInfo(@"C:\missing.md"), "", "");
    }

    internal static class TemplateHelpers
    {
        public static void AddPost<T>(Template.Definition<T, Context> self, Func<T, Post> post)
        {
            self.AddVar("Title", link => post(link).Front.Title);
            self.AddVar("ContentHtml", link => Template.Str.DontEscape(post(link).Html));
            self.AddVar("ContentText", link => post(link).Plain);
            self.AddVar("Date", link => post(link).Front.Date.ToString(CultureInfo.InvariantCulture));

            /*
            attribute Permalink: did you mean [Link, Title, Date] of 5: [Link, Title, ContentHtml, ContentText, Date]
            attribute Content: did you mean [ContentHtml, ContentText, Site, Date, Title] of 5: [Site, Title, ContentHtml, ContentText, Date]
            */
        }
        public static void AddSite<T>(Template.Definition<T, Context> self, Func<T, Site> site, SiteConfig config)
        {
            /*
            array SiteMenus_main: No match in 0: []
            array SiteMenus_main: No match in 2: [Posts, Sections]
            */
            self.AddVar("Site_Title", link => site(link).Config.Name);
            self.AddVar("Site_BaseURL", link => site(link).Config.Url);
            foreach (var key in config.Params.Keys)
            {
                self.AddVar($"SiteParams_{key}", link => site(link).Config.Params[key]);
            }

            foreach (var key in config.Menus.Keys)
            {
                self.AddList($"SiteMenus_{key}", link => site(link).Config.Menus[key].OrderBy(x => x.Weight), MakeMenuItem());
            }
        }
    }
    
    internal static Template.Definition<TemplatePostData, Context> MakePostData(SiteConfig config) => new Template.Definition<TemplatePostData, Context>()
        .Add(self =>
        {
            TemplateHelpers.AddSite(self, x => x.Site, config);
        })
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x.Post);
        })
    ;

    // todo(Gustav): expand with WriteInfo
    private static Template.Definition<MenuItem, Context> MakeMenuItem() => new Template.Definition<MenuItem, Context>()
        .AddVar("Name", x => x.Name)
        .AddVar("URL", x => x.Url)
    ;

    private static Template.Definition<Post, Context> MakePostLink() => new Template.Definition<Post, Context>()
        .AddVar("Link", x => x.Name)
        .AddVar("Permalink", x => x.Name) // is this correct???
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x);
        })
    ;

    internal record WriteInfo(FileInfo Target, DirectoryInfo PublicDir);
    internal record SectionLink(Section Section, WriteInfo Write);
    private static Template.Definition<SectionLink, Context> MakeSectionLink() => new Template.Definition<SectionLink, Context>()
        .AddVar("Title", x => x.Section.Post?.Front.Title ?? x.Section.Name)
        // todo(Gustav): add Write Info to link
        .AddVar("Link", x=>x.Section.Name)
    ;

    internal static Template.Definition<TemplateSectionData, Context> MakeSectionData(SiteConfig config) => new Template.Definition<TemplateSectionData, Context>()
        .Add(self =>
        {
            TemplateHelpers.AddSite(self, x => x.Site, config);
        })
        .AddBool("hasPost", x => x.Section.Post != null)
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x.Post);
        })
        .AddList("Posts", x => x.Section.Posts.OrderByDescending(post => post.Front.Date), MakePostLink())
        .AddList("Sections", x => x.Section.Dirs.Select(y => new SectionLink(y, x.Write)), MakeSectionLink())
    ;

    private static string GetRelativePath(DirectoryInfo public_dir, FileInfo x)
    {
        var rel = Path.GetRelativePath(public_dir.FullName, x.FullName);
        var split = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fin = split.Where(dir => dir != ".");
        return string.Join("/", fin);
    }

    private static string GenerateAbsoluteUrl(Site site, Post post)
    {
        return "";
        // var rel = post.IsIndex ? post.RelativePath.PopBack() : post.RelativePath;
        // var relative = string.Join('/', rel.Add("index"));
        // return $"{site.Config.Url}/{relative}";
    }

    public static async Task<int> WriteSite(Run run, Site site, VfsWrite vfs_write, TemplateDictionary templates,
        DirectoryInfo public_dir)
    {
        return await WriteSiteRec(site.Root, []);
        

        async Task<int> WriteSiteRec(Section section, ImmutableArray<string> dirs)
        {
            int pages = 0;
            // write section
            {
                var target = public_dir.GetSubDirs(dirs).GetFile("index.html");
                var data = new TemplateSectionData(site, section, new WriteInfo(target, public_dir));
                var gen = FindInTemplate(dirs, g => g.Section);
                if (gen == null)
                {
                    run.WriteError($"No template found for section {section.SourceDir}");
                }
                else
                {
                    await vfs_write.WriteAllTextAsync(target, gen(data, new Context()));
                    pages += 1;
                }
            }

            // write pages
            foreach (var p in section.Posts)
            {
                var data = new TemplatePostData(site, p);
                var gen = FindInTemplate(dirs, g => g.Post);
                if (gen == null)
                {
                    run.WriteError($"No template found for post {p.SourceFile}");
                }
                else
                {
                    var target = public_dir.GetSubDirs(dirs).GetDir(p.Name).GetFile("index.html");
                    await vfs_write.WriteAllTextAsync(target, gen(data, new Context()));
                    pages += 1;
                }
            }

            // write sub sections
            foreach (var s in section.Dirs)
            {
                pages += await WriteSiteRec(s, dirs.Add(s.Name));
            }

            return pages;
        }

        T? FindInTemplate<T>(ImmutableArray<string> dirs, Func<TemplateFolder, T> selector) where T : class?
        {
            var d = dirs;
            while (true)
            {
                var found = templates.GetProp(d, selector);
                if (found != null) return found;
                if (d.Length == 0) return null;
                d = d.PopBack();
            }
        }
    }
}
