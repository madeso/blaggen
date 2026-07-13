using System.Collections.Immutable;
using System.Globalization;

namespace Blaggen;

internal static class Generate
{
    // data to mustache
    internal record TemplatePostData(Site Site, Post Post);

    internal record TemplateSectionData(Site Site, Section Section)
    {
        internal Post Post { get; } = Section.Post ??
                                      new Post("index", PostType.Section, new FrontMatter(), new FileInfo(@"C:\missing.md"), "", "");
    }

    internal static class TemplateHelpers
    {
        public static void AddPost<T>(Template.Definition<T> self, Func<T, Post> post)
        {
            self.AddVar("Title", link => post(link).Front.Title);
            self.AddVar("ContentHtml", link => post(link).Html);
            self.AddVar("ContentText", link => post(link).Plain);
            self.AddVar("Date", link => post(link).Front.Date.ToString(CultureInfo.InvariantCulture));
        }
        public static void AddSite<T>(Template.Definition<T> self, Func<T, Site> site, SiteConfig config)
        {
            /*
            array SiteMenus_main: No match in 0: []
           array SiteMenus_main: No match in 2: [Posts, Sections]
           attribute Content: did you mean [ContentHtml, ContentText, Site, Date, Title] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Permalink: did you mean [Link, Title, Date] of 5: [Link, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute Site_BaseURL: did you mean [Site, Title, ContentText, Date] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute SiteTitle: did you mean [Title, Site, Date, ContentHtml, ContentText] of 5: [Site, Title, ContentHtml, ContentText, Date]
           attribute SiteTitle: did you mean [Title, Site, Date, ContentHtml, ContentText] of 5: [Site, Title, ContentHtml, ContentText, Date]
             */
            self.AddVar("Site", link => site(link).Config.Name);
            foreach(var key in config.Params.Keys)
            {
                self.AddVar($"SiteParams_{key}", link => site(link).Config.Params[key]);
            }
        }
    }
    
    internal static Template.Definition<TemplatePostData> MakePostData(SiteConfig config) => new Template.Definition<TemplatePostData>()
        .Add(self =>
        {
            TemplateHelpers.AddSite(self, x => x.Site, config);
        })
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x.Post);
        })
    ;

    private static Template.Definition<Post> MakePostLink() => new Template.Definition<Post>()
        .AddVar("Link", x => x.Name)
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x);
        })
    ;
    private static Template.Definition<Section> MakeSectionLink() => new Template.Definition<Section>()
        .AddVar("Title", x => x.Post?.Front.Title ?? x.Name)
        .AddVar("Link", x=>x.Name)
    ;

    internal static Template.Definition<TemplateSectionData> MakeSectionData(SiteConfig config) => new Template.Definition<TemplateSectionData>()
        .Add(self =>
        {
            TemplateHelpers.AddSite(self, x => x.Site, config);
        })
        .AddBool("hasPost", x => x.Section.Post != null)
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x.Post);
        })
        .AddList("Posts", x => x.Section.Posts ?? [], MakePostLink())
        .AddList("Sections", x => x.Section.Dirs, MakeSectionLink())
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
                var data = new TemplateSectionData(site, section);
                var gen = FindInTemplate(dirs, g => g.Section);
                if (gen == null)
                {
                    run.WriteError($"No template found for section {section.SourceDir}");
                }
                else
                {
                    await vfs_write.WriteAllTextAsync(public_dir.GetSubDirs(dirs).GetFile("index.html"),
                        gen(data));
                    pages += 1;
                }
            }

            // write pages
            foreach (var p in section.Posts ?? [])
            {
                var data = new TemplatePostData(site, p);
                var gen = FindInTemplate(dirs, g => g.Post);
                if (gen == null)
                {
                    run.WriteError($"No template found for post {p.SourceFile}");
                }
                else
                {
                    await vfs_write.WriteAllTextAsync(public_dir.GetSubDirs(dirs).GetDir(p.Name).GetFile("index.html"),
                        gen(data));
                    pages += 1;
                }
            }

            // write sub sections
            foreach (var s in section.Dirs)
            {
                pages += await WriteSiteRec(s, dirs.Add(section.Name));
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
