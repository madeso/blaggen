namespace Blaggen;

// ----------------------------------------------------------------------------------------------------------------------------
// App logic

internal static class Constants
{
    internal const string ROOT_FILENAME_WITH_EXTENSION = "site.blaggen.json";

    internal const string SECTION_INDEX_NAME_NO_EXT = "_index";
    internal const string TURN_DIR_INTO_POST_NAME_NO_EXT = "index";

    internal const string TEMPLATE_POST = "single";
    internal const string TEMPLATE_SECTION = "list";

    internal static DirectoryInfo CalculateTemplateDirectoryFromString(string template_name, DirectoryInfo root)
    {
        return root.GetDir("themes").GetDir(template_name);
    }

    internal static DirectoryInfo CalculateTemplateDirectory(SiteConfig site, DirectoryInfo root)
    {
        return CalculateTemplateDirectoryFromString(site.TemplateName, root);
    }

    internal static DirectoryInfo GetContentDirectory(DirectoryInfo root)
    {
        return root.GetDir("content");
    }
}
