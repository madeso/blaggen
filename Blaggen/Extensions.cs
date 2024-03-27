using ColorCode.Compilation.Languages;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Blaggen;


internal static class FileExtensions
{
    internal static DirectoryInfo GetDir(this DirectoryInfo dir, string sub)
    {
        return new DirectoryInfo(Path.Join(dir.FullName, sub));
    }

    internal static DirectoryInfo GetSubDirs(this DirectoryInfo dir, IEnumerable<string> sub)
    {
        return sub.Aggregate(dir, (current, name) => current.GetDir(name));
    }

    internal static DirectoryInfo GetSubDirs(this DirectoryInfo dir, params string[] sub)
    {
        return sub.Aggregate(dir, (current, name) => current.GetDir(name));
    }

    internal static FileInfo GetFile(this DirectoryInfo dir, string file)
    {
        return new FileInfo(Path.Join(dir.FullName, file));
    }

    internal static FileInfo ChangeExtension(this FileInfo file, string newExtension)
    {
        return new FileInfo(Path.ChangeExtension(file.FullName, newExtension));
    }

    internal static async Task<string?> LoadFileOrNull(this FileInfo path, Run run, VfsRead vfs)
    {
        try { return await vfs.ReadAllTextAsync(path); }
        catch (Exception x)
        {
            run.WriteError($"Failed to load {path.FullName}: {x.Message}");
            return null;
        }
    }

    internal static string DisplayNameForFile(this FileInfo file) => Path.GetRelativePath(Environment.CurrentDirectory, file.FullName);
}

internal static class ImmutableArrayExtensions
{
    internal static ImmutableArray<T> PopBack<T>(this ImmutableArray<T> data)
    {
        if (data.Length == 0) { return data; }
        var ret = data.RemoveAt(data.Length - 1);
        return ret;
    }
}

internal static class IterTools
{
    // returns: initial+p0, initial+p0+p1, initial+p0+p1+p2 ...
    internal static IEnumerable<R> Accumulate<T, R>(this IEnumerable<T> src, R initial, Func<T, R, R> add)
    {
        var current = initial;
        foreach (var t in src)
        {
            current = add(t, current);
            yield return current;
        }
    }

    internal static IEnumerable<T> Where<T>(this IEnumerable<T> src, Func<T, bool> predicate, Action<T> fail)
    {
        foreach (var t in src)
        {
            if (predicate(t))
            {
                yield return t;
            }
            else
            {
                fail(t);
            }
        }
    }

    internal static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> asyncEnumerable)
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

internal static class ChannelExtension
{
    internal static async IAsyncEnumerable<T> ReadAsyncOrCancel<T>(this ChannelReader<T> reader, [EnumeratorCancellation] CancellationToken ct)
    {
        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var postPath) && ct.IsCancellationRequested==false)
            {
                yield return postPath;
            }
        }
    }
}