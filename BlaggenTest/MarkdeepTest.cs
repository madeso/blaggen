using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Blaggen;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace BlaggenTest;

public class MarkdeepTest
{
    static string protect(string s)
    {
        return s;
    }

    static string NoHighlight(Markdeep.Highlight h)
    {
        return Markdeep.escapeHTMLEntities(h.Code);
    }

    static void NoLog(string _)
    {
    }

    [Fact]
    public void TestList()
    {
        new Markdeep().replaceLists("", protect).Should().Be("");
        new Markdeep().replaceLists("* abc", protect).Should().Be("* abc");
        new Markdeep().replaceLists("\n\n * cat\n * dog\n", protect).Should().Be("\n\n\n<ul>\n <li class=\"asterisk\">cat\n</li>\n <li class=\"asterisk\">dog</li></ul>\n\n");
    }


    [Fact]
    public void TestSchedule()
    {
        new Markdeep().replaceScheduleLists("", protect).Should().Be("");

        new Markdeep().replaceScheduleLists("\n\n1922-06-10: Birthday\n1922-06-11: Birthday\n", protect).Should().Be(
            "\n\n"+
            "<table class=\"schedule\"><tr valign=\"top\"><td style=\"width:100px;padding-right:15px\" rowspan=\"2\"><a class=\"target\" name=\"schedule1_1922-6-10\">&nbsp;</a>Wednesday<br/>10&nbsp;06&nbsp;1922</td><td><b>Birthday</b></td></tr><tr valign=\"top\"><td style=\"padding-bottom:25px\">"
            +"\n\n" +
            "</td></tr><tr valign=\"top\"><td style=\"width:100px;padding-right:15px\" rowspan=\"2\"><a class=\"target\" name=\"schedule1_1922-6-11\">&nbsp;</a>Thursday<br/>11&nbsp;06&nbsp;1922</td><td><b>Birthday</b></td></tr><tr valign=\"top\"><td style=\"padding-bottom:25px\">"
            +"\n\n"+
            "</td></tr></table>"
            + "\n\n"
            );
    }

    private static string[] CleanHtml(string dirty)
    {
        return
            dirty.Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();
            ;
    }

