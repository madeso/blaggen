using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;


public class TagCommandTest : TestBase
{
    DirectoryInfo cwd;
    DirectoryInfo content;
    DirectoryInfo publics;

    public TagCommandTest()
    {
        cwd = new(@"C:\test\");
        content = cwd.GetDir("content");
        publics = cwd.GetDir("public");
    }


    [Fact]
    public async void ErrorRun()
    {
        read.AddContent(Constants.CalculateTemplateDirectory(cwd).GetFile("_post.mustache.html"), "{{content_text}}");
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        read.AddContent(content.GetFile("post.md"), "{}\n***\nThis is a post");

        var ret = await Facade.AddTagsToGroup(run, read, write, cwd, "tags", "dog", "cat");
        using (new AssertionScope())
        {
            ret.Should().Be(-1);
            run.Errors.Should().BeEquivalentTo("tags is not a valid group");
        }

        write.RemainingFiles.Should().BeEmpty();
    }


    [Fact]
    public async void SimpleRun()
    {
        read.AddContent(Constants.CalculateTemplateDirectory(cwd).GetFile("_post.mustache.html"), "{{content_text}}");
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        read.AddContent(content.GetFile("post.md"), "{\"date\": \"2020-01-01T00:00:00\", \"tags\": {\"tags\": [\"dog\"]}}\n***\nThis is a post");
        read.AddContent(content.GetFile("untouched.md"), "{}\n***\nThis post is untouched");

        var ret = await Facade.AddTagsToGroup(run, read, write, cwd, "tags", "dog", "cat");
        using (new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();

            write.GetLines(content.GetFile("post.md"))
                .Should().BeEquivalentTo(
                    "```json",
                    "{",
                    "\"title\": \"\",",
                    "\"description\": \"This is a post\\n\",",
                    "\"date\": \"2020-01-01T00:00:00\",",
                    "\"tags\": {",
                    "\"tags\": [",
                    "\"dog\",",
                    "\"cat\"",
                    "]",
                    "}",
                    "}",
                    "```",
                    "***",
                    "This is a post");
        }

        write.RemainingFiles.Should().BeEmpty();
    }
}
