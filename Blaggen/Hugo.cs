using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Syntax;

namespace Blaggen;

public static class Hugo
{
    private const string TOML_CONTENT_SEPARATOR = "+++";
    // private const string YAML_CONTENT_SEPARATOR = "---";

    internal static bool LooksLikeHugoMarkdown(IEnumerable<string> lines)
    {
        var f = lines.FirstOrDefault(string.Empty).Trim();
        return f is TOML_CONTENT_SEPARATOR; // or YAML_CONTENT_SEPARATOR;
    }

    internal static (FrontMatter fm, string markdownContent) ParseHugoYaml(IEnumerable<string> lines, FileInfo file)
    {
        var (frontmatterYaml, markdownContent) =
            Input.ParseGenericPostData(lines, file, TOML_CONTENT_SEPARATOR, _ => false, skips: 1);

        var parsed = Toml.Parse(frontmatterYaml);
        var root = parsed.IterChildren().First();
        var rc = root.IterChildren()
            .Select(s => s as KeyValueSyntax)
            .Where(s => s != null).Select(s => s!)
            .ToArray();

        var fm = new FrontMatter();

        foreach (var kv in rc)
        {
            switch (kv.KeyString())
            {
                case "date":
                    fm.Date = kv.Value switch
                    {
                        DateTimeValueSyntax dt => dt.Value.DateTime.DateTime,
                        _ => throw new InvalidCastException($"{kv} was not a date")
                    };
                    break;
                case "title":
                    fm.Title = kv.Value!.GetStringValue();
                    break;
                default:
                    var node = ToJson(kv.Value);

                    // is this really the best way to convert from a node to a element?
                    // https://github.com/dotnet/runtime/issues/52611
                    var json = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());

                    static JsonNode ToJson(SyntaxNode? kv)
                    {
                        return kv switch
                        {
                            null => null,
                            ArraySyntax arr => new JsonArray(arr.IterChildren().Select(ToJson).ToArray()),
                            BareKeySyntax v => JsonValue.Create(v.Key?.Text),
                            StringValueSyntax v => JsonValue.Create(v.Value),
                            // BareKeyOrStringValueSyntax v => throw new NotImplementedException(),
                            BooleanValueSyntax v => JsonValue.Create(v.Value),
                            DateTimeValueSyntax v => JsonValue.Create(v.Value),
                            DottedKeyItemSyntax dottedKeyItemSyntax => throw new NotImplementedException(),
                            FloatValueSyntax v => JsonValue.Create(v.Value),
                            InlineTableSyntax inlineTableSyntax => throw new NotImplementedException(),
                            IntegerValueSyntax v => JsonValue.Create(v.Value),
                            KeySyntax key => ToJson(key.Key),
                            _ => throw new ArgumentOutOfRangeException(nameof(kv))
                        } ?? throw new NullReferenceException();
                    }

                    fm.ExtensionData ??= new();
                    fm.ExtensionData.Add(kv.KeyString(), json);
                    break;
            }
        }

        return (fm, markdownContent);
    }
}

public static class TomlExtensions
{
    public static IEnumerable<SyntaxNode> IterChildren(this SyntaxNode node)
    {
        for (int i = 0; i < node.ChildrenCount; i += 1)
        {
            var n = node.GetChild(i);
            if (n != null)
            {
                yield return n;
            }
        }
    }

    public static string KeyString(this KeyValueSyntax kv)
    {
        return kv.Key?.Key switch
        {
            null => throw new NullReferenceException(),
            BareKeySyntax bareKeySyntax => bareKeySyntax.Key?.Text,
            StringValueSyntax sv => sv.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(kv))
        } ?? throw new NullReferenceException();
    }

    public static string GetStringValue(this ValueSyntax value)
    {
        return value switch
        {
            StringValueSyntax sv => sv.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        } ?? throw new NullReferenceException();
    }
}