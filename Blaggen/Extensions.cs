using System.Collections.Immutable;

namespace Blaggen;


public static class FileExtensions
{
    public static DirectoryInfo GetDir(this DirectoryInfo dir, string sub)
    {
        return new DirectoryInfo(Path.Join(dir.FullName, sub));
    }

    public static DirectoryInfo GetSubDirs(this DirectoryInfo dir, IEnumerable<string> sub)
    {
        return sub.Aggregate(dir, (current, name) => current.GetDir(name));
    }

    public static DirectoryInfo GetSubDirs(this DirectoryInfo dir, params string[] sub)
    {
        return sub.Aggregate(dir, (current, name) => current.GetDir(name));
    }

    public static FileInfo GetFile(this DirectoryInfo dir, string file)
    {
        return new FileInfo(Path.Join(dir.FullName, file));
    }

    public async static Task<string?> LoadFileOrNull(this FileInfo path, Run run, VfsRead vfs)
    {
        try { return await vfs.ReadAllTextAsync(path); }
        catch (Exception x)
        {
            run.WriteError($"Failed to load {path.FullName}: {x.Message}");
            return null;
        }
    }

    public static async Task<string?> LoadFileSilentOrNull(this FileInfo path, VfsRead vfs)
    {
        try { return await vfs.ReadAllTextAsync(path); }
        catch
        {
            return null;
        }
    }
}

public static class DictionaryExtensions
{
    public static void AddRange<K, V>(this Dictionary<K, V> data, IEnumerable<KeyValuePair<K, V>> list)
        where K : class
        where V : class
    { foreach (var (k, v) in list) { data.Add(k, v); } }
}

public static class ImmutableArrayExtensions
{
    public static ImmutableArray<T> PopBack<T>(this ImmutableArray<T> data)
    {
        if (data.Length == 0) { return data; }
        var ret = data.RemoveAt(data.Length - 1);
        return ret;
    }
}

public static class IterTools
{
    // returns: initial+p0, initial+p0+p1, initial+p0+p1+p2 ...
    public static IEnumerable<R> Accumulate<T, R>(this IEnumerable<T> src, R initial, Func<T, R, R> add)
    {
        var current = initial;
        foreach (var t in src)
        {
            current = add(t, current);
            yield return current;
        }
    }

    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> asyncEnumerable)
    {
        if (null == asyncEnumerable)
            throw new ArgumentNullException(nameof(asyncEnumerable));

        var list = new List<T>();
        await foreach (var t in asyncEnumerable)
        {
            list.Add(t);
        }

        return list;
    }
}