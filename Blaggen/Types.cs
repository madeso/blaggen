using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Blaggen;


public class SiteData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("culture")]
    public string Culture { get; set; } = "en-US";

    [JsonPropertyName("short_date_format")]
    public string ShortDateFormat { get; set; } = "g";

    [JsonPropertyName("long_date_format")]
    public string LongDateFormat { get; set; } = "G";

    [JsonPropertyName("url")]
    public string BaseUrl { get; set; } = string.Empty;
    public string Url => BaseUrl.EndsWith('/') ? BaseUrl.TrimEnd('/') : BaseUrl;

    [JsonIgnore]
    public CultureInfo CultureInfo
    {
        get
        {
            return new CultureInfo(Culture, false);
        }
    }

    public string ShortDateToString(DateTime dt)
    {
        return dt.ToString(ShortDateFormat, this.CultureInfo);
    }

    public string LongDateToString(DateTime dt)
    {
        return dt.ToString(ShortDateFormat, this.CultureInfo);
    }
}

public class FrontMatter
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; } = DateTime.Now;

    [JsonPropertyName("tags")]
    public HashSet<string> Tags { get; set; } = new();
}

// todo(Gustav): add associated files to be generated...
public record Post(Guid Id, bool IsIndex, ImmutableArray<string> RelativePath, FrontMatter Front, FileInfo SourceFile, string Name, string MarkdownHtml, string MarkdownPlainText);
public record Dir(Guid Id, string Title, string Name, ImmutableArray<Post> Posts, ImmutableArray<Dir> Dirs);
public record Site(SiteData Data, Dir Root);
