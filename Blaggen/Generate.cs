using System.Collections.Immutable;

namespace Blaggen;

internal static class Generate
{
    // data to mustache
    internal record TemplatePostData(string Data);
    internal record TemplateSectionData(string Data);

    
    internal static Template.Definition<TemplatePostData> MakePostData() => new Template.Definition<TemplatePostData>()
        .AddVar("Name", link => link.Data)
    ;

    internal static Template.Definition<TemplateSectionData> MakeSectionData() => new Template.Definition<TemplateSectionData>()
        .AddVar("Name", link => link.Data)
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
            if(section.Post != null)
            {
                var data = Input.TemplateDataFromSection(site, section, section.Post);
                var gen = FindInTemplate(dirs, g => g.Section);
                if (gen == null)
                {
                    run.WriteError($"No template found for section {section.SourceDir}");
                }
                else
                {
                    await vfs_write.WriteAllTextAsync(public_dir.GetSubDirs(dirs).GetFile(section.Post.Name + ".html"),
                        gen(data));
                    pages += 1;
                }
            }

            // write pages
            foreach (var p in section.Posts ?? [])
            {
                var data = Input.TemplateDataFromPost(site, p);
                var gen = FindInTemplate(dirs, g => g.Post);
                if (gen == null)
                {
                    run.WriteError($"No template found for post {p.SourceFile}");
                }
                else
                {
                    await vfs_write.WriteAllTextAsync(public_dir.GetSubDirs(dirs).GetFile(p.Name + ".html"),
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
