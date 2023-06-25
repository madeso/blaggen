using Blaggen;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;


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
                ret = await Facade.GenerateSite(run, vfs, vfsWrite, VfsReadFile.GetCurrentDirectory());
            });
        return ret;
    }
}


[Description("Genrate or publish the site")]
internal sealed class ServerCommand : AsyncCommand<ServerCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
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

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var ret = 0;
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var run = new RunConsoleWithContext(ctx);
                var vfs = new VfsReadFile();
                var vfsWrite = new VfsWriteFile();
                var wait = Task.Run(() => MonitorKeypress(ConsoleKey.Escape));
                var completed = await Task.WhenAny(wait);
                ret = await completed;
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

    public async Task<string> ReadAllTextAsync(FileInfo fullName)
    {
        return await File.ReadAllTextAsync(fullName.FullName);
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