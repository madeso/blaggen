using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;


public class TestInitSite : TestBase
{
    [Fact]
    public async void SimpleRun()
    {
        var cwd = new DirectoryInfo(@"C:\test\");

        var ret = await Facade.InitSite(run, read, write, cwd);
        using (new AssertionScope())
        {
            ret.Should().Be(0);
            run.Errors.Should().BeEmpty();
        }

        // test content?
        write.GetContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION));
        write.RemainingFiles.Should().BeEmpty();
    }

    [Fact]
    public async void FailWithExistingSite()
    {
        var cwd = new DirectoryInfo(@"C:\test\");
        read.AddContent(cwd.Parent!.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var ret = await Facade.InitSite(run, read, write, cwd);
        ret.Should().Be(-1);

        write.RemainingFiles.Should().BeEmpty();
    }
}
