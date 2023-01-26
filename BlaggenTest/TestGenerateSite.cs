using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;


public class TestGenerateSite : TestBase
{
    DirectoryInfo cwd;
    DirectoryInfo content;
    DirectoryInfo publics;

    public TestGenerateSite()
    {
        cwd = new(@"C:\test\");
        content = cwd.GetDir("content");
        publics = cwd.GetDir("public");
    }

    [Fact]
    public async void ErrorWhithNoTemplates()
    {
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var ret = await Facade.GenerateSite(run, read, write, cwd);
        using (new AssertionScope())
        {
            ret.Should().Be(-1);
            run.Errors.Should().ContainSingle().Which
                .Should().Be($"No templates found in {Templates.CalculateTemplateDirectory(cwd)}");
        }

        write.RemainingFiles.Should().BeEmpty();
    }

    [Fact]
    public async void ErrorWhithNoPosts()
    {
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");
        read.AddContent(Templates.CalculateTemplateDirectory(cwd).GetFile("_post.mustache.html"), "{{content_text}}");

        var ret = await Facade.GenerateSite(run, read, write, cwd);
        using (new AssertionScope())
        {
            ret.Should().Be(-1);
            run.Errors.Should().ContainSingle().Which
                .Should().Be("No pages were generated.");
        }

        write.RemainingFiles.Should().BeEmpty();
    }

    [Fact]
    public async void SimpleRun()
    {
        read.AddContent(Templates.CalculateTemplateDirectory(cwd).GetFile("_post.mustache.html"), "{{content_text}}");
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        read.AddContent(content.GetFile("post.md"), "{}\n***\nThis is a post");

        var ret = await Facade.GenerateSite(run, read, write, cwd);
        using (new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();

            write.GetContent(publics.GetDir("post").GetFile("index.html"))
                .Should().Be("This is a post\n");
        }

        write.RemainingFiles.Should().BeEmpty();
    }

    [Fact]
    public async void RootsShouldBeValid()
    {
        // todo(Gustav): add url
        // missing: ({{& Url}})";
        const string mustache = "{{#roots}}{{& Name}}{{#IsSelected}} (selected){{/IsSelected}}\n{{/roots}}";
        read.AddContent(Templates.CalculateTemplateDirectory(cwd).GetFile("_post.mustache.html"), mustache);
        read.AddContent(Templates.CalculateTemplateDirectory(cwd).GetFile("_dir.mustache.html"), mustache);
        read.AddContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        read.AddContent(content.GetFile("_index.md"),
            Facade.GeneratePost("Root", new FrontMatter { Date = new DateTime(2022, 1, 1) }));
        read.AddContent(content.GetFile("hello.md"),
            Facade.GeneratePost("Hello", new FrontMatter { Date = new DateTime(2022, 1, 2) }));
        read.AddContent(content.GetDir("world").GetFile("_index.md"),
            Facade.GeneratePost("World", new FrontMatter { Date = new DateTime(2022, 1, 3) }));

        var posts = content.GetDir("posts");
        read.AddContent(posts.GetFile("_index.md"),
            Facade.GeneratePost("Posts", new FrontMatter { Date = new DateTime(2022, 1, 4) }));
        read.AddContent(posts.GetFile("lorem.md"),
            Facade.GeneratePost("Lorem", new FrontMatter { Date = new DateTime(2022, 1, 5) }));

        var ret = await Facade.GenerateSite(run, read, write, cwd);
        using (new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();

            write.GetLines(publics.GetFile("index.html"))
                .Should().BeEquivalentTo("posts","World","Hello");
            write.GetLines(publics.GetDir("hello").GetFile("index.html"))
                .Should().BeEquivalentTo("posts", "World","Hello (selected)");
            write.GetLines(publics.GetDir("world").GetFile("index.html"))
                .Should().BeEquivalentTo("posts", "World (selected)", "Hello");
            write.GetLines(publics.GetDir("posts").GetFile("index.html"))
                .Should().BeEquivalentTo("posts (selected)","World", "Hello");
            write.GetLines(publics.GetSubDirs("posts", "lorem").GetFile("index.html"))
                .Should().BeEquivalentTo("posts (selected)", "World", "Hello");
        }

        write.RemainingFiles.Should().BeEmpty();
    }
}
