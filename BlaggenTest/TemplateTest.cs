using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;

public class TemplateTest
{
    private readonly Dictionary<string, string> abba = new()
    {
        {"artist", "ABBA"},
        {"title", "dancing queen"},
        {"album", "Arrival"},
        { "track", "2"}
    };

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
}