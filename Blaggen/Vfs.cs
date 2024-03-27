using Spectre.Console;
using System.Collections.Concurrent;
using System.Text;

namespace Blaggen;


public interface VfsRead
{
    public bool Exists(FileInfo fileInfo);
    public Task<string> ReadAllTextAsync(FileInfo path);

    public IEnumerable<FileInfo> GetFiles(DirectoryInfo dir);
    public IEnumerable<DirectoryInfo> GetDirectories(DirectoryInfo root);
    public IEnumerable<FileInfo> GetFilesRec(DirectoryInfo dir);
}

public interface VfsWrite
{
    public Task WriteAllTextAsync(FileInfo path, string contents);
}

public class VfsReadFile : VfsRead
{
    public bool Exists(FileInfo fileInfo)
    {
        return fileInfo.Exists;
    }

    protected async Task<byte[]> ReadBytes(FileInfo fullName)
    {
        var bytes = await File.ReadAllBytesAsync(fullName.FullName);
        return bytes;
    }

    protected static string BytesToString(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }

    public virtual async Task<string> ReadAllTextAsync(FileInfo fullName)
    {
        var bytes = await ReadBytes(fullName);
        return BytesToString(bytes);
    }

    public IEnumerable<FileInfo> GetFiles(DirectoryInfo dir)
    {
        return dir.GetFiles("*", SearchOption.TopDirectoryOnly);
    }

    public IEnumerable<DirectoryInfo> GetDirectories(DirectoryInfo root)
    {
        return root.GetDirectories();
    }

    internal static DirectoryInfo GetCurrentDirectory()
    {
        return new DirectoryInfo(Environment.CurrentDirectory);
    }

    public IEnumerable<FileInfo> GetFilesRec(DirectoryInfo dir)
    {
        return dir.EnumerateFiles("*.*", SearchOption.AllDirectories);
    }
}

public class VfsCachedFileRead : VfsReadFile
{
    private readonly ConcurrentDictionary<string, byte[]> cache = new();

    public override async Task<string> ReadAllTextAsync(FileInfo fullName)
    {
        if (cache.TryGetValue(fullName.FullName, out var bytes) != false) return BytesToString(bytes);

        bytes = await ReadBytes(fullName);
        AddToCache(fullName, bytes);
        return BytesToString(bytes);
    }

    // returns true if cache has been updated, false if not
    private bool AddToCache(FileInfo file, byte[] newBytes)
    {
        while (true)
        {
            byte[] oldBytes;
            while (true)
            {
                if (cache.TryAdd(file.FullName, newBytes))
                    // could add, then return
                    return true;

                if (false == cache.TryGetValue(file.FullName, out var gotBytes))
                    // failed to get value the value, go back to start
                    continue;

                oldBytes = gotBytes;
                break;
            }

            var oldChecksum = Checksum(oldBytes);
            var newChecksum = Checksum(newBytes);

            if (oldChecksum == newChecksum)
            {
                AnsiConsole.WriteLine($"Same checksum: {file.FullName}");
                return false;
            }

            if (false == cache.TryUpdate(file.FullName, newBytes, oldBytes))
                // if cache has changed since we got it... do everything again!
                continue;

            return true;
        }

        static string Checksum(IEnumerable<byte>? bytes)
        {
            // todo(Gustav): add better checksum
            var result = bytes?.Sum(x => x) ?? 0;
            result &= 0xff;
            return result.ToString("X2");
        }
    }

    // return true if the content was updated
    internal async Task<bool> AddFileToCache(FileInfo file)
    {
        try
        {
            var bytes = await ReadBytes(file);
            return AddToCache(file, bytes);
        }
        catch (IOException x)
        {
            AnsiConsole.MarkupLineInterpolated($"ERROR: {file.FullName}: {x.Message}");
            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return false;
        }
    }

    internal void Remove(FileInfo file)
    {
        cache.TryRemove(file.FullName, out _);
    }
}

public class VfsWriteFile : VfsWrite
{
    public async Task WriteAllTextAsync(FileInfo path, string contents)
    {
        await File.WriteAllTextAsync(path.FullName, contents);
    }
}