using Blaggen;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;


// ----------------------------------------------------------------------------------------------------------------------------
// commandline handling and main runners

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<InitSiteCommand>("init");
            config.AddCommand<NewPostCommand>("new");
            config.AddCommand<GenerateCommand>("generate");
            config.AddCommand<ServerCommand>("server");
        });
        return await app.RunAsync(args);
    }
}

[Description("Generate a new site in the curent directory")]
internal sealed class InitSiteCommand : AsyncCommand<InitSiteCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var run = new RunConsole();
        var vfsRead = new VfsReadFile();
        var vfsWrite = new VfsWriteFile();
        return await Facade.InitSite(run, vfsRead, vfsWrite, VfsReadFile.GetCurrentDirectory());
    }
}

[Description("Generate a new page")]
internal sealed class NewPostCommand : AsyncCommand<NewPostCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Relative path to post")]
        [CommandArgument(1, "<path>")]
        public string Path { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var run = new RunConsole();
        var vfs = new VfsReadFile();
        var vfsWrite = new VfsWriteFile();
        var path = new FileInfo(args.Path);
        return await Facade.NewPost(run, vfs, vfsWrite, path);
    }
}

[Description("Genrate or publish the site")]
internal sealed class GenerateCommand : AsyncCommand<GenerateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var ret = 0;
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var run = new RunConsoleWithContext(ctx);
                var vfs = new VfsReadFile();
                var vfsWrite = new VfsWriteFile();
                ret = await Facade.GenerateSiteFromCurrentDirectory(run, vfs, vfsWrite, VfsReadFile.GetCurrentDirectory());
            });
        return ret;
    }
}


[Description("Genrate or publish the site")]
internal sealed class ServerCommand : AsyncCommand<ServerCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("The port to use")]
        [CommandOption("-p|--port")]
        public int? Port { get; set; }
    }

    public static int MonitorKeypress(ConsoleKey abortKey)
    {
        AnsiConsole.WriteLine($"Press {abortKey} to exit...");
        ConsoleKeyInfo cki;
        do
        {
            cki = Console.ReadKey(true);
        } while (cki.Key != abortKey);

        return 0;
    }

    private record FileEvent
    {
        public record FileCreated(FileInfo File) : FileEvent;
        public record FileRemoved(FileInfo File) : FileEvent;

        private FileEvent() {}
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var ret = -1;
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var run = new RunConsoleWithContext(ctx);
                var vfsCache = new VfsCachedFileRead();

                var root = Input.FindRoot(vfsCache, VfsReadFile.GetCurrentDirectory());
                if (root == null)
                {
                    run.WriteError("Unable to find root");
                    ret = -1;
                    return;
                }

                var publicDir = root.GetDir("public");
                var serverVfs = new ServerVfs(publicDir);

                await Facade.GenerateSite(false, run, vfsCache, serverVfs, root, publicDir);

                var events = Channel.CreateUnbounded<FileEvent>(new UnboundedChannelOptions() { SingleReader = false, SingleWriter = true });

                using var watcher = Facade.WatchForChanges(run, root,
                    async (file) =>
                    {
                        AnsiConsole.WriteLine($"Changed {file.FullName}");
                        await events.Writer.WriteAsync(new FileEvent.FileCreated(file));
                    },
                    async (file) =>
                    {
                        AnsiConsole.WriteLine($"Deleted {file.FullName}");
                        await events.Writer.WriteAsync(new FileEvent.FileRemoved(file));
                    });

                var cts = new CancellationTokenSource();

                var generateTask = Task.Run(async () =>
                {
                    await foreach(var ev in events.Reader.ReadAsyncOrCancel(cts.Token))
                    {
                        switch (ev)
                        {
                            case FileEvent.FileCreated fileCreated:
                                var regenerate = await vfsCache.AddFileToCache(fileCreated.File);
                                if (regenerate == false)
                                {
                                    continue;
                                }
                                break;
                            case FileEvent.FileRemoved fileRemoved:
                                vfsCache.Remove(fileRemoved.File);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(ev));
                        }

                        // todo(Gustav): print only errors
                        await Facade.GenerateSite(false, run, vfsCache, serverVfs, root, publicDir);
                    }

                    return 0;
                }, cts.Token);
                var pressKeyToExitTask = Task.Run(() => MonitorKeypress(ConsoleKey.Escape), cts.Token);
                var serverTask = LocalServer.Run(run, serverVfs, args.Port ?? 8080, cts.Token);
                
                var completedTask = await Task.WhenAny(pressKeyToExitTask, serverTask);
                cts.Cancel();
                ret = await completedTask;
            });
        return ret;
    }
}


public class VfsReadFile : VfsRead
{
    public bool Exists(FileInfo fileInfo)
    {
        return fileInfo.Exists;
    }

    protected async Task<byte[]> ReadBytes(FileInfo fullName)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(fullName.FullName);
            return bytes;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return new byte[] { };
        }
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

    public static DirectoryInfo GetCurrentDirectory()
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

    private bool AddToCache(FileInfo file, byte[] newBytes)
    {
        while(true)
        {
            byte[] oldBytes;

            while (true)
            {
                // can add, then return
                if(cache.TryAdd(file.FullName, newBytes)) { return true; }

                // there is a value blocking, try to get that
                if (!cache.TryGetValue(file.FullName, out var gotBytes)) continue;

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

            // update cache failed? do everything again!
            if (!cache.TryUpdate(file.FullName, newBytes, oldBytes)) continue;

            return true;
        }

        string Checksum(IEnumerable<byte>? bytes)
        {
            // todo(Gustav): add better checksum
            var result = bytes?.Sum(x => x) ?? 0;
            result &= 0xff;
            return result.ToString("X2");
        }
    }

    // return true if the content was updated
    public async Task<bool> AddFileToCache(FileInfo file)
    {
        var bytes = await ReadBytes(file);
        return AddToCache(file, bytes);
    }

    public void Remove(FileInfo file)
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


public class RunConsole : Run
{
    private int errorCount = 0;

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]ERROR[/]: {message}");
        errorCount += 1;
    }

    public bool HasError()
    {
        return errorCount > 0;
    }

    public void Status(string message)
    {
    }
}

public class RunConsoleWithContext : Run
{
    private int errorCount = 0;
    private readonly StatusContext context;

    public RunConsoleWithContext(StatusContext ctx)
    {
        this.context = ctx;
    }

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]ERROR[/]: {message}");
        errorCount += 1;
    }

    public bool HasError()
    {
        return errorCount > 0;
    }

    public void Status(string message)
    {
        context.Status(message);
    }
}