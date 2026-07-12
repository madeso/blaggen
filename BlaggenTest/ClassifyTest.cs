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

        var config = await Input.LoadSiteConfig(run, read, cwd);
        config.Should().NotBeNull();
        if (config == null) return;

        var posts = await Input.LoadPosts(run, read, cwd);
        posts.Should().NotBeNull();
        if (posts == null) return;

        using (new AssertionScope())
        {
            validate(new Site(config, posts));
            run.Errors.Should().BeEmpty();
        }

        write.RemainingFiles.Should().BeEmpty();
    }

    private static ImmutableArray<T> A<T>(params T[] it)
        => [.. it];

    [Fact]
    public async Task IndexFile()
    {
        Post(cwd, "content/_index.md", "index");
        await RunTest(site =>
        {
            site.DebugString.Should().BeEquivalentTo([
                @"index(C:\test\content\_index.md) => index"
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
                @"Hello(C:\test\content\hello.md) => hello"
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
                @"Promoted post(C:\test\content\post\index.md) => post"
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
                @"  Dir(C:\test\content\post\_index.md) => index"
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
                @"  Hello(C:\test\content\post\hello.md) => hello"
            ]);
        });
    }
}
