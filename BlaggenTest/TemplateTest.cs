using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;

public class TemplateTest
{
    private record Song(string Artist, string Title, string Album, int Track);
    private static Template.Definition<Song> MakeSongDef() => new Template.Definition<Song>()
        .AddVar("artist", song => song.Artist)
        .AddVar("title", song => song.Title)
        .AddVar("album", song => song.Album)
        .AddVar("track", song => song.Track.ToString())
        ;

    private record MixTape(ImmutableArray<Song> Songs);
    private static Template.Definition<MixTape> MakeMixTapeDef() => new Template.Definition<MixTape>()
        .AddList("songs", mt => mt.Songs, MakeSongDef())
        ;

    private readonly Song AbbaSong = new("ABBA", "dancing queen", "Arrival", 2);
    private static readonly MixTape AwesomeMix = new(ImmutableArray.Create (
        new Song("Gloria Gaynor", "I Will Survive", "Nevermind", 1),
        new Song("Nirvana", "Smells Like Teen Spirit", "Love Tracks", 5)
    ));

    internal VfsReadTest read = new();
    private readonly DirectoryInfo cwd = new(@"C:\test\");

    [Fact]
    public async void Test1()
    {
        var file = cwd.GetFile("test.txt");
        read.AddContent(file, "{{artist}} - {{title}} ({{album}})");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeSongDef());
            evaluator(AbbaSong).Should().Be("ABBA - dancing queen (Arrival)");
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public async void Test2()
    {
        var file = cwd.GetFile("test.txt");
        read.AddContent(file, "{{artist}} - {{title | title}} ( {{- album -}} )");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeSongDef());
            evaluator(AbbaSong).Should().Be("ABBA - Dancing Queen (Arrival)");
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public async void Test3()
    {
        var file = cwd.GetFile("test.txt");
        read.AddContent(file, "{{track | zfill(3)}} {{- /** a comment **/ -}}  . {{title | title}}");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeSongDef());
            evaluator(AbbaSong).Should().Be("002. Dancing Queen");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public async void Test4()
    {
        var file = cwd.GetFile("test.txt");
        read.AddContent(file, "{{#songs}}[{{title}}]{{/songs}}");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeMixTapeDef());
            evaluator(AwesomeMix).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public async void Test4Readable()
    {
        var file = cwd.GetFile("test.txt");
        read.AddContent(file, "{{range songs}}[{{title}}]{{end}}");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeMixTapeDef());
            evaluator(AwesomeMix).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public async void Test5()
    {
        var file = cwd.GetFile("test.txt");
        read.AddContent(file, "{{range songs}} {{- include \"include.txt\" -}} {{end}}");
        read.AddContent(cwd.GetFile("include.txt"), "[{{title}}]");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeMixTapeDef());
            evaluator(AwesomeMix).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public async void Test5Alt()
    {
        var file = cwd.GetFile("test.txt");
        // use quotes to escape include keyword
        read.AddContent(file, "{{range songs}} {{- include \"include\" -}} {{end}}");
        read.AddContent(cwd.GetFile("include.txt"), "[{{title}}]");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeMixTapeDef());
            evaluator(AwesomeMix).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public async void Test6()
    {
        var file = cwd.GetFile("test.txt");
        read.AddContent(file, "{{range songs}} {{- include file -}} {{end}}");
        read.AddContent(cwd.GetFile("file.txt"), "[{{title}}]");
        using (new AssertionScope())
        {
            var (evaluator, errors) = await Template.Parse(file, read, Template.DefaultFunctions(), cwd, MakeMixTapeDef());
            evaluator(AwesomeMix).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }
}
