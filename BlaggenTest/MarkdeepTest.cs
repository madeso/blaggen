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

    [Fact]
    public void TestIntParse()
    {
        Markdeep.Convert_ToInt32("1", 10).Should().Be(1);
        Markdeep.Convert_ToString(1, 10).Should().Be("1");

        Markdeep.Convert_ToInt32("42", 10).Should().Be(42);
        Markdeep.Convert_ToString(42, 10).Should().Be("42");

        
        Markdeep.Convert_ToString(123, 10).Should().Be("123");

        Markdeep.Convert_ToString(1234, 10).Should().Be("1234");
    }

    [Fact]
    public void TestParseFail()
    {
        Markdeep.Convert_ToInt32("123", 10).Should().Be(123);
        Markdeep.Convert_ToInt32("1234", 10).Should().Be(1234);
    }
    
}
