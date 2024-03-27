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


    internal const string INDEX_NAME = "_index";

    internal static DirectoryInfo CalculateTemplateDirectory(DirectoryInfo root)
    {
        return root.GetDir("templates");
    }

    internal static DirectoryInfo GetContentDirectory(DirectoryInfo root)
    {
        return root.GetDir("content");
    }
}
