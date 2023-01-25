using Blaggen;
using System.Threading.Tasks.Dataflow;

namespace BlaggenTest;

internal class VfsReadTest : VfsRead
{
    private readonly Dictionary<string, string> files = new();

    public bool Exists(FileInfo fileInfo)
    {
        return files.ContainsKey(fileInfo.FullName);
    }

    public Task<string> ReadAllTextAsync(FileInfo fullName)
    {
        return new Task<string>(() => files[fullName.Name]);
    }

    public void AddContent(FileInfo file, string content)
    {
        files.Add(file.FullName, content);
    }
}

internal class VfsWriteTest : VfsWrite
{
    private readonly Dictionary<string, string> files = new();

    public Task WriteAllTextAsync(FileInfo path, string contents)
    {
        return new Task( () => files.Add(path.FullName, contents) );
    }

    public string GetContent(FileInfo file)
    {
        if (files.Remove(file.FullName, out var content))
        {
            return content;
        }
        else
        {
            throw new FileNotFoundException($"{file.FullName} was not added to container");
        }
    }

    internal bool IsEmpty()
    {
        return files.Count == 0;
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
        throw new NotImplementedException();
    }

    public void WriteError(string message)
    {
        Errors.Add(message);
    }
}
