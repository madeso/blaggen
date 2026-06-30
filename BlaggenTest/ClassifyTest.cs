using System.Collections.Immutable;
using FluentAssertions.Equivalency;

namespace BlaggenTest;

using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

public class ClassifyTest : TestBase
{
    DirectoryInfo cwd = new DirectoryInfo(@"C:\test\");
    private static readonly DateTime Christmas = new(2024, 12, 24);

    private FileInfo Post(DirectoryInfo root, string path, string? title = null)
    {
        var splits = path.Split('/');
        var dir = splits.SkipLast(1).Aggregate(root, (current, sub) => current.GetDir(sub));
        var file = dir.GetFile(splits[^1]);
        read.AddContent(file, Facade.GeneratePostWithTitle(title ?? path, new FrontMatter {Date = Christmas}));

        return file;
    }

    private static ImmutableArray<T> A<T>(params T[] it) => [..it];

    [Fact]
    public async Task JustRoot()
    {
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var source = Post(cwd, "content/_index.md", "index");

        var site = await Input.LoadEntireSite(run, read, cwd);
        site.Should().NotBeNull();
        if (site == null) return;

        using (new AssertionScope())
        {
            site.Root.Dirs.Should().BeEmpty();
            site.Root.Posts.Should().BeEquivalentTo(A(P(source, "index")), IgnoreMarkdown);
            run.Errors.Should().BeEmpty();
        }

        write.RemainingFiles.Should().BeEmpty();
    }

    private static EquivalencyAssertionOptions<ImmutableArray<Post>> IgnoreMarkdown(EquivalencyAssertionOptions<ImmutableArray<Post>> options)
        => options.Excluding(ctx => ctx.Path.EndsWith("Markdown"));

    private static Post P(FileInfo file, string title)
    {
        return new Post(PostType.Post, new FrontMatter {Date = Christmas, Title = title}, file, "markdown should be ignored");
    }

    [Fact]
    public async Task SomeFiles()
    {
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        Post(cwd, "content/hello.md");
        Post(cwd, "content/post/index.md");
        Post(cwd, "content/post/_index.md");
        Post(cwd, "content/post/hello.md");

        var site = await Input.LoadEntireSite(run, read, cwd);
        site.Should().NotBeNull();
        if (site == null) return;

        using (new AssertionScope())
        {
            site.Root.Dirs.Should().BeEmpty();
            run.Errors.Should().BeEmpty();
        }

        write.RemainingFiles.Should().BeEmpty();
    }
}
