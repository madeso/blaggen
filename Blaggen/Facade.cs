﻿using System.Collections.Immutable;
using System.Diagnostics;
using Spectre.Console;

namespace Blaggen;


public interface VfsRead
{
    bool Exists(FileInfo fileInfo);
    public Task<string> ReadAllTextAsync(FileInfo path);

    public IEnumerable<FileInfo> GetFiles(DirectoryInfo dir);
    IEnumerable<DirectoryInfo> GetDirectories(DirectoryInfo root);
    IEnumerable<FileInfo> GetFilesRec(DirectoryInfo dir);
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

    public static string GeneratePostWithTitle(string title, FrontMatter fm)
    {
        fm.Title = title;
        return GeneratePost($"# {title}", fm);
    }

    public static string GeneratePost(string markdown, FrontMatter fm)
    {
        var frontmatter = JsonUtil.Write(fm);
        var content = $"{Input.SOURCE_START}\n{frontmatter}\n{Input.SOURCE_END}\n{Input.FRONTMATTER_SEP}\n{markdown}";
        return content;
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
        var contentFolder = Constants.GetContentDirectory(root);
        var relative = Path.GetRelativePath(contentFolder.FullName, path.FullName);
        if (relative.Contains("..")) { run.WriteError($"Post {pathDir} must be a subpath of {contentFolder}"); return -1; }

        var postNameBase = Path.GetFileNameWithoutExtension(path.Name);
        if (postNameBase == Constants.INDEX_NAME)
        {
            postNameBase = path.Directory!.Name;
        }
        var title = site.CultureInfo.TextInfo.ToTitleCase(postNameBase.Replace('-', ' ').Replace('_', ' '));
        var content = GeneratePostWithTitle(title, new FrontMatter());

        path.Directory!.Create();
        await vfsWrite.WriteAllTextAsync(path, content);

        Debug.Assert(run.HasError() == false);
        AnsiConsole.MarkupLineInterpolated($"Wrote [blue]${path.FullName}[/]");
        return 0;
    }


    public static Task<int> MigrateFromHugo(Run run, VfsRead vfsRead, VfsWrite vfsWrite, DirectoryInfo currentDirectory)
    {
        var root = Input.FindRoot(vfsRead, currentDirectory);
        if (root == null) { run.WriteError("Unable to find root"); return Task.FromResult(-1); }

        run.Status("Finding files");
        var contentFolder = Constants.GetContentDirectory(root);
        var files = vfsRead.GetFilesRec(contentFolder)
            .Where(file => file.Extension == ".md")
            .ToImmutableArray()
            ;

        run.Status("Migrating hugo markdowns");
        var writeTasks = files.Select(async file =>
        {
            var lines = (await vfsRead.ReadAllTextAsync(file)).Split('\n');
            if (false == Hugo.LooksLikeHugoMarkdown(lines))
            {
                AnsiConsole.MarkupLineInterpolated($"Ignored [red]${file.FullName}[/]");
                return;
            }
            var (frontMatter, markdown) = Hugo.ParseHugoYaml(lines, file);
            var newContent = GeneratePost(markdown, frontMatter);
            await vfsWrite.WriteAllTextAsync(file, newContent);
            AnsiConsole.MarkupLineInterpolated($"Updated [blue]${file.FullName}[/]");
        });
        Task.WaitAll(writeTasks.ToArray());

        return Task.FromResult(0);
    }

    public static async Task<int> GenerateSiteFromCurrentDirectory(Run run, VfsRead vfs, VfsWrite vfsWrite, DirectoryInfo currentDirectory)
    {
        var root = Input.FindRoot(vfs, currentDirectory);
        if (root == null) { run.WriteError("Unable to find root"); return -1; }
        var publicDir = root.GetDir("public");

        return await GenerateSite(true, run, vfs, vfsWrite, root, publicDir);
    }

    public static async Task<int> GenerateSite(bool print, Run run, VfsRead vfs, VfsWrite vfsWrite, DirectoryInfo root, DirectoryInfo publicDir)
    {
        var timeStart = DateTime.Now;

        if(print) run.Status("Parsing directory");

        var site = await Input.LoadSite(run, vfs, root);
        if (site == null)
        {
            return -1;
        }

        var templateFolder = Constants.CalculateTemplateDirectory(root);
        var partialFolder = root.GetDir("partials");

        var templates = await TemplateDictionary.Load(run, vfs, root, templateFolder, partialFolder);

        if (templates.Extensions.Count == 0)
        {
            run.WriteError($"No templates found in {templateFolder}");
            return -1;
        }

        if (print) run.Status("Writing data to disk");
        var pages = Generate.ListPagesForSite(site, publicDir, templateFolder).ToImmutableArray();
        var tags = Generate.CollectTagPages(site, publicDir, templateFolder, pages);
        var roots = Generate.CollectRoots(pages, tags);
        var numberOfPagesGenerated =
            await Generate.WriteAllPages(roots, pages, tags, run, vfsWrite, site, publicDir, templates);
        // todo(Gustav): copy static files

        if (numberOfPagesGenerated == 0)
        {
            run.WriteError("No pages were generated.");
            return -1;
        }

        var timeEnd = DateTime.Now;
        var timeTaken = timeEnd - timeStart;
        if (print) AnsiConsole.MarkupLineInterpolated($"Wrote [green]{numberOfPagesGenerated}[/] files in [blue]{timeTaken}[/]");

        return run.HasError() ? -1 : 0;
    }




    public static FileSystemWatcher WatchForChanges(Run run, DirectoryInfo dir, Func<FileInfo, Task> changed, Func<FileInfo, Task> deleted)
    {
        var watcher = new FileSystemWatcher(dir.FullName);

        watcher.NotifyFilter = NotifyFilters.Attributes
                             | NotifyFilters.CreationTime
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.FileName
                             | NotifyFilters.LastAccess
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Security
                             | NotifyFilters.Size;

        watcher.Changed += async (sender, e) =>
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
            {
                return;
            }
            await changed(new FileInfo(e.FullPath));
        };
        watcher.Created += async (sender, e) =>
        {
            await changed(new FileInfo(e.FullPath));
        };
        watcher.Deleted += async (sender, e) =>
        {
            await deleted(new FileInfo(e.FullPath));
        };
        watcher.Renamed += async (sender, e) =>
        {
            await deleted(new FileInfo(e.OldFullPath));
            await changed(new FileInfo(e.FullPath));
        };
        watcher.Error += (sender, e) =>
        {
            Exception? ex = e.GetException();
            while (true)
            {
                if (ex == null) return;
                run.WriteError($"Message: {ex.Message}");
                var sr = ex.StackTrace;
                if (sr != null)
                {
                    run.WriteError("Stacktrace:");
                    run.WriteError(sr);
                }
                ex = ex.InnerException;
            }
        };

        watcher.Filter = "*.*";
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        AnsiConsole.WriteLine($"Listening for changes in {dir.FullName}");
        return watcher;
    }
}
