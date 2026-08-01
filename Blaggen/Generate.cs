using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Blaggen;

internal static class Generate
{
    // data to mustache
    internal record TemplatePostData(Site Site, Post Post);

    internal record Context(DirectoryInfo Public, FileInfo Target);

    internal record TemplateSectionData(Site Site, Section Section, WriteInfo Write)
    {
        internal Post Post { get; } = Section.Post ??
                                      new Post("index", PostType.Section, new FrontMatter(), new FileInfo(@"C:\missing.md"), "", "");
    }

    internal static class TemplateHelpers
    {
        public static void AddPost<T>(Template.Definition<T, Context> self, Func<T, Post> post, SiteConfig site, ImmutableArray<string> post_types)
        {
            self.AddVar("Title", link => post(link).Front.Title);
            self.AddVar("ContentHtml", link => Template.Str.DontEscape(post(link).Html));
            self.AddVar("ContentText", link => post(link).Plain);
            self.AddVar("Date", link => post(link).Front.Date.ToString(CultureInfo.InvariantCulture));

            // todo(Gustav): how to handle nesting?
            foreach (var type in post_types)
            {
                if (false == site.PageParams.TryGetValue(type, out var param_list)) continue;

                foreach (var pa in param_list)
                {
                    var template_name = $"Params_{pa.Name}";
                    if (pa.Optional)
                    {
                        self.AddBool(template_name, link => post(link).Front.Params.ContainsKey(pa.Name));
                    }

                    if (pa.Var != null)
                    {
                        self.AddList(template_name, (link, _) => post(link).Front.Params.TryGetValue(pa.Name, out var val) ? val.EnumerateArray() : [],
                            new Template.Definition<JsonElement, Context>($"array of {pa}")
                            .AddVar(pa.Var, x => x.ToString())
                            );
                    }
                    else
                    {
                        self.AddVar(template_name, link =>
                        {
                            if (false == post(link).Front.Params.TryGetValue(pa.Name, out var val)) return string.Empty;
                            return val.GetString() ?? string.Empty;
                        });
                    }
                }
            }
        }
        public static void AddSite<T>(Template.Definition<T, Context> self, Func<T, Site> site, SiteConfig config)
        {
            self.AddList("Site_RegularPages", (link, _) => site(link).Root.AllPosts, MakePostLink(config, []));

            self.AddVar("Site_Title", link => site(link).Config.Name);
            self.AddVar("Site_BaseURL", link => site(link).Config.Url);
            foreach (var key in config.Params.Keys)
            {
                self.AddVar($"SiteParams_{key}", link => site(link).Config.Params[key]);
            }

            foreach (var key in config.Menus.Keys)
            {
                self.AddList($"SiteMenus_{key}",
                    (link, ctx) => site(link).Config.Menus[key].OrderBy(x => x.Weight).Select(x => new MenuItemLink(x, ctx)),
                    MakeMenuItem(),
                    Template.DefaultFilterFunctions<MenuItemLink>());
            }
        }
    }
    
    internal static Template.Definition<TemplatePostData, Context> MakePostData(SiteConfig config, ImmutableArray<string> page_types) => new Template.Definition<TemplatePostData, Context>($"PostData[{string.Join('/', page_types)}]")
        .Add(self =>
        {
            TemplateHelpers.AddSite(self, x => x.Site, config);
        })
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x.Post, config, page_types);
        })
    ;

    private record MenuItemLink(MenuItem Menu, Context Context);
    private static Template.Definition<MenuItemLink, Context> MakeMenuItem() => new Template.Definition<MenuItemLink, Context>()
        .AddVar("Name", x => x.Menu.Name)
        .AddVar("URL", x =>
        {
            var base_url = x.Menu.Url;

            if(Uri.TryCreate(base_url, UriKind.RelativeOrAbsolute, out var u))
            {
                if (u.IsAbsoluteUri) return base_url;

                if (base_url.EndsWith('/') == false)
                {
                    var to_file = x.Context.Public.GetFile(base_url);
                    var rel = GetRelativePath(x.Context.Target, to_file);
                    return rel;
                }
            }

            var to_root = Path.GetRelativePath(x.Context.Target.Directory?.FullName ?? "", x.Context.Public.FullName);
            var link = to_root + base_url;
            return link;
        })
    ;

    private static Template.Definition<Post, Context> MakePostLink(SiteConfig site, ImmutableArray<string> post_types) => new Template.Definition<Post, Context>()
        .AddVar("Link", x => x.Name)
        .AddVar("Permalink", x => x.Name) // is this correct???
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x, site, post_types);
        })
    ;

    internal record WriteInfo(FileInfo Target, DirectoryInfo PublicDir);
    internal record SectionLink(Section Section, WriteInfo Write);
    private static Template.Definition<SectionLink, Context> MakeSectionLink() => new Template.Definition<SectionLink, Context>()
        .AddVar("Title", x => x.Section.Post?.Front.Title ?? x.Section.Name)
        // todo(Gustav): add Write Info to link
        .AddVar("Link", x=>x.Section.Name)
    ;

    internal static Template.Definition<TemplateSectionData, Context> MakeSectionData(SiteConfig config, ImmutableArray<string> post_types) => new Template.Definition<TemplateSectionData, Context>()
        .Add(self =>
        {
            TemplateHelpers.AddSite(self, x => x.Site, config);
        })
        .AddBool("hasPost", x => x.Section.Post != null)
        .Add(self =>
        {
            TemplateHelpers.AddPost(self, x => x.Post, config, post_types);
        })
        .AddList("Posts", (x, _) => x.Section.Posts.OrderByDescending(post => post.Front.Date), MakePostLink(config, post_types))
        .AddList("Sections", (x, _) => x.Section.Dirs.Select(y => new SectionLink(y, x.Write)), MakeSectionLink())
    ;

    private static string GetRelativePath(FileInfo from, FileInfo to)
    {
        var rel = Path.GetRelativePath(from.FullName, to.FullName);
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
                var include_index = dirs.Length == 0;
                var section_was_written = false;
                
                if(include_index)
                {
                    var index = FindInTemplate(dirs, g => g.Index);
                    if(index != null)
                    {
                        await vfs_write.WriteAllTextAsync(target, index(data, new Context(public_dir, target)));
                        pages += 1;
                        section_was_written = true;
                    }
                }

                if (section_was_written == false)
                {
                    var gen = FindInTemplate(dirs, g => g.Section);
                    if (gen == null)
                    {
                        var index_or = include_index ? "index/" : "";
                        run.WriteError($"No template found for the {index_or}section {section.SourceDir}");
                    }
                    else
                    {
                        await vfs_write.WriteAllTextAsync(target, gen(data, new Context(public_dir, target)));
                        pages += 1;
                    }
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
                    await vfs_write.WriteAllTextAsync(target, gen(data, new Context(public_dir, target)));
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
