namespace Blaggen;

// ----------------------------------------------------------------------------------------------------------------------------
// App logic

internal static class Constants
{
    internal const string ROOT_FILENAME_WITH_EXTENSION = "site.blaggen.json";


    // actual template file is named _post.mustache.html or similar
    internal const string MUSTACHE_TEMPLATE_POSTFIX = ".mustache";
    internal const string DIR_TEMPLATE = "_dir" + MUSTACHE_TEMPLATE_POSTFIX;
    internal const string POST_TEMPLATE = "_post" + MUSTACHE_TEMPLATE_POSTFIX;

    internal const string DEFAULT_TEMPLATE_NAME = "default";


    internal const string INDEX_NAME = "_index";

    internal static DirectoryInfo CalculateTemplateDirectoryFromString(string templateName, DirectoryInfo root)
    {
        return root.GetDir("themes").GetDir(templateName);
    }

    internal static DirectoryInfo CalculateTemplateDirectory(SiteData site, DirectoryInfo root)
    {
        return CalculateTemplateDirectoryFromString(site.TemplateName, root);
    }

    internal static DirectoryInfo GetContentDirectory(DirectoryInfo root)
    {
        return root.GetDir("content");
    }
}
