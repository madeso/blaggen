using System.Text.Json;

namespace Blaggen;

internal static class JsonUtil
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        IgnoreReadOnlyProperties = true,
    };

    internal static T? Parse<T>(Run run, FileInfo file, string content)
        where T : class
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<T>(content, Options);
            if (loaded == null) { throw new Exception("internal error"); }
            return loaded;
        }
        catch (JsonException err)
        {
            run.WriteError($"Unable to parse json {file}: {err.Message}");
            return null;
        }
    }

    internal static async Task<T?> Load<T>(Run run, VfsRead vfs, FileInfo path)
        where T : class
    {
        var content = await vfs.ReadAllTextAsync(path);
        return Parse<T>(run, path, content);
    }

    internal static string Write<T>(T self)
    {
        return JsonSerializer.Serialize(self, Options);
    }
}
