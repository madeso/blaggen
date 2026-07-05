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
        Post(cwd, "content/_index.md", "index");
        await RunTest(site =>
        {
            site.DebugString.Should().BeEquivalentTo([
                @"index(C:\test\content\_index.md)"
            ]);
        });
    }

    [Fact]
    public async Task Hello()
    {
        Post(cwd, "content/hello.md", "Hello");
        await RunTest(site =>
        {
            site.DebugString.Should().BeEquivalentTo([
                @"Hello(C:\test\content\hello.md)"
            ]);
        });
    }

    [Fact]
    public async Task Promoted()
    {
        Post(cwd, "content/post/index.md", "Promoted post");
        await RunTest(site =>
        {
            site.DebugString.Should().BeEquivalentTo([
                @"Promoted post(C:\test\content\post\index.md)"
            ]);
        });
    }

    [Fact]
    public async Task DirInSub()
    {
        Post(cwd, "content/post/_index.md", "Dir");
        await RunTest(site =>
        {
            site.DebugString.Should().BeEquivalentTo([
                "post/",
                @"  Dir(C:\test\content\post\_index.md)"
            ]);
        });
    }

    [Fact]
    public async Task HelloInSub()
    {
        Post(cwd, "content/post/hello.md", "Hello");
        await RunTest(site =>
        {
            site.DebugString.Should().BeEquivalentTo([
                "post/",
                @"  Hello(C:\test\content\post\hello.md)"
            ]);
        });
    }
}
