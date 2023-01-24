using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using Stubble.Core.Builders;
using Stubble.Core;
using Stubble.Core.Settings;
using Stubble.Core.Exceptions;
using Spectre.Console;
using System.Diagnostics;

namespace Blaggen;


public class VfsRead
{
    public async Task<string> ReadAllTextAsync(FileInfo fullName)
    {
        return await File.ReadAllTextAsync(fullName.FullName);
    }
}

public class VfsWrite
{
    public async Task WriteAllTextAsync(FileInfo path, string contents)
    {
        await File.WriteAllTextAsync(path.FullName, contents);
    }
}


public class Run
{
    public int ErrorCount { get; set; } = 0;

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]ERROR[/]: {message}");
        ErrorCount += 1;
    }
}


public static class Facade
{
    public static async Task<int> InitSite(Run run, VfsWrite vfs)
    {
        var existingSite = Input.FindRootFromCurentDirectory();
        if (existingSite != null)
        {
            run.WriteError($"Site already exists at {existingSite.FullName}");
            return -1;
        }

        var site = new SiteData { Name = "My new blog" };
        var json = JsonUtil.Write(site);
        var path = Path.Join(Environment.CurrentDirectory, Constants.ROOT_FILENAME_WITH_EXTENSION);
        await vfs.WriteAllTextAsync(new FileInfo(path), json);

        // todo(Gustav): generate basic templates
        return 0;
    }

    public static async Task<int> NewPost(Run run, VfsRead vfs, VfsWrite vfsWrite, FileInfo path)
    {
        if (path.Exists) { run.WriteError($"Post {path} already exit"); return -1; }

        var pathDir = path.Directory;
        if (pathDir == null) { run.WriteError($"Post {path} isn't rooted"); return -1; }

        var root = Input.FindRoot(pathDir);
        if (root == null) { run.WriteError("Unable to find root"); return -1; }

        var site = await Input.LoadSiteData(run, vfs, root);
        if (site == null) { return -1; }

        // todo(Gustav): create _index.md for each directory depending on setting
        var contentFolder = Input.GetContentDirectory(root);
        var relative = Path.GetRelativePath(contentFolder.FullName, path.FullName);
        if (relative.Contains("..")) { run.WriteError($"Post {pathDir} must be a subpath of {contentFolder}"); return -1; }

        var postNameBase = Path.GetFileNameWithoutExtension(path.Name);
        if (postNameBase == Constants.INDEX_NAME)
        {
            postNameBase = path.Directory!.Name;
        }
        var title = site.CultureInfo.TextInfo.ToTitleCase(postNameBase.Replace('-', ' ').Replace('_', ' '));
        var frontmatter = JsonUtil.Write(new FrontMatter { Title = title });
        var content = $"{Input.SOURCE_START}\n{frontmatter}\n{Input.SOURCE_END}\n{Input.FRONTMATTER_SEP}\n# {title}";

        path.Directory!.Create();
        await vfsWrite.WriteAllTextAsync(path, content);

        Debug.Assert(run.ErrorCount == 0);
        AnsiConsole.MarkupLineInterpolated($"Wrote [blue]${path.FullName}[/]");
        return 0;
    }
}
