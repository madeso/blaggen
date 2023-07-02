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
            config.AddCommand<GenerateCommand>("generate").WithAlias("publish").WithAlias("build");
            config.AddCommand<ServerCommand>("server").WithAlias("serve").WithAlias("dev");

            config.AddBranch("tags", tags =>
            {
                tags.SetDescription("group related commands");
                tags.AddCommand<ListTagsCommand>("list").WithAlias("ls");
                tags.AddCommand<AddTagCommand>("add");
                tags.AddCommand<RemoveTagCommand>("remove");
            })
                .WithAlias("tag")
                .WithAlias("groups")
                .WithAlias("group")
                ;
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

[Description("Create a new page")]
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

        // todo(Gustav): accept dir and post title and generate file name from title
        // todo(Gustav): generate content from template
        // todo(Gustav): template can generate title/filename from default patterns in template (weekly, daily)? or is this a different command
        return await Facade.NewPost(run, vfs, vfsWrite, path);
    }
}

[Description("Generate/publish the site")]
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


[Description("Start a local server and watch for changes")]
internal sealed class ServerCommand : AsyncCommand<ServerCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("The port to use")]
        [CommandOption("-p|--port")]
        public int? Port { get; set; }
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

                ret = await Facade.StartServerAndMonitorForChanges(args.Port ?? 8080, run, vfsCache, serverVfs, root, publicDir, ConsoleKey.Escape);
            });
        return ret;
    }
}


[Description("List tags")]
internal sealed class ListTagsCommand : AsyncCommand<ListTagsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("The tag/group name to list for")]
        [CommandArgument(1, "[name]")]
        public string Name { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var ret = -1;
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var run = new RunConsoleWithContext(ctx);
                var vfsRead = new VfsReadFile();

                var root = Input.FindRoot(vfsRead, VfsReadFile.GetCurrentDirectory());
                if (root == null)
                {
                    run.WriteError("Unable to find root");
                    ret = -1;
                    return;
                }

                ret = string.IsNullOrWhiteSpace(args.Name)
                    ? await Facade.ListGroups(run, vfsRead, root)
                    : await Facade.LisGroupsWithTag(run, vfsRead, root, args.Name)
                    ;
            });
        return ret;
    }
}


[Description("Add tags to post")]
internal sealed class AddTagCommand : AsyncCommand<AddTagCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("The group name to add to")]
        [CommandArgument(1, "<group>")]
        public string Group { get; init; } = string.Empty;

        [Description("Add to posts that has this")]
        [CommandArgument(1, "<where>")]
        public string Where { get; init; } = string.Empty;

        [Description("The tag to add")]
        [CommandArgument(1, "<what>")]
        public string What { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var ret = -1;
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var run = new RunConsoleWithContext(ctx);
                var vfsRead = new VfsReadFile();

                var root = Input.FindRoot(vfsRead, VfsReadFile.GetCurrentDirectory());
                if (root == null)
                {
                    run.WriteError("Unable to find root");
                    ret = -1;
                    return;
                }

                var vfsWrite = new VfsWriteFile();

                ret = await Facade.AddTagsToGroup(run, vfsRead, vfsWrite, root, args.Group, args.Where, args.What);
            });
        return ret;
    }
}


[Description("Remove tags from post")]
internal sealed class RemoveTagCommand : AsyncCommand<RemoveTagCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("The group name to remove from")]
        [CommandArgument(1, "<group>")]
        public string Group { get; init; } = string.Empty;

        [Description("Remove from posts that has this")]
        [CommandArgument(1, "<where>")]
        public string Where { get; init; } = string.Empty;

        [Description("The tag to remove (if different from where)")]
        [CommandArgument(1, "[what]")]
        public string What { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings args)
    {
        var ret = -1;
        await AnsiConsole.Status()
            .StartAsync("Working...", async ctx =>
            {
                var run = new RunConsoleWithContext(ctx);
                var vfsRead = new VfsReadFile();

                var root = Input.FindRoot(vfsRead, VfsReadFile.GetCurrentDirectory());
                if (root == null)
                {
                    run.WriteError("Unable to find root");
                    ret = -1;
                    return;
                }

                var vfsWrite = new VfsWriteFile();

                ret = await Facade.RemoveTagFromGroup(run, vfsRead, vfsWrite, root, args.Group, args.Where,
                    string.IsNullOrWhiteSpace(args.What)==false
                        ? args.What
                        : args.Where);
            });
        return ret;
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