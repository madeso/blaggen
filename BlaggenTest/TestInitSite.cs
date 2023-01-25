using Blaggen;

namespace BlaggenTest;

public class TestBase
{
    internal RunTest run = new();
    internal VfsReadTest read = new();
    internal VfsWriteTest write = new();
}

public class TestInitSite : TestBase
{
    [Fact]
    public async void SimpleRun()
    {
        var cwd = new DirectoryInfo(@"C:\test\");

        var ret = await Facade.InitSite(run, read, write, cwd);
        Assert.Equal(0, ret);

        // test content?
        write.GetContent(cwd.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION));
        Assert.True(write.IsEmpty());
    }

    [Fact]
    public async void FailWithExistingSite()
    {
        var cwd = new DirectoryInfo(@"C:\test\");
        read.AddContent(cwd.Parent!.GetFile(Constants.ROOT_FILENAME_WITH_EXTENSION), "{}");

        var ret = await Facade.InitSite(run, read, write, cwd);
        Assert.Equal(-1, ret);

        Assert.True(write.IsEmpty());
    }
}
