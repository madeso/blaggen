using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;

public class TemplateTest
{
    private static Template.Data MakeSong(string artist, string title, string album, string trackNumber) =>
        new(new Dictionary<string, string>()
        {
            {"artist", artist},
            {"title", title},
            {"album", album},
            { "track", trackNumber}
        }, new());

    private readonly Template.Data abba = MakeSong("ABBA", "dancing queen", "Arrival", "2");

    private readonly Template.Data songs = new(
        new(),
        new()
        {
            {
                "songs",
                new()
                {
                    MakeSong("Gloria Gaynor", "I Will Survive", "Nevermind", "1"),
                    MakeSong("Nirvana", "Smells Like Teen Spirit", "Love Tracks", "5")
                }
            }
        }
    );

    [Fact]
    public void Test1()
    {
        using (new AssertionScope())
        {
            var (node, errors) = Template.Parse("{{artist}} - {{title}} ({{album}})", Template.DefaultFunctions());
            node.Evaluate(abba).Should().Be("ABBA - dancing queen (Arrival)");
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test2()
    {
        using (new AssertionScope())
        {
            var (node, errors) = Template.Parse("{{artist}} - {{title | title}} ( {{- album -}} )", Template.DefaultFunctions());
            node.Evaluate(abba).Should().Be("ABBA - Dancing Queen (Arrival)");
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test3()
    {
        using (new AssertionScope())
        {
            var (node, errors) = Template.Parse("{{track | zfill(3)}} {{- /** a comment **/ -}}  . {{title | title}}", Template.DefaultFunctions());
            node.Evaluate(abba).Should().Be("002. Dancing Queen");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test4()
    {
        using (new AssertionScope())
        {
            var (node, errors) = Template.Parse("{{#songs}}[{{title}}]{{/songs}}", Template.DefaultFunctions());
            node.Evaluate(songs).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test4Readable()
    {
        using (new AssertionScope())
        {
            var (node, errors) = Template.Parse("{{range songs}}[{{title}}]{{end}}", Template.DefaultFunctions());
            node.Evaluate(songs).Should().Be("[I Will Survive][Smells Like Teen Spirit]");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }
}