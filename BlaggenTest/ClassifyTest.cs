using System.Collections.Immutable;

namespace BlaggenTest;

using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

public class ClassifyTest : TestBase
{
    DirectoryInfo cwd = new DirectoryInfo(@"C:\test\");

    private FileInfo Post(DirectoryInfo root, string path)
    {
        var splits = path.Split('/');
        var dir = splits.SkipLast(1).Aggregate(root, (current, sub) => current.GetDir(sub));
        var file = dir.GetFile(splits[^1]);
        read.AddContent(file, Facade.GeneratePostWithTitle(path, new FrontMatter()));

        return file;
    }

    [Fact]
    public async Task JustRoot()
    {
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var source = Post(cwd, "content/_index.md");

        var site = await Input.LoadEntireSite(run, read, cwd);
        site.Should().NotBeNull();
        if (site == null) return;

        using (new AssertionScope())
        {
            site.Root.Dirs.Should().BeEmpty();
            var pp = new Post(PostType.Post, new FrontMatter(), source, "");
            List<Post> pl = [pp];
            site.Root.Posts.Should().BeSameAs(pl);
            run.Errors.Should().BeEmpty();
        }

        write.RemainingFiles.Should().BeEmpty();
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
