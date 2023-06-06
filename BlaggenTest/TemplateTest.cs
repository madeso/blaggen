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

    [Fact]
    public void Test1()
    {
        using (new AssertionScope())
        {
            var (evaluator, errors) = Template.Parse("{{artist}} - {{title}} ({{album}})", Template.DefaultFunctions(), MakeSongDef());
            evaluator(AbbaSong).Should().Be("ABBA - dancing queen (Arrival)");
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test2()
    {
        using (new AssertionScope())
        {
            var (evaluator, errors) = Template.Parse("{{artist}} - {{title | title}} ( {{- album -}} )", Template.DefaultFunctions(), MakeSongDef());
            evaluator(AbbaSong).Should().Be("ABBA - Dancing Queen (Arrival)");
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test3()
    {
        using (new AssertionScope())
        {
            var (evaluator, errors) = Template.Parse("{{track | zfill(3)}} {{- /** a comment **/ -}}  . {{title | title}}", Template.DefaultFunctions(), MakeSongDef());
            evaluator(AbbaSong).Should().Be("002. Dancing Queen");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test4()
    {
        using (new AssertionScope())
        {
            var (evaluator, errors) = Template.Parse("{{#songs}}[{{title}}]{{/songs}}", Template.DefaultFunctions(), MakeMixTapeDef());
            evaluator(AwesomeMix).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test4Readable()
    {
        using (new AssertionScope())
        {
            var (evaluator, errors) = Template.Parse("{{range songs}}[{{title}}]{{end}}", Template.DefaultFunctions(), MakeMixTapeDef());
            evaluator(AwesomeMix).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }
}
