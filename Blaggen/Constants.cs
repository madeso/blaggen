namespace Blaggen;

// ----------------------------------------------------------------------------------------------------------------------------
// App logic

public static class Constants
{
    public const string ROOT_FILENAME_WITH_EXTENSION = "site.blaggen.json";


    // actual template file is named _post.mustache.html or similar
    public const string MUSTACHE_TEMPLATE_POSTFIX = ".mustache";
    public const string DIR_TEMPLATE = "_dir" + MUSTACHE_TEMPLATE_POSTFIX;
    public const string POST_TEMPLATE = "_post" + MUSTACHE_TEMPLATE_POSTFIX;


    public const string INDEX_NAME = "_index";

    public static DirectoryInfo CalculateTemplateDirectory(DirectoryInfo root)
    {
        return root.GetDir("templates");
    }

    public static DirectoryInfo GetContentDirectory(DirectoryInfo root)
    {
        return root.GetDir("content");
    }
}