    [Fact]
    public void TestMarkdeep()
    {
        CleanHtml(new Markdeep().markdeepToHTML("Hello world", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(
            CleanHtml("<span class=\"md\"><p><p>\nHello world</p></span>"));

        CleanHtml(new Markdeep().markdeepToHTML("*dog* is good", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(
            CleanHtml("<span class=\"md\"><p><p>\n<em class=\"asterisk\">dog</em> is good</p></span>"));

        CleanHtml(new Markdeep().markdeepToHTML("A list with just bullets:\n" +
                                                "- Bread\n" +
                                                "- Fish\n" +
                                                "- Milk\n" +
                                                "- Cheese\n" +
                                                "", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(CleanHtml(
            "<span class=\"md\"><p><p>\n" +
            "A list with just bullets:\n" +
            "</p><p>\n" +
            "<ul>\n" +
            "<li class=\"minus\">Bread\n" +
            "</li>\n" +
            "<li class=\"minus\">Fish\n" +
            "</li>\n" +
            "<li class=\"minus\">Milk\n" +
            "</li>\n" +
            "<li class=\"minus\">Cheese</li></ul>\n" +
            "</p><p>\n" +
            "</p></span>"));

        CleanHtml(new Markdeep().markdeepToHTML("Lists can also:\n" +
                                                "\n" +
                                                "* Use asterisks instead of minus signs\n" +
                                                "* `or have code`\n" +
                                                "* *and* other formatting\n" +
                                                "\n" +
                                                "o\n" +
                                                "\n" +
                                                "+ Use plus\n" +
                                                "+ Signs\n" +
                                                "", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(CleanHtml(
            "<span class=\"md\"><p><p>\n" +
            "Lists can also:\n" +
            "</p><p>\n" +
            "<ul>\n" +
            "<li class=\"asterisk\">Use asterisks instead of minus signs\n" +
            "</li>\n" +
            "<li class=\"asterisk\"><code>or have code</code>\n" +
            "</li>\n" +
            "<li class=\"asterisk\"><em class=\"asterisk\">and</em> other formatting</li></ul>\n" +
            "</p><p>\n" +
            "o\n" +
            "</p><p>\n" +
            "<ul>\n" +
            "<li class=\"plus\">Use plus\n" +
            "</li>\n<li class=\"plus\">Signs</li></ul>\n" +
            "</p><p>\n" +
            "</p></span>"));


        // table

        CleanHtml(new Markdeep().markdeepToHTML("\n\n | A |\n |---|\n | B |\n | C |\n | D |\n", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(
            CleanHtml("<span class=\"md\"><p><p>\n" +
                     "<div class='table'><table class=\"table\"><tr><th style=\"text-align:left\">  A  </th></tr>\n" +
                     "<tr><td style=\"text-align:left\">  B  </td></tr>\n" +
                     "<tr><td style=\"text-align:left\">  C  </td></tr>\n" +
                     "<tr><td style=\"text-align:left\">  D  </td></tr>\n" +
                     "</table></div>\n</p></span>"));

        // code
        CleanHtml(new Markdeep().markdeepToHTML("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ none\n" +
                                                "void insertion_sort(int data[], int length) {\n" +
                                                "    for (int i = 0; i < length; ++i) {\n" +
                                                "       ...\n" +
                                                "    }\n" +
                                                "}\n" +
                                                "~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n" +
                                                "", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(CleanHtml(
            "<span class=\"md\"><p><pre class=\"listing tilde\"><code><span class=\"line\"></span>void insertion_sort(int data[], int length) {\n" +
            "<span class=\"line\"></span>    for (int i = 0; i &lt; length; ++i) {\n" +
            "<span class=\"line\"></span>       ...\n" +
            "<span class=\"line\"></span>    }\n" +
            "<span class=\"line\"></span>}</code></pre><p>\n" +
            "</p></span>"));


        // header
        CleanHtml(new Markdeep().markdeepToHTML("Basic Formatting\n" +
                                                "===\n" +
                                                "", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(CleanHtml(
            "<span class=\"md\"><p>\n" +
            "<a class=\"target\" name=\"basicformatting\">&nbsp;</a><a class=\"target\" name=\"basicformatting\">&nbsp;</a><a class=\"target\" name=\"toc1\">&nbsp;</a><h1>Basic Formatting</h1>\n" +
            "<p>\n" +
            "</p></span>"));

        // link
        CleanHtml(new Markdeep().markdeepToHTML("\n" +
                                                "[Markdeep](http://casual-effects.com/markdeep)", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(CleanHtml(
            "<span class=\"md\"><p><p>\n" +
            "<a href=\"http://casual-effects.com/markdeep\">Markdeep</a></p></span>"));
    }

    [Fact]
    public void TestEscape()
    {
        Markdeep.unescapeHTMLEntities("I &lt;3 cats").Should().Be("I <3 cats");
        Markdeep.escapeHTMLEntities("I <3 cats").Should().Be("I &lt;3 cats");
    }

    [Fact]
    public void MarkdeepFail()
    {
        // longer
        CleanHtml(new Markdeep().markdeepToHTML("                      **Markdeep Feature Demo**\n" +
                                                "                           Morgan McGuire\n" +
                                                "\n" +
                                                "This demonstration documents the features of\n" +
                                                "[Markdeep](http://casual-effects.com/markdeep) and acts as a test fo\n" +
                                                "it.  Markdeep is a text formatting syntax that extends Markdown, and a\n" +
                                                "JavaScript program for making it work in browsers. The two most\n" +
                                                "powerful features are its ability to run in any **web browser** on the\n" +
                                                "client side and the inclusion of **diagrams**.\n" +
                                                "\n" +
                                                "[Click here](https://casual-effects.com/markdeep/features.md.html?noformat)\n" +
                                                "to see this document without automatic formatting.\n" +
                                                "\n" +
                                                "Markdeep is free and easy to use. It doesn't need a plugin, o\n" +
                                                "Internet connection. There's nothing to install. Just start\n" +
                                                "writing in Vi, Nodepad, Zed, Emacs, Visual Studio, Atom, or anothe\n" +
                                                "editor! You don't have to export, compile, or otherwise process\n" +
                                                "your document.\n" +
                                                "\n" +
                                                "If you want to support development of Markdeep, you can\n" +
                                                "[sponsor](https://github.com/sponsors/morgan3d) my open source\n" +
                                                "projects on GitHub for $2.\n" +
                                                "\n" +
                                                "\n" +
                                                "Basic Formatting\n" +
                                                "=======================================================================================\n" +
                                                "Text formatting: \n" +
                                                "", NoHighlight, NoLog, "", false)).Should().BeEquivalentTo(CleanHtml(
            "<span class=\"md\"><p><title>Markdeep Feature Demo</title><div class=\"title\"> Markdeep Feature Demo </div>\n" +
            "<div class=\"subtitle\"> Morgan McGuire </div>\n" +
            "<div class=\"afterTitles\"></div>\n" +
            "</p><p>\n" +
            "This demonstration documents the features of\n" +
            "<a href=\"http://casual-effects.com/markdeep\">Markdeep</a> and acts as a test fo\n" +
            "it.  Markdeep is a text formatting syntax that extends Markdown, and a\n" +
            "JavaScript program for making it work in browsers. The two most\n" +
            "powerful features are its ability to run in any <strong class=\"asterisk\">web browser</strong> on the\n" +
            "client side and the inclusion of <strong class=\"asterisk\">diagrams</strong>.\n" +
            "</p><p>\n" +
            "<a href=\"https://casual-effects.com/markdeep/features.md.html?noformat\">Click here</a>\n" +
            "to see this document without automatic formatting.\n" +
            "</p><p>\n" +
            "Markdeep is free and easy to use. It doesn't need a plugin, o\n" +
            "Internet connection. There's nothing to install. Just start\n" +
            "writing in Vi, Nodepad, Zed, Emacs, Visual Studio, Atom, or anothe\n" +
            "editor! You don't have to export, compile, or otherwise process\n" +
            "your document.\n" +
            "</p><p>\n" +
            "If you want to support development of Markdeep, you can\n" +
            "<a href=\"https://github.com/sponsors/morgan3d\">sponsor</a> my open source\n" +
            "projects on GitHub for $2.\n" +
            "</p>\n" +
            "<a class=\"target\" name=\"basicformatting\">&nbsp;</a><a class=\"target\" name=\"basicformatting\">&nbsp;</a><a class=\"target\" name=\"toc1\">&nbsp;</a><h1>Basic Formatting</h1>\n" +
            "<p>\n" +
            "Text formatting: \n" +
            "</p></span>"));
    }

    [Fact]
    public void TestIntParse10()
    {
        Markdeep.Convert_ToInt32("1", 10).Should().Be(1);
        Markdeep.Convert_ToString(1, 10).Should().Be("1");

        Markdeep.Convert_ToInt32("42", 10).Should().Be(42);
        Markdeep.Convert_ToString(42, 10).Should().Be("42");

        Markdeep.Convert_ToInt32("123", 10).Should().Be(123);
        Markdeep.Convert_ToString(123, 10).Should().Be("123");

        Markdeep.Convert_ToInt32("1234", 10).Should().Be(1234);
        Markdeep.Convert_ToString(1234, 10).Should().Be("1234");
    }

    [Fact]
    public void TestIntParse32()
    {
        Markdeep.Convert_ToString(432, 32).Should().Be("dg");
        Markdeep.Convert_ToInt32("dg", 32).Should().Be(432);
        // Markdeep.Convert_ToString(2147483647, 32).Should().Be("ZIK0ZJ");
    }
    
}
