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
                ret = await Run(run);
            });
        return ret;
    }

    // todo(Gustav): move to Facade
    private static async Task<int> Run(Run run)
    {
        var vfs = new VfsRead();
        var vfsWrite = new VfsWrite();

        var root = Input.FindRootFromCurentDirectory();
        if (root == null) { run.WriteError("Unable to find root"); return -1; }

        var timeStart = DateTime.Now;

        run.Status("Parsing directory");
        var site = await Input.LoadSite(run, vfs, root, new Markdown());
        if (site == null) { return -1; }

        var publicDir = root.GetDir("public");
        var templates = await Templates.Load(run, vfs, root);
        var unloadedPartials = root.GetDir("partials").EnumerateFiles()
            .Select(async file => new { Name = Path.GetFileNameWithoutExtension(file.Name), Content = await vfs.ReadAllTextAsync(file)})
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
