using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using Spectre.Console;

namespace Blaggen;


public interface Run
{
    public void WriteError(FormattableString message);
    public void WriteInfo(FormattableString message);

    public bool HasError();
    public void Status(string message);
}



public static class Facade
{
    public static async Task<int> InitSite(Run run, VfsRead read, VfsWrite vfs, DirectoryInfo currentDirectory)
    {
        var existing_site = Input.FindRoot(read, currentDirectory);
        if (existing_site != null)
        {
            run.WriteError($"Site already exists at [red]{existing_site.FullName}[/]");
            return -1;
        }

        var site = new SiteConfig { Name = "My new blog" };
        var site_path = currentDirectory.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION);
        await vfs.WriteAllTextAsync(site_path, JsonUtil.Write(site));

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
        // todo(Gustav): should this function be exposed, tests use it but shouldn't they use a much simpler function???
        var frontmatter = JsonUtil.Write(fm);
        var content = $"{Input.SOURCE_START}\n{frontmatter}\n{Input.SOURCE_END}\n{Input.FRONTMATTER_SEP}\n{markdown}";
        return content;
    }

    public static async Task<int> NewPost(Run run, VfsRead vfs, VfsWrite vfsWrite, FileInfo path)
    {
        if (vfs.Exists(path)) { run.WriteError($"Post [red]{path}[/] already exist"); return -1; }

        var path_dir = path.Directory;
        if (path_dir == null) { run.WriteError($"Post [red]{path}[/] isn't rooted"); return -1; }

        var root = Input.FindRoot(vfs, path_dir);
        if (root == null) { run.WriteError($"Unable to find root"); return -1; }

        var site = await Input.LoadSiteConfig(run, vfs, root);
        if (site == null) { return -1; }

        // todo(Gustav): create _index.md for each directory depending on setting
        var content_folder = Constants.GetContentDirectory(root);
        var relative = Path.GetRelativePath(content_folder.FullName, path.FullName);
        if (relative.Contains("..")) { run.WriteError($"Post [red]{path_dir}[/] must be a subpath of [blue]{content_folder}[/]"); return -1; }

        var post_name_base = Path.GetFileNameWithoutExtension(path.Name);
        if (post_name_base == Constants.SECTION_INDEX_NAME_NO_EXT)
        {
            post_name_base = path.Directory!.Name;
        }
        var title = site.CultureInfo.TextInfo.ToTitleCase(post_name_base.Replace('-', ' ').Replace('_', ' '));
        var content = GeneratePostWithTitle(title, new FrontMatter());

        // todo(Gustav): use a template in the future...

        path.Directory!.Create();
        await vfsWrite.WriteAllTextAsync(path, content);

        Debug.Assert(run.HasError() == false);
        run.WriteInfo($"Wrote [blue]${path.FullName}[/]");
        return 0;
    }


    public static Task<int> MigrateFromHugo(Run run, VfsRead vfsRead, VfsWrite vfsWrite, DirectoryInfo currentDirectory)
    {
        var root = Input.FindRoot(vfsRead, currentDirectory);
        if (root == null) { run.WriteError($"Unable to find root"); return Task.FromResult(-1); }

        run.Status("Finding files");
        var content_folder = Constants.GetContentDirectory(root);
        var files = vfsRead.GetFilesRec(content_folder)
            .Where(file => file.Extension == ".md")
            .ToImmutableArray()
            ;

        run.Status("Migrating hugo markdowns");
        var write_tasks = files.Select(async file =>
        {
            var lines = (await vfsRead.ReadAllTextAsync(file)).Split('\n');
            if (false == Hugo.LooksLikeHugoMarkdown(lines))
            {
                run.WriteInfo($"Ignored [red]${file.FullName}[/]");
                return;
            }
            var (front_matter, markdown) = Hugo.ParseHugoYaml(lines, file);
            var new_content = GeneratePost(markdown, front_matter);
            await vfsWrite.WriteAllTextAsync(file, new_content);
            run.WriteInfo($"Updated [blue]${file.FullName}[/]");
        });
        Task.WaitAll(write_tasks.ToArray());

        return Task.FromResult(0);
    }

    public static async Task<int> GenerateSiteFromCurrentDirectory(Run run, VfsRead vfs, VfsWrite vfs_write, DirectoryInfo current_directory)
    {
        var root = Input.FindRoot(vfs, current_directory);
        if (root == null) { run.WriteError($"Unable to find root"); return -1; }
        var public_dir = root.GetDir("public");

        return await GenerateSite(run, vfs, vfs_write, root, public_dir);
    }

    public static async Task<int> GenerateSite(Run run, VfsRead vfs, VfsWrite vfs_write, DirectoryInfo root, DirectoryInfo public_dir)
    {
        var time_start = DateTime.Now;

        run.Status("Parsing directory");

        var site = await Input.LoadEntireSite(run, vfs, root);
        if (site == null)
        {
            return -1;
        }

        var template_folder = Constants.CalculateTemplateDirectory(site.Config, root);
        var partial_folder = root.GetDir("partials");

        var templates = await TemplateDictionary.Load(run, vfs, root, template_folder, partial_folder);
        if (templates == null)
        {
            run.WriteError($"No templates found in [red]{template_folder}[/]");
            return -1;
        }

        // todo(Gustav): generate page
        int number_of_pages_generated = -42;

        var time_end = DateTime.Now;
        var time_taken = time_end - time_start;
        run.WriteInfo($"Wrote [green]{number_of_pages_generated}[/] files in [blue]{time_taken}[/]");

        return run.HasError() ? -1 : 0;
    }




    private static FileSystemWatcher WatchForChanges(Run run, DirectoryInfo dir, Func<FileInfo, Task> changed, Func<FileInfo, Task> deleted)
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
                run.WriteError($"Message: [red]{ex.Message}[/]");
                var sr = ex.StackTrace;
                if (sr != null)
                {
                    run.WriteError($"Stacktrace:");
                    run.WriteError($"{sr}");
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

    // todo(Gustav): replace with union
    private record FileEvent
    {
        public record FileCreated(FileInfo File) : FileEvent;
        public record FileRemoved(FileInfo File) : FileEvent;

        private FileEvent() { }
    }

    public static async Task<int> StartServerAndMonitorForChanges(int port, Run run,
        VfsCachedFileRead vfs_cache, ServerVfs server_vfs, DirectoryInfo root, ConsoleKey abort_key)
    {
        var public_dir = root.GetDir("public");
        await GenerateSite(run, vfs_cache, server_vfs, root, public_dir);

        var watch_for_changes = true;

        var cts = new CancellationTokenSource();
        var tasks = new List<Task<int>>
        {
            Task.Run(() => MonitorKeypress(abort_key), cts.Token),

            LocalServer.Run(run, server_vfs, port, cts.Token)
        };

        using var watcher = watch_for_changes
                ? RegenerateSiteIfChanged(run, vfs_cache, server_vfs, root, public_dir, cts, tasks)
                // dummy file watcher
                : new FileSystemWatcher()
            ;

        var completed_task = await Task.WhenAny(tasks);
        await cts.CancelAsync();

        return await completed_task;

        static int MonitorKeypress(ConsoleKey abort_key)
        {
            AnsiConsole.WriteLine($"Press {abort_key} to exit...");
            ConsoleKeyInfo cki;
            do
            {
                cki = Console.ReadKey(true);
            } while (cki.Key != abort_key);

            return 0;
        }

        static async Task<int> GenerateSiteOnChange(ChannelReader<FileEvent> events_reader, CancellationToken cts_token,
        VfsCachedFileRead vfs_cache,
        Run run, VfsWrite server_vfs, DirectoryInfo root, DirectoryInfo public_dir)
        {
            await foreach (var ev in events_reader.ReadAsyncOrCancel(cts_token))
            {
                switch (ev)
                {
                    case FileEvent.FileCreated file_created:
                        var regenerate = await vfs_cache.AddFileToCache(file_created.File);
                        if (regenerate == false)
                        {
                            continue;
                        }

                        break;
                    case FileEvent.FileRemoved file_removed:
                        vfs_cache.Remove(file_removed.File);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(ev));
                }

                // todo(Gustav): print only errors
                await GenerateSite(run, vfs_cache, server_vfs, root, public_dir);
            }

            return 0;
        }

        static FileSystemWatcher RegenerateSiteIfChanged(Run run, VfsCachedFileRead vfs_cached_file_read, ServerVfs server_vfs1,
            DirectoryInfo root, DirectoryInfo public_dir, CancellationTokenSource cts, List<Task<int>> tasks)
        {

            var events = Channel.CreateUnbounded<FileEvent>(new UnboundedChannelOptions
            { SingleReader = false, SingleWriter = true });

            tasks.Add(Task.Run(async () =>
                    await GenerateSiteOnChange(events.Reader, cts.Token, vfs_cached_file_read, run, server_vfs1, root,
                        public_dir)
                , cts.Token));

            return WatchForChanges(run, root,
                async (file) =>
                {
                    AnsiConsole.WriteLine($"Changed {file.FullName}");
                    await events.Writer.WriteAsync(new FileEvent.FileCreated(file), cts.Token);
                },
                async (file) =>
                {
                    AnsiConsole.WriteLine($"Deleted {file.FullName}");
                    await events.Writer.WriteAsync(new FileEvent.FileRemoved(file), cts.Token);
                });
        }
    }

    public static async Task<int> ListTaxonomies(Run run, VfsReadFile vfs, DirectoryInfo root)
    {
        run.Status("Parsing directory");
        var site = await Input.LoadEntireSite(run, vfs, root);
        if (site == null)
        {
            return -1;
        }

        run.Status("Collecting");
        // todo(Gustav): also use ColCounter here?
        var keys = AllPosts(site.Root).SelectMany(p => p.Front.TaxonomyData.Keys).ToHashSet();
        AnsiConsole.WriteLine($"{keys.Count} unique group(s)");
        foreach (var key in keys)
        {
            AnsiConsole.WriteLine($" - {key}");
        }

        return 0;
    }

    public static async Task<int> ListTermsForTaxonomy(Run run, VfsReadFile vfs, DirectoryInfo root, string taxonomy)
    {
        run.Status("Parsing directory");
        var site = await Input.LoadEntireSite(run, vfs, root);
        if (site == null)
        {
            return -1;
        }

        run.Status("Collecting");
        var selected = AllPosts(site.Root)
            .Select(p => p.Front.TaxonomyData.TryGetValue(taxonomy, out var props) ? props : null)
            .Where(p => p != null)
            .ToImmutableArray();

        if (selected.Length == 0)
        {
            run.WriteError($"[red]{taxonomy}[/red] is not a valid taxonomy");
            return -1;
        }

        var keys = selected
            .SelectMany(p => p!)
            .ToColCounter()
            ;
        run.WriteInfo($"[blue]{keys.Keys.Count()}[/] unique terms(s) for [red]{taxonomy}[red]");
        foreach (var (key, count) in keys.MostCommon())
        {
            run.WriteInfo($" - [red]{key}[/red]: [blue]{count}[/blue]");
        }

        return 0;
    }

    private static IEnumerable<Post> AllPosts(Section root)
    {
        foreach (var p in root.Posts)
        {
            yield return p;
        }

        foreach (var p in root.Dirs.SelectMany(AllPosts))
        {
            yield return p;
        }
    }

    public static async Task<int> AddAdditionalTermToTaxonomy(Run run, VfsRead vfs, VfsWrite vfs_write, DirectoryInfo root, string taxonomy, string term, string additional_term)
    {
        var posts = await ExtractPostsWithTerm(run, vfs, root, taxonomy, term);
        if (posts.Length == 0)
        {
            return -1;
        }

        foreach (var p in posts)
        {
            var was_added = p.Front.TaxonomyData[taxonomy].Add(additional_term);
            if (was_added == false)
            {
                AnsiConsole.WriteLine($"warning: {additional_term} already existing for {p.SourceFile.DisplayNameForFile()}");
                continue;
            }

            var contents = Input.PostToFileData(p);
            await vfs_write.WriteAllTextAsync(p.SourceFile, contents);
        }

        return 0;
    }


    public static async Task<int> RemoveTagFromGroup(Run run, VfsRead vfs, VfsWrite vfs_write, DirectoryInfo root, string taxonomy, string term, string term_to_remove)
    {
        var posts = await ExtractPostsWithTerm(run, vfs, root, taxonomy, term);
        if (posts.Length == 0)
        {
            return -1;
        }

        foreach (var p in posts)
        {
            var was_removed = p.Front.TaxonomyData[taxonomy].Remove(term_to_remove);
            if (was_removed == false)
            {
                AnsiConsole.WriteLine($"warning: {term_to_remove} didn't exist for {p.SourceFile.DisplayNameForFile()}");
                continue;
            }

            var contents = Input.PostToFileData(p);
            await vfs_write.WriteAllTextAsync(p.SourceFile, contents);
        }

        return 0;
    }

    public static DirectoryInfo GetCurrentDirectory() => VfsReadFile.GetCurrentDirectory();
    public static DirectoryInfo? FindRoot(VfsRead vfs, DirectoryInfo start) => Input.FindRoot(vfs, start);

    private record PostWithTags(HashSet<string> Props, Post Post);
    private record PostWithOptionalTags(HashSet<string>? Props, Post Post);

    private static async Task<ImmutableArray<Post>> ExtractPostsWithTerm(Run run, VfsRead vfs, DirectoryInfo root, string taxonomy, string term)
    {
        run.Status("Parsing directory");
        var site = await Input.LoadEntireSite(run, vfs, root);
        if (site == null)
        {
            return ImmutableArray<Post>.Empty;
        }

        run.Status("Collecting");
        var selected = AllPosts(site.Root)
                .Select(p => p.Front.TaxonomyData.TryGetValue(taxonomy, out var props)
                    ? new PostWithOptionalTags(props, p)
                    : new PostWithOptionalTags(null, p))
                .Where(p => p.Props != null)
                .Select(p => new PostWithTags(p.Props!, p.Post))
                .ToImmutableArray()
            ;

        if (selected.Length == 0)
        {
            run.WriteError($"{taxonomy} is not a valid taxonomy");
            return ImmutableArray<Post>.Empty;
        }

        var posts = selected
                .Where(p => p.Props.Contains(term))
                .Select(p => p.Post)
                .ToImmutableArray()
            ;

        if (posts.Length == 0)
        {
            run.WriteError($"{term} is not a valid {taxonomy}");
            return ImmutableArray<Post>.Empty;
        }

        return posts;
    }
}
