﻿using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blaggen;


internal class SiteData
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
    internal CultureInfo CultureInfo => new CultureInfo(Culture, false);

    internal string ShortDateToString(DateTime dt)
    {
        return dt.ToString(ShortDateFormat, CultureInfo);
    }

    internal string LongDateToString(DateTime dt)
    {
        return dt.ToString(ShortDateFormat, CultureInfo);
    }

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

    // a dictionary since there is a difference between tags the concept and "tags" the tag
    // a site could also choose to tag posts with "authors", group or whatever tags may fit the content
    [JsonPropertyName("tags")]
    public Dictionary<string, HashSet<string>> TagData { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// todo(Gustav): add associated files to be generated...
internal record Post(Guid Id, bool IsIndex, ImmutableArray<string> RelativePath, FrontMatter Front, FileInfo SourceFile, string Name, string MarkdownHtml, string MarkdownPlainText, string Markdown);
internal record Dir(Guid Id, string Title, string Name, ImmutableArray<Post> Posts, ImmutableArray<Dir> Dirs);
internal record Site(SiteData Data, Dir Root);
