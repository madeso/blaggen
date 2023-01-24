using Blaggen;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
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
        var vfs = new VfsWrite();
        return await Facade.InitSite(run, vfs);
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
        var vfs = new VfsRead();
        var vfsWrite = new VfsWrite();
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
                var vfs = new VfsRead();
                var vfsWrite = new VfsWrite();
                ret = await Facade.GenerateSite(run, vfs, vfsWrite);
            });
        return ret;
    }
}
