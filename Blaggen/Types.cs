using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blaggen;

// Taxonomy / Term / Value(the posts)
internal class Taxonomy
{
    [JsonPropertyName("singular")]
    public string Singular { get; set; } = string.Empty;

    [JsonPropertyName("plural")]
    public string Plural { get; set; } = string.Empty;

    // todo(Gustav): introduce later to speed up generation
    // [JsonPropertyName("terms")]
    // public HashSet<string> Terms { get; set; } = new HashSet<string>();
}

internal class SiteConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("culture")]
    public string Culture { get; set; } = "en-US";

    [JsonPropertyName("theme")]
    public string TemplateName { get; set; } = "theme";

    [JsonPropertyName("dates")]
    public Dictionary<string, string> DateFormats { get; set; } = new()
    {
        {"Short", "g"},
        {"Long", "G"},
    };

    [JsonPropertyName("tags")]
    public Dictionary<string, Taxonomy> Tags = new Dictionary<string, Taxonomy>();

    [JsonPropertyName("url")]
    public string BaseUrl { get; set; } = string.Empty;
    public string Url => BaseUrl.EndsWith('/') ? BaseUrl.TrimEnd('/') : BaseUrl;

    [JsonIgnore]
    internal CultureInfo CultureInfo => new CultureInfo(Culture, false);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class FrontMatter
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; } = DateTime.Now;

    // if whitespace or empty, the value is _default and is the template type to use
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; } = null;

    // note: tag concept is a kind of taxonomy, as is category
    [JsonPropertyName("taxonomy")]
    public Dictionary<string, HashSet<string>> TaxonomyData { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

enum PostType
{
    Post, Section
}

// todo(Gustav): add associated files to be generated...
internal record Post(string Name, PostType Type, FrontMatter Front, FileInfo SourceFile, string Html, string Plain);
internal record Section(string Name, Post? Post, ImmutableArray<Post>? Posts, ImmutableArray<Section> Dirs, DirectoryInfo SourceDir);
internal record Site(SiteConfig Config, Section Root)
{
    public ImmutableArray<string> DebugString
    {
        get
        {
            var sb = new List<string>();
            AddSection(Root, 0);
            return [..sb];

            void AddSection(Section root, int depth)
            {
                var indent = new string(' ', depth*2);

                if (root.Post != null)
                {
                    AddPost(root.Post);
                }

                foreach (var s in root.Dirs)
                {
                    sb.Add($"{indent}{s.Name}/");
                    AddSection(s, depth+1);
                }

                foreach (var f in root.Posts ?? [])
                {
                    AddPost(f);
                }

                return;

                void AddPost(Post post)
                {
                    sb.Add($"{indent}{post.Front.Title}({post.SourceFile}) => {post.Name}");
                }
            }
        }
    }
}
