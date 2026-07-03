using System.Collections.Immutable;
using FluentAssertions.Equivalency;

namespace BlaggenTest;

using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

public class ClassifyTest : TestBase
{
    private readonly DirectoryInfo cwd = new(@"C:\test\");
    private static readonly DateTime Christmas = new(2024, 12, 24);

    private FileInfo Post(DirectoryInfo root, string path, string title)
    {
        var splits = path.Split('/');
        var dir = splits.SkipLast(1).Aggregate(root, (current, sub) => current.GetDir(sub));
        var file = dir.GetFile(splits[^1]);
        read.AddContent(file, Facade.GeneratePostWithTitle(title, new FrontMatter {Date = Christmas}));

        return file;
    }

    private async Task RunTest(Action<Site> validate)
    {
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var site = await Input.LoadEntireSite(run, read, cwd);
        site.Should().NotBeNull();
        if (site == null) return;

        using (new AssertionScope())
        {
            validate(site);
            run.Errors.Should().BeEmpty();
        }

        write.RemainingFiles.Should().BeEmpty();
    }

    private static EquivalencyAssertionOptions<ImmutableArray<Post>> IgnoreMarkdown(EquivalencyAssertionOptions<ImmutableArray<Post>> options)
        => options.Excluding(ctx => ctx.Path.EndsWith("Markdown"));
    private static EquivalencyAssertionOptions<ImmutableArray<Section>> IgnoreMarkdownSect(EquivalencyAssertionOptions<ImmutableArray<Section>> options)
        => options.Excluding(ctx => ctx.Path.EndsWith("Markdown"));
    private static Post P(FileInfo file, string title)
        => new(PostType.Post, new FrontMatter { Date = Christmas, Title = title }, file, "markdown should be ignored");
    private static ImmutableArray<T> A<T>(params T[] it)
        => [.. it];

    [Fact]
    public async Task IndexFile()
    {
        var source = Post(cwd, "content/_index.md", "index");
        await RunTest(site =>
        {
            site.Root.Dirs.Should().BeEmpty();
            site.Root.Posts.Should().BeEquivalentTo(A(P(source, "index")), IgnoreMarkdown);
        });
    }

    [Fact]
    public async Task Hello()
    {
        var source = Post(cwd, "content/hello.md", "Hello");
        await RunTest(site =>
        {
            site.Root.Dirs.Should().BeEmpty();
            site.Root.Posts.Should().BeEquivalentTo(A(P(source, "Hello")), IgnoreMarkdown);
        });
    }

    [Fact]
    public async Task Promoted()
    {
        var source = Post(cwd, "content/post/index.md", "Promoted post");
        await RunTest(site =>
        {
            var map = site.DebugString();
            map.Should().Be("");
            var sect = new Section("", P(source, "Promoted post"), [], []);
            site.Root.Dirs.Should().BeEquivalentTo(A(sect));
            // site.Root.Posts.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task DirInSub()
    {
        var source = Post(cwd, "content/post/_index.md", "Dir");
        await RunTest(site =>
        {
            site.Root.Dirs.Should().BeEmpty();
            site.Root.Posts.Should().BeEquivalentTo(A(P(source, "Dir")), IgnoreMarkdown);
        });
    }

    [Fact]
    public async Task HelloInSub()
    {
        var source = Post(cwd, "content/post/hello.md", "Hello");
        await RunTest(site =>
        {
            site.Root.Dirs.Should().BeEmpty();
            site.Root.Posts.Should().BeEquivalentTo(A(P(source, "Hello")), IgnoreMarkdown);
        });
    }
}
