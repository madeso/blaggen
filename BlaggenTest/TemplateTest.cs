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
            var (node, errors) = Template.Parse("{{artist}} - {{title | title}} ({{album}})", Template.DefaultFunctions());
            node.Evaluate(abba).Should().Be("ABBA - Dancing Queen (Arrival)");
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void Test3()
    {
        using (new AssertionScope())
        {
            var (node, errors) = Template.Parse("{{track | zfill(3)}}. {{title | title}}", Template.DefaultFunctions());
            node.Evaluate(abba).Should().Be("002. Dancing Queen");

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest1()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("");
            scanned.Should().BeEquivalentTo(new[]
                { new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 0), "") });

            errors.Should().BeEquivalentTo(new Template.Error[]{});
        }
    }

    [Fact]
    public void ScannerTest2()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("dog");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Text, "dog", new Template.Location(1, 0), "dog"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 3), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest2A()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("{{}}");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Begin, "{{", new Template.Location(1, 0), "{{"),
                new Template.Token(Template.TokenType.End, "}}", new Template.Location(1, 2), "}}"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 4), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }
    
    [Fact]
    public void ScannerTest2B()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("{{-}}");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.BeginTrim, "{{-", new Template.Location(1, 0), "{{-"),
                new Template.Token(Template.TokenType.End, "}}", new Template.Location(1, 3), "}}"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 5), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest2C()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("{{ -}}");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Begin, "{{", new Template.Location(1, 0), "{{"),
                new Template.Token(Template.TokenType.EndTrim, "-}}", new Template.Location(1, 3), "-}}"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 6), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest3()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("{{cat}}");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Begin, "{{", new Template.Location(1, 0), "{{"),
                new Template.Token(Template.TokenType.Ident, "cat", new Template.Location(1, 2), "cat"),
                new Template.Token(Template.TokenType.End, "}}", new Template.Location(1, 5), "}}"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 7), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest4()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("awesome {{\"black cat\"");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Text, "awesome ", new Template.Location(1, 0), "awesome "),
                new Template.Token(Template.TokenType.Begin, "{{", new Template.Location(1, 8), "{{"),
                new Template.Token(Template.TokenType.Ident, "\"black cat\"", new Template.Location(1, 10),
                    "black cat"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 21), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest5()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("{{42 4.42}} { dog {");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Begin, "{{", new Template.Location(1, 0), "{{"),
                new Template.Token(Template.TokenType.Ident, "42", new Template.Location(1, 2), "42"),
                new Template.Token(Template.TokenType.Ident, "4.42", new Template.Location(1, 5), "4.42"),
                new Template.Token(Template.TokenType.End, "}}", new Template.Location(1, 9), "}}"),
                new Template.Token(Template.TokenType.Text, " { dog {", new Template.Location(1, 11), " { dog {"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 19), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest6()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("{{}}");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Begin, "{{", new Template.Location(1, 0), "{{"),
                new Template.Token(Template.TokenType.End, "}}", new Template.Location(1, 2), "}}"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 4), "")
            });
            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }

    [Fact]
    public void ScannerTest7()
    {
        using (new AssertionScope())
        {
            var (scanned, errors) = Template.Scanner("{{ /* this is a test */ }}");
            scanned.Should().BeEquivalentTo(new[]
            {
                new Template.Token(Template.TokenType.Begin, "{{", new Template.Location(1, 0), "{{"),
                new Template.Token(Template.TokenType.End, "}}", new Template.Location(1, 24), "}}"),
                new Template.Token(Template.TokenType.Eof, "", new Template.Location(1, 26), "")
            });

            errors.Should().BeEquivalentTo(new Template.Error[] { });
        }
    }
}