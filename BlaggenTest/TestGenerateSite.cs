﻿using Blaggen;
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

            var post = write.GetContent(publics.GetDir("post").GetFile("index.html"))
                .Should().Be("This is a post\n");
        }

        write.RemainingFiles.Should().BeEmpty();
    }
}