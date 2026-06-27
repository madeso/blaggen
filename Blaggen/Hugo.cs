using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Tomlyn;
using Tomlyn.Syntax;

namespace Blaggen;

internal static class Hugo
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
        var (frontmatter_yaml, markdown_content) =
            Input.ParseGenericPostData(lines, file, TOML_CONTENT_SEPARATOR, _ => false, skips: 1);

        var parsed = Toml.Parse(frontmatter_yaml);
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
                    var node = JsonFromToml(kv.Value);

                    // is this really the best way to convert from a node to a element?
                    // https://github.com/dotnet/runtime/issues/52611
                    var json = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());

                    fm.ExtensionData ??= new();
                    fm.ExtensionData.Add(kv.KeyString(), json);
                    break;
            }
        }

        return (fm, markdown_content);
    }

    private static JsonNode JsonArrayFromTomlArray(ArraySyntax arr)
    {
        var array_syntax = arr.IterChildren().ToArray();
        if (array_syntax.Length != 3) throw new Exception("Weird array");
        var array_body = array_syntax[1];
        var children = array_body.IterChildren().Select(x => ((ArrayItemSyntax)x).Value).ToArray();
        return new JsonArray(children.Select(JsonFromToml).ToArray());
    }

    private static JsonNode JsonFromToml(SyntaxNode? yaml_key_val)
    {
        return yaml_key_val switch
        {
            null => null,
            ArraySyntax arr => JsonArrayFromTomlArray(arr),
            BareKeySyntax v => JsonValue.Create(v.Key?.Text),
            StringValueSyntax v => JsonValue.Create(v.Value),
            // BareKeyOrStringValueSyntax v => throw new NotImplementedException(),
            BooleanValueSyntax v => JsonValue.Create(v.Value),
            DateTimeValueSyntax v => JsonValue.Create(v.Value),
            DottedKeyItemSyntax dotted_key_item_syntax => throw new NotImplementedException(),
            FloatValueSyntax v => JsonValue.Create(v.Value),
            InlineTableSyntax inline_table_syntax => throw new NotImplementedException(),
            IntegerValueSyntax v => JsonValue.Create(v.Value),
            KeySyntax key => JsonFromToml(key.Key),
            _ => throw new ArgumentOutOfRangeException(nameof(yaml_key_val))
        } ?? throw new NullReferenceException();
    }
}

internal static class TomlExtensions
{
    internal static IEnumerable<SyntaxNode> IterChildren(this SyntaxNode node)
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

    internal static string KeyString(this KeyValueSyntax kv)
    {
        return kv.Key?.Key switch
        {
            null => throw new NullReferenceException(),
            BareKeySyntax bare_key_syntax => bare_key_syntax.Key?.Text,
            StringValueSyntax sv => sv.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(kv))
        } ?? throw new NullReferenceException();
    }

    internal static string GetStringValue(this ValueSyntax value)
    {
        return value switch
        {
            StringValueSyntax sv => sv.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        } ?? throw new NullReferenceException();
    }
}