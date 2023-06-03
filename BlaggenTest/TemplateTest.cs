using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;

public class TemplateTest
{
    [Fact]
    public void Test()
    {
        var data = new Dictionary<string, string>
        {
            {"artist", "ABBA"},
            {"title", "dancing queen"},
            {"album", "Arrival"},
            { "track", "2"}
        };
        var f = Template.DefaultFunctions();

        Template.Compile("%artist% - %title% (%album%)").Evaluate(f, data).Should().Be("ABBA - dancing queen (Arrival)");
        Template.Compile("%artist% - $title(%title%) (%album%)").Evaluate(f, data).Should().Be("ABBA - Dancing Queen (Arrival)");
        Template.Compile("$zfill(%track%,3). $title(%title%)").Evaluate(f, data).Should().Be("002. Dancing Queen");
    }
}