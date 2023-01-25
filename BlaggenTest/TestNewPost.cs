using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;



public class TestNewPost : TestBase
{
    [Fact]
    public async void CreateSimplePostInRoot()
    {
        var cwd = new DirectoryInfo(@"C:\test\");
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var contentFolder = cwd.GetDir("content");
        var postFile = contentFolder.GetFile("test.md");

        var ret = await Facade.NewPost(run, read, write, postFile);

        using(new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();
        }

        var content = write.GetContent(postFile);
        content.Should().EndWith("\n# Test");
        
        write.RemainingFiles.Should().BeEmpty();
    }

    [Fact]
    public async void CreateComplexPostInRoot()
    {
        var cwd = new DirectoryInfo(@"C:\test\");
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var contentFolder = cwd.GetDir("content");
        var postFile = contentFolder.GetFile("this-is-a_test.md");

        var ret = await Facade.NewPost(run, read, write, postFile);

        using (new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();
        }

        var content = write.GetContent(postFile);
        content.Should().EndWith("\n# This Is A Test");

        write.RemainingFiles.Should().BeEmpty();
    }

    [Fact]
    public async void CreateComplexPromotedPost()
    {
        var cwd = new DirectoryInfo(@"C:\test\");
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var contentFolder = cwd.GetDir("content");
        var postFile = contentFolder.GetDir("this-is-a_test").GetFile("_index.md");

        var ret = await Facade.NewPost(run, read, write, postFile);

        using (new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();
        }

        var content = write.GetContent(postFile);
        content.Should().EndWith("\n# This Is A Test");

        write.RemainingFiles.Should().BeEmpty();
    }

    [Fact]
    public async void CantCreateExisting()
    {
        var cwd = new DirectoryInfo(@"C:\test\");
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var contentFolder = cwd.GetDir("content");
        var postFile = contentFolder.GetFile("test.md");

        read.AddContent(postFile, "I am a existing blog");

        var ret = await Facade.NewPost(run, read, write, postFile);

        using (new AssertionScope())
        {
            ret.Should().Be(-1);
            write.RemainingFiles.Should().BeEmpty();
            run.Errors.Should().ContainSingle()
                .Which.Should().Be($"Post {postFile.FullName} already exist");
        }
    }

    [Fact]
    public async void CantCreatePostInEmptyFolder()
    {
        var cwd = new DirectoryInfo(@"C:\test\");

        var contentFolder = cwd.GetDir("content");
        var postFile = contentFolder.GetFile("test.md");

        var ret = await Facade.NewPost(run, read, write, postFile);

        using (new AssertionScope())
        {
            ret.Should().Be(-1);
            write.RemainingFiles.Should().BeEmpty();
            run.Errors.Should().ContainSingle()
                .Which.Should().Be("Unable to find root");
        }
    }
}
