using Blaggen;
using System.Threading.Tasks.Dataflow;

namespace BlaggenTest;

internal class VfsReadTest : VfsRead
{
    class Entry
    {
        public readonly Dictionary<string, Entry> directories = new();
        public readonly List<FileInfo> files = new();
    }

    private readonly Dictionary<string, Entry> directories = new();
    private readonly Dictionary<string, string> files = new();

    private Entry GetEntry(DirectoryInfo dir)
    {
        if(directories.TryGetValue(dir.FullName, out var entry))
        { return entry; }

        entry = new Entry();
        directories[dir.FullName] = entry;

        var parent = dir.Parent;
        if(parent != null)
        {
            GetEntry(parent).directories.Add(dir.Name, entry);
        }

        return entry;
    }

    public bool Exists(FileInfo fileInfo)
    {
        return files.ContainsKey(fileInfo.FullName);
    }

    public Task<string> ReadAllTextAsync(FileInfo fullName)
    {
        return Task<string>.Factory.StartNew(() => files[fullName.FullName]);
    }

    public void AddContent(FileInfo file, string content)
    {
        var dir = file.Directory;
        if(dir != null)
        {
            GetEntry(dir).files.Add(file);
        }

        files.Add(file.FullName, content);
    }

    public IEnumerable<FileInfo> GetFiles(DirectoryInfo dir)
    {
        return GetEntry(dir).files;
    }

    public IEnumerable<DirectoryInfo> GetDirectories(DirectoryInfo root)
    {
        return GetEntry(root).directories.Keys.Select(root.GetDir);
    }

    public IEnumerable<FileInfo> GetFilesRec(DirectoryInfo dir)
    {
        return RecurseFiles(GetEntry(dir));

        static IEnumerable<FileInfo> RecurseFiles(Entry entry)
        {
            foreach (var d in entry.directories.Values)
            {
                foreach(var f in RecurseFiles(d))
                {
                    yield return f;
                }
            }
            foreach(var f in entry.files)
            {
                yield return f;
            }
        }
    }
}

internal class VfsWriteTest : VfsWrite
{
    private readonly Dictionary<string, string> files = new();

    public Task WriteAllTextAsync(FileInfo path, string contents)
    {
        return Task.Factory.StartNew( () => { files.Add(path.FullName, contents); } );
    }

    public string GetContent(FileInfo file)
    {
        if (files.Remove(file.FullName, out var content))
        {
            return content;
        }
        else
        {
            throw new FileNotFoundException($"{file.FullName} was not added to container.\n{GetRemainingFilesAsText()}");
        }
    }

    public IEnumerable<string> RemainingFiles => files.Keys;

    internal bool IsEmpty()
    {
        return files.Count == 0;
    }

    internal string GetRemainingFilesAsText()
    {
        var fs = string.Join(" ", files.Keys);
        return $"files: [{fs}]";
    }
}

internal class RunTest : Run
{
    public List<string> Errors { get; } = new();

    public bool HasError()
    {
        return Errors.Count > 0;
    }

    public void Status(string message)
    {
    }

    public void WriteError(string message)
    {
        Errors.Add(message);
    }

    internal string GetOutput()
    {
        return string.Join("\n", Errors);
    }
}

public class TestBase
{
    internal RunTest run = new();
    internal VfsReadTest read = new();
    internal VfsWriteTest write = new();
}
