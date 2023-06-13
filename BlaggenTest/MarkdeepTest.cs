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

    private static string CleanHtml(string dirty)
    {
        return string.Join('\n',
            dirty.Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
            );
    }

    [Fact]
    public void TestMarkdeep()
    {
        CleanHtml(new Markdeep().markdeepToHTML("Hello world", NoHighlight, NoLog, "", false)).Should().Be(
            "<span class=\"md\"><p><p>\nHello world</p></span>");

        CleanHtml(new Markdeep().markdeepToHTML("*dog* is good", NoHighlight, NoLog, "", false)).Should().Be(
            "<span class=\"md\"><p><p>\n<em class=\"asterisk\">dog</em> is good</p></span>");

        CleanHtml(new Markdeep().markdeepToHTML("A list with just bullets:\n" +
                                                "- Bread\n" +
                                                "- Fish\n" +
                                                "- Milk\n" +
                                                "- Cheese\n" +
                                                "", NoHighlight, NoLog, "", false)).Should().Be(
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
            "</p></span>");

        CleanHtml(new Markdeep().markdeepToHTML("Lists can also:\n" +
                                                "\n" +
                                                "* Use asterisks instead of minus signs\n" +
                                                "* `or have code`\n" +
                                                "* *and* other formatting\n" +
                                                "\n" +
                                                "or\n" +
                                                "\n" +
                                                "+ Use plus\n" +
                                                "+ Signs\n" +
                                                "", NoHighlight, NoLog, "", false)).Should().Be(
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
            "or\n" +
            "</p><p>\n" +
            "<ul>\n" +
            "<li class=\"plus\">Use plus\n" +
            "</li>\n<li class=\"plus\">Signs</li></ul>\n" +
            "</p><p>\n" +
            "</p></span>");


        // table

        CleanHtml(new Markdeep().markdeepToHTML("\n\n | A |\n |---|\n | B |\n | C |\n | D |\n", NoHighlight, NoLog, "", false)).Should().Be(
            "<span class=\"md\"><p><p>\n" +
            "<div class='table'><table class=\"table\"><tr><th style=\"text-align:left\">  A  </th></tr>\n" +
            "<tr><td style=\"text-align:left\">  B  </td></tr>\n" +
            "<tr><td style=\"text-align:left\">  C  </td></tr>\n" +
            "<tr><td style=\"text-align:left\">  D  </td></tr>\n" +
            "</table></div>\n</p></span>");
    }

    [Fact]
    public void TestEscape()
    {
        Markdeep.unescapeHTMLEntities("I &lt;3 cats").Should().Be("I <3 cats");
        Markdeep.escapeHTMLEntities("I <3 cats").Should().Be("I &lt;3 cats");
    }

    [Fact]
    public void MarkdeepCode()
    {
        // code
        CleanHtml(new Markdeep().markdeepToHTML("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ none\n" +
                                                "void insertion_sort(int data[], int length) {\n" +
                                                "    for (int i = 0; i < length; ++i) {\n" +
                                                "       ...\n" +
                                                "    }\n" +
                                                "}\n" +
                                                "~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n" +
                                                "", NoHighlight, NoLog, "", false)).Should().Be(
            "<span class=\"md\"><p><pre class=\"listing tilde\"><code><span class=\"line\"></span>void insertion_sort(int data[], int length) {\n" +
            "<span class=\"line\"></span>    for (int i = 0; i &lt; length; ++i) {\n" +
            "<span class=\"line\"></span>       ...\n" +
            "<span class=\"line\"></span>    }\n" +
            "<span class=\"line\"></span>}</code></pre><p>\n" +
            "</p></span>");
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
