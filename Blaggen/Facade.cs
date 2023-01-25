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


public interface VfsRead
{
    bool Exists(FileInfo fileInfo);
    public Task<string> ReadAllTextAsync(FileInfo fullName);
}

public interface VfsWrite
{
    public Task WriteAllTextAsync(FileInfo path, string contents);
}

public interface Run
{
    public void WriteError(string message);
    public bool HasError();
    public void Status(string message);
}



public static class Facade
{
    public static async Task<int> InitSite(Run run, VfsRead read, VfsWrite vfs, DirectoryInfo currentDirectory)
    {
        var existingSite = Input.FindRoot(read, currentDirectory);
        if (existingSite != null)
        {
            run.WriteError($"Site already exists at {existingSite.FullName}");
            return -1;
        }

        var site = new SiteData { Name = "My new blog" };
        var json = JsonUtil.Write(site);
        var path = currentDirectory.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION);
        await vfs.WriteAllTextAsync(path, json);

        // todo(Gustav): generate basic templates
        return 0;
    }

    public static async Task<int> NewPost(Run run, VfsRead vfs, VfsWrite vfsWrite, FileInfo path)
    {
        if (vfs.Exists(path)) { run.WriteError($"Post {path} already exist"); return -1; }

        var pathDir = path.Directory;
        if (pathDir == null) { run.WriteError($"Post {path} isn't rooted"); return -1; }

        var root = Input.FindRoot(vfs, pathDir);
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

        Debug.Assert(run.HasError() == false);
        AnsiConsole.MarkupLineInterpolated($"Wrote [blue]${path.FullName}[/]");
        return 0;
    }

    public static async Task<int> GenerateSite(Run run, VfsRead vfs, VfsWrite vfsWrite, DirectoryInfo currentDirectory)
    {
        var root = Input.FindRoot(vfs, currentDirectory);
        if (root == null) { run.WriteError("Unable to find root"); return -1; }

        var timeStart = DateTime.Now;

        run.Status("Parsing directory");
        var site = await Input.LoadSite(run, vfs, root, new Markdown());
        if (site == null) { return -1; }

        var publicDir = root.GetDir("public");
        var templates = await Templates.Load(run, vfs, root);
        var unloadedPartials = root.GetDir("partials").EnumerateFiles()
            .Select(async file => new { Name = Path.GetFileNameWithoutExtension(file.Name), Content = await vfs.ReadAllTextAsync(file) })
            ;
        var partials = (await Task.WhenAll(unloadedPartials))
            .Select(d => new KeyValuePair<string, object>(d.Name, new Func<object>(() => d.Content)))
            .ToImmutableArray()
            ;

        run.Status("Writing data to disk");
        var pagesGenerated = await Generate.WriteSite(run, vfsWrite, site, publicDir, templates, partials);
        // todo(Gustav): copy static files

        var timeEnd = DateTime.Now;
        var timeTaken = timeEnd - timeStart;
        AnsiConsole.MarkupLineInterpolated($"Wrote [green]{pagesGenerated}[/] files in [blue]{timeTaken}[/]");

        return run.HasError() ? -1 : 0;
    }
}
