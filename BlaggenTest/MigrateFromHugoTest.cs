using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;


public class MigrateFromHugoTest : TestBase
{
    DirectoryInfo cwd;
    DirectoryInfo content;

    public MigrateFromHugoTest()
    {
        cwd = new(@"C:\test\");
        content = cwd.GetDir("content");
    }

    [Fact]
    public async void SimpleRun()
    {
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        read.AddContent(content.GetDir("posts").GetFile("hello.md"), """
            +++
            date = 2015-12-30T20:23:30Z
            draft = false
            title = "Hello"
            +++
            Hello?
            """);
        read.AddContent(content.GetDir("posts").GetFile("world.md"), """
            +++
            date = 2015-04-21T10:26:14Z
            draft = false
            title = "World"
            slug = "world"
            +++
            World!
            """);
        read.AddContent(content.GetFile("cool.md"), """
            +++
            date = 2022-09-27T05:26:12Z
            draft = false
            title = "Cool"
            +++
            Cool!
            """);

        var ret = await Facade.MigrateFromHugo(run, read, write, cwd);
        using (new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();

            var hello = ReadAndParseFile(content.GetDir("posts").GetFile("hello.md"));
            hello.frontmatter.Should().NotBeNull();
            hello.frontmatter?.Title.Should().Be("Hello");
            hello.frontmatter?.Date.Should().Be(new DateTime(2015, 12, 30, 20, 23, 30));
            hello.markdownContent.Trim().Should().Be("Hello?");

            var world = ReadAndParseFile(content.GetDir("posts").GetFile("world.md"));
            world.frontmatter.Should().NotBeNull();
            world.frontmatter?.Title.Should().Be("World");
            world.frontmatter?.Date.Should().Be(new DateTime(2015, 04, 21, 10, 26, 14));
            world.markdownContent.Trim().Should().Be("World!");

            var cool = ReadAndParseFile(content.GetFile("cool.md"));
            cool.frontmatter.Should().NotBeNull();
            cool.frontmatter?.Title.Should().Be("Cool");
            cool.frontmatter?.Date.Should().Be(new DateTime(2022, 09, 27, 05, 26, 12));
            cool.markdownContent.Trim().Should().Be("Cool!");

            write.RemainingFiles.Should().BeEmpty();
        }

        (FrontMatter? frontmatter, string markdownContent) ReadAndParseFile(FileInfo hf)
        {
            return Input.ParsePostToTuple(run, write.GetContent(hf).Split('\n'), hf);
        }
    }


}