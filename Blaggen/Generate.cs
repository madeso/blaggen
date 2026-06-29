using System.Collections.Immutable;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.VisualBasic;
using static Blaggen.Generate;

namespace Blaggen;

internal static class Generate
{
    // data to mustache
    internal record SummaryForPost(
        string Title,
        string TimeShort,
        string TimeLong,
        string Name,
        string Summary,
        string FullHtml,
        string FullText,
        string RelativeUrl
        );
    internal record RootLink(string Name, string Url, bool IsSelected);
    internal record PageData(Site Site, ImmutableArray<RootLink> Roots, ImmutableArray<SummaryForPost> Pages,
            string Title,
            string Summary,
            string Url,
            string ContentHtml,
            string ContentText,
            string TimeShort,
            string TimeLong
        );

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
    internal static Template.Definition<PageData> MakePageDataDef() => new Template.Definition<PageData>()
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
}
