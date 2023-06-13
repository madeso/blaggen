using System;
using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Blaggen;

// (Note: invisible BOM on this line!)
/** 

  Markdeep.js
  Version 1.15

  Copyright 2015-2022, Morgan McGuire, https://casual-effects.com
  All rights reserved.

  -------------------------------------------------------------

  See https://casual-effects.com/markdeep for documentation on how to
  use this script make your plain text documents render beautifully
  in web browsers.

  Markdeep was created by Morgan McGuire. It extends the work of:

   - John Gruber's original Markdown
   - Ben Hollis' Maruku Markdown dialect
   - Michel Fortin's Markdown Extras dialect
   - Ivan Sagalaev's highlight.js
   - Contributors to the above open source projects

  -------------------------------------------------------------
 
  You may use, extend, and redistribute this code under the terms of
  the BSD license at https://opensource.org/licenses/BSD-2-Clause.

  Contains highlight.js (https://github.com/isagalaev/highlight.js) by Ivan
  Sagalaev, which is used for code highlighting. (BSD 3-clause license)

  There is an invisible Byte-Order-Marker at the start of this file to
  ensure that it is processed as UTF-8. Do not remove this character or it
  will break the regular expressions in highlight.js.
*/
/** See https://casual-effects.com/markdeep for @license and documentation.
markdeep.min.js 1.15 (C) 2022 Morgan McGuire 
*/

public enum TocStyle
{
    auto, none, kshort, medium, klong
}

public class Options
{
    public string mode =               "markdeep";
    public bool detectMath =         true;
    public Dictionary<string, string> lang = new(); // English
    public TocStyle tocStyle =           TocStyle.auto;
    public bool hideEmptyWeekends=  true;
    public bool autoLinkImages=     true;
    public bool showLabels=         false;
    public bool sortScheduleLists=  true;
    public string definitionStyle=    "auto";
    public bool linkAPIDefinitions= false;
    public string? inlineCodeLang=     null;
    public int scrollThreshold=    90;
    public bool captionAbove_diagram= false;
    public bool captionAbove_image =   false;
    public bool captionAbove_table =   false;
    public bool captionAbove_listing = false;

    public bool smartQuotes = true;
};

public static class MarkdownExtensions
{
    public static string ss(this string s, int start, int end)
    {
        return s.Substring(start, end - start);
    }

    public static string rp(this string s, Regex re, string rp)
    {
        return re.Replace(s, rp);
    }
    public static string rp(this string s, Regex re, MatchEvaluator rep)
    {
        return re.Replace(s, rep);
    }

    public static string Repeat(this string s, int count)
    {
        return string.Concat(Enumerable.Repeat(s, count));
    }

    public static IEnumerable<T> slice<T>(this IEnumerable<T> list, int start, int end)
    {
        return list.Skip(start).Take(end - start);
    }

    public static IEnumerable<T> splice<T>(this List<T> list, int start, int end)
    {
        var ret = list.Skip(start).Take(end - start);
        list.RemoveRange(start, end - start);
        return ret;
    }

    public static int IndexOf<T>(this IEnumerable<T> list, T t)
    {
        foreach (var (v,i) in list.Select((v, i) => (v,i)))
        {
            if (!EqualityComparer<T>.Default.Equals(v, t))
            {
                return i;
            }
        }

        return -1;
    }

    public static IEnumerable<(T, int)> WihIndex<T>(this IEnumerable<T> list)
    {
        return list.Select((x, index) => (x, index));
    }

    public static int push<T>(this List<T> list, T item)
    {
        list.Add(item);
        return list.Count;
    }

    public static void unshift<T>(this List<T> list, params T[] newItems)
    {
        list.InsertRange(0, newItems);
    }

    public static double TimeSinceEpoch(this DateTime now)
    {
        DateTime epoch = DateTime.UnixEpoch;

        TimeSpan ts = now.Subtract(epoch);
        return ts.TotalMilliseconds;
    }

    public static T? pop<T>(this List<T> list)
        where T : class
    {
        if(list.Count > 0)
        {
            var index = list.Count-1;
            var r = list[index];
            list.RemoveAt(index);
            return r;
        }
        else
        {
            return null;
        }
    }

    public static string S1(this Match m) => m.Groups[1].Value;
    public static string S2(this Match m) => m.Groups[2].Value;
    public static string S3(this Match m) => m.Groups[3].Value;
    public static string S4(this Match m) => m.Groups[4].Value;
    public static string S5(this Match m) => m.Groups[5].Value;
    public static string S6(this Match m) => m.Groups[6].Value;

    public static string Get1(this Match m)
        => m.S1();
    public static (string, string) Get2(this Match m)
        => (m.S1(), m.S2());
    public static (string, string, string) Get3(this Match m)
        => (m.S1(), m.S2(), m.S3());
    public static (string, string, string, string) Get4(this Match m)
        => (m.S1(), m.S2(), m.S3(), m.S4());
    public static (string, string, string, string, string) Get5(this Match m)
        => (m.S1(), m.S2(), m.S3(), m.S4(), m.S5());
    public static (string, string, string, string, string, string) Get6(this Match m)
        => (m.S1(), m.S2(), m.S3(), m.S4(), m.S5(), m.S6());

    public static (string, string?) Get1Op1(this Match m)
        => (m.S1(), m.Groups.Count >= 2 ? m.S2() : null);
    public static (string, string, string?) Get2Op1(this Match m)
        => (m.S1(), m.S2(), m.Groups.Count >= 3 ? m.S3() : null);
    public static (string, string, string, string, string?) Get4Op1(this Match m)
        => (m.S1(), m.S2(), m.S3(), m.S4(), m.Groups.Count >=5 ? m.S5() : null);
}

public class Markdeep
{
    public string MARKDEEP_FOOTER = "<div class=\"markdeepFooter\"><i>formatted by <a href=\"https://casual-effects.com/markdeep\" style=\"color:#999\">Markdeep&nbsp;1.15&nbsp;&nbsp;</a></i><div style=\"display:inline-block;font-size:13px;font-family:\'Times New Roman\',serif;vertical-align:middle;transform:translate(-3px,-1px)rotate(135deg);\">&#x2712;</div></div>";
    
    /** Enable for debugging to view character bounds in diagrams */
    public bool DEBUG_SHOW_GRID = false;

    /** Overlay the non-empty characters of the original source in diagrams */
    public bool DEBUG_SHOW_SOURCE => DEBUG_SHOW_GRID;

    /** Use to suppress passing through text in diagrams */
    public bool DEBUG_HIDE_PASSTHROUGH => DEBUG_SHOW_SOURCE;

    /** In pixels of lines in diagrams */
    const int STROKE_WIDTH = 2;

    /** A box of these denotes a diagram */
    public char DIAGRAM_MARKER = '*';

    // http://stackoverflow.com/questions/1877475/repeat-character-n-times
    // ECMAScript 6 has a String.repeat method, but that's not available everywhere
    public string DIAGRAM_START => new string(DIAGRAM_MARKER, 5 + 1);

    public Options option = new();

    private static string entag(string tag, string content, string? attribs=null) {
        return "<" + tag + (attribs!=null ? " " + attribs : "") + ">" + content + "</" + tag + ">";
    }

    private string maybeShowLabel(string url, string? tag=null)
    {
        if (option.showLabels)
        {
            var text = " {\u00A0" + url + "\u00A0}";
            return tag!=null ? entag(tag, text) : text;
        }
        else
        {
            return "";
        }
    }

    // Returns the localized version of word, defaulting to the word itself
    private string keyword(string word)
    {
        if(option.lang.TryGetValue(word, out var value)) return value;
        if (option.lang.TryGetValue(word.ToLowerInvariant(), out value)) return value;
        return word;
    }


    /** Restores the original source string's '<' and '>' as entered in
        the document, before the browser processed it as HTML. There is no
        way in an HTML document to distinguish an entity that was entered
        as an entity. */
    static string unescapeHTMLEntities(string str) {
        // Process &amp; last so that we don't recursively unescape
        // escaped escape sequences.
        return str.
            rp(new Regex(@"&lt;"), "<").
            rp(new Regex(@"&gt;"), ">").
            rp(new Regex(@"&quot;"), "\"").
            rp(new Regex(@"&#39;"), "'").
            rp(new Regex(@"&ndash;"), "\u2013").
            rp(new Regex(@"&mdash;"), "---").
            rp(new Regex(@"&amp;"), "&");
    }


    static string removeHTMLTags(string str) {
        return str.rp(new Regex("<.*?>"), "");
    }


    public enum AlignmentHint
    {
        floatleft,
        floatright,
        center,
        flushleft,
    }
    
    /** Extracts one diagram from a Markdown string.

        Returns {beforeString, diagramString, alignmentHint, afterString}
        diagramString will be empty if nothing was found. The
        DIAGRAM_MARKER is stripped from the diagramString. 

        alignmentHint may be:
        floatleft  
        floatright
        center
        flushleft

        diagramString does not include the marker characters. 
        If there is a caption, it will appear in the afterString and not be parsed.
    */
    public class ExtractedDiagram
    {
        public ExtractedDiagram(string beforeString, string diagramString, AlignmentHint alignmentHint, string afterString)
        {
            this.beforeString = beforeString;
            this.diagramString = diagramString;
            this.alignmentHint = alignmentHint;
            this.afterString = afterString;
        }

        public string beforeString { get; set; }
        public string diagramString { get; set; }
        public AlignmentHint alignmentHint { get; set; }
        public string afterString { get; set; }
        public string? caption { get; set; } = null;
    }

    public ExtractedDiagram extractDiagram(string sourceString) {
        // Returns the number of wide Unicode symbols (outside the BMP) in string s between indices
        // start and end - 1
        static int unicodeSyms(string s, int start, int end) {
            var p = start;
            for (int i = start; i < end; ++i, ++p) {
                var c = s[p];
                p += (c >= 0xD800) && (c <= 0xDBFF) ? 1 : 0;
            }
            return p - end;
        }

        var noDiagramResult = new ExtractedDiagram(beforeString: sourceString, diagramString: "", alignmentHint: AlignmentHint.center, afterString: "");

        // Search sourceString for the first rectangle of enclosed
        // DIAGRAM_MARKER characters at least DIAGRAM_START.Length wide
        for (var i = sourceString.IndexOf(DIAGRAM_START);
            i >= 0;
            i = sourceString.IndexOf(DIAGRAM_START, i + DIAGRAM_START.Length)) {

            // We found what looks like a diagram start. See if it has either a full border of
            // aligned '*' characters, or top-left-bottom borders and nothing but white space on
            // the left.
            
            // Look backwards to find the beginning of the line (or of the string)
            // and measure the start character relative to it
            var lineBeginning = Math.Max(0, sourceString.LastIndexOf('\n', i)) + 1;
            var xMin = i - lineBeginning;
            
            // Find the first non-diagram character on this line...or the end of the entire source string
            int j;
            for (j = i + DIAGRAM_START.Length; sourceString[j] == DIAGRAM_MARKER; ++j) {}
            var xMax = j - lineBeginning - 1;
            
            // We have a potential hit. Start accumulating a result. If there was anything
            // between the newline and the diagram, move it to the after string for proper alignment.
            var result = new ExtractedDiagram(
                beforeString: sourceString.ss(0, lineBeginning), 
                diagramString: "",
                alignmentHint: AlignmentHint.center, 
                afterString: sourceString.ss(lineBeginning, i).rp(new Regex("[ \t]+$"), " ")
            );

            var nextLineBeginning = 0;
            var wideCharacters = 0;
            var textOnLeft = false;
            var textOnRight = false;
            var noRightBorder = false;

            void advance() {
                nextLineBeginning = sourceString.IndexOf('\n', lineBeginning) + 1;
                wideCharacters = unicodeSyms(sourceString, lineBeginning + xMin, lineBeginning + xMax);
                textOnLeft  = textOnLeft  || new Regex(@"\S").IsMatch(sourceString.ss(lineBeginning, lineBeginning + xMin));
                noRightBorder = noRightBorder || (sourceString[lineBeginning + xMax + wideCharacters] != '*');

                // Text on the right ... if the line is not all '*'
                textOnRight = ! noRightBorder && (textOnRight || new Regex(@"[^ *\t\n\r]").IsMatch(sourceString.ss(lineBeginning + xMax + wideCharacters + 1, nextLineBeginning)));
            }


            advance();

            // Now, see if the pattern repeats on subsequent lines
            var good = true;
            var previousEnding = j;
            while(good) {
                // Find the next line
                lineBeginning = nextLineBeginning;
                advance();
                if (lineBeginning == 0) {
                    // Hit the end of the string before the end of the pattern
                    return noDiagramResult; 
                }
                
                if (textOnLeft) {
                    // Even if there is text on *both* sides
                    result.alignmentHint = AlignmentHint.floatright;
                } else if (textOnRight) {
                    result.alignmentHint = AlignmentHint.floatleft;
                }
                
                // See if there are markers at the correct locations on the next line
                if ((sourceString[lineBeginning + xMin] == DIAGRAM_MARKER) && 
                    (! textOnLeft || (sourceString[lineBeginning + xMax + wideCharacters] == DIAGRAM_MARKER))) {

                    // See if there's a complete line of DIAGRAM_MARKER, which would end the diagram
                    int x;
                    for (x = xMin; (x < xMax) && (sourceString[lineBeginning + x] == DIAGRAM_MARKER); ++x) {}
            
                    var begin = lineBeginning + xMin;
                    var end   = lineBeginning + xMax + wideCharacters;
                    
                    if (! textOnLeft) {
                        // This may be an incomplete line
                        var newlineLocation = sourceString.IndexOf('\n', begin);
                        if (newlineLocation != -1) {
                            end = Math.Min(end, newlineLocation);
                        }
                    }

                    // Trim any excess whitespace caused by our truncation because Markdown will
                    // interpret that as fixed-formatted lines
                    result.afterString += sourceString.ss(previousEnding, begin).rp(new Regex(@" ^[ \t]*[ \t]"), " ").rp(new Regex(@"[ \t][ \t]*$"), " ");
                    if (x == xMax) {
                        // We found the last row. Put everything else into
                        // the afterString and return the result.
                    
                        result.afterString += sourceString.Substring(lineBeginning + xMax + 1);
                        return result;
                    } else {
                        // A line of a diagram. Extract everything before
                        // the diagram line started into the string of
                        // content to be placed after the diagram in the
                        // final HTML
                        result.diagramString += sourceString.ss(begin + 1, end) + '\n';
                        previousEnding = end + 1;
                    }
                } else {
                    // Found an incorrectly delimited line. Abort
                    // processing of this potential diagram, which is now
                    // known to NOT be a diagram after all.
                    good = false;
                }
            } // Iterate over verticals in the potential box
        } // Search for the start

        return noDiagramResult;
    }


    /** Turn the argument into a legal URL anchor */
    private static string mangle(string text)
    {
        var r = new Regex(@"\s");
        return Uri.EscapeDataString(r.Replace(text, "").ToLowerInvariant());
    }

    /** Creates a style sheet containing elements like:

    hn::before { 
        content: counter(h1) "." counter(h2) "." ... counter(hn) " "; 
        counter-increment: hn; 
    } 
    */
    public static string sectionNumberingStylesheet()
    {
        var s = "";

        for (int i = 1; i <= 6; ++i)
        {
            s += ".md h" + i + "::before {\ncontent:";
            for (var j = 1; j <= i; ++j) {
                s += "counter(h" + j + ") \"" + ((j < i) ? "." : " ") + "\"";
            }
            s += ";\ncounter-increment: h" + i + ";margin-right:10px}\n\n";
        }

        return entag("style", s);
    }


    // TABLE, LISTING, and FIGURE LABEL NUMBERING: Figure [symbol]: Table [symbol]: Listing [symbol]: Diagram [symbol]:

    // This data structure maps caption types [by localized name] to a count of how many of
    // that type of object exist.
    Dictionary<string, int> refCounter = new();

    // refTable['type_symbolicName'] = {number: number to link to, used: bool}
    public class NumUsed
    {
        public int number { get; init; }
        public bool used { get; set; }
        public string source { get; init; }

        public NumUsed(int number, bool used, string source)
        {
            this.number = number;
            this.used = used;
            this.source = source;
        }
    }
    Dictionary<string, NumUsed> refTable = new();

    // Processes Figure|Diagram|Table|Listing captions and returns the anchor tag with the numbered caption
    public record Target(string target, string caption);
    public Target createTarget(string caption, Func<string, string> protect){
        var pattern = new Regex("\\[?(?<type>"+ keyword("figure") + "|" + keyword("table") + "|" + keyword("listing") + "|" + keyword("diagram") + ")" + @"\s+\[(?<ref>.+?)\]:(?<text>.*[^\]])\]?", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var match = pattern.Match(caption);
        if (match.Success) {
            var type = match.Groups["type"].Value.ToLowerInvariant();
            var _ref = match.Groups["ref"].Value;
            // Increment the counter
            var count = refCounter[type] = refCounter.GetValueOrDefault(type, 0) + 1;
            var theRef = type + "_" + mangle(_ref.ToLowerInvariant().Trim());

            // Store the reference number
            refTable[theRef] = new NumUsed(number: count, used: false, source: type + " [" + _ref + "]");

            return new Target(
                target: protect(entag("a", "&nbsp;", protect("class=\"target\" name=\"" + theRef + "\""))),
                caption: entag("b", type[0].ToString().ToUpperInvariant() + type.Substring(1) + "&nbsp;" + count + ":", protect("style=\"font-style:normal;\"") +
                maybeShowLabel(_ref)) + match.Groups["text"].Value
            );
            
        } else {
            return new Target(
                target: "",
                caption: caption
            );
        }
    }




    

    /** Maruku ("github")-style table processing */
    string replaceTables(string s, Func<string, string> protect)
    {
        var TABLE_ROW       = @"(?:\n[ \t]*(?:(?:\|?[ \t\S]+?(?:\|[ \t\S]+?)+\|?)|\|[ \t\S]+\|)(?=\n))";
        var TABLE_SEPARATOR = @"\n[ \t]*(?:(?:\|? *\:?-+\:?(?: *\| *\:?-+\:?)+ *\|?|)|\|[\:-]+\|)(?=\n)";
        var TABLE_CAPTION   = @"\n[ \t]*\[[^\n\|]+\][ \t]*(?=\n)";
        var TABLE_REGEXP    = new Regex(TABLE_ROW + TABLE_SEPARATOR + TABLE_ROW + "+(" + TABLE_CAPTION + ")?");

        static string trimTableRowEnds(string row)
        {
            return new Regex(@"^\||\|$").Replace(row.Trim(), "");
        }

        s = TABLE_REGEXP.Replace(s, (match) => {
            // Found a table, actually parse it by rows
            var rowArray = match.Value.Split("\n").ToList();
            
            var result = "";
            
            // Skip the bogus leading row
            var startRow = (rowArray[0] == "") ? 1 : 0;

            string? caption = rowArray[rowArray.Count - 1].Trim();

            if ((caption.Length > 3) && (caption[0] == '[') && (caption[caption.Length - 1] == ']')) {
                // Remove the caption from the row array
                rowArray.pop();
                caption = caption.ss(1, caption.Length - 1);
            } else {
                caption = null;
            }

            // Parse the separator row for left/center/right-indicating colons
            var columnStyle = 
            new Regex(@":?-+:?").Matches(trimTableRowEnds(rowArray[startRow + 1])).Select(match => {
                var left = (match.Value[0] == ':');
                var right = (match.Value[match.Length - 1] == ':');
                return protect(" style=\"text-align:" + ((left && right) ? "center" : (right ? "right" : "left")) + "\"");
            }).ToImmutableArray();

            var row = rowArray[startRow + 1].Trim();
            var hasLeadingBar  = row[0] == '|';
            var hasTrailingBar = row[row.Length - 1] == '|';
            
            var tag = "th";
            
            for (var r = startRow; r < rowArray.Count; ++r) {
                // Remove leading and trailing whitespace and column delimiters
                row = rowArray[r].Trim();
                
                if (! hasLeadingBar && (row[0] == '|')) {
                    // Empty first column
                    row = "&nbsp;" + row;
                }
                
                if (! hasTrailingBar && (row[row.Length - 1] == '|')) {
                    // Empty last column
                    row += "&nbsp;";
                }
                
                row = trimTableRowEnds(row);
                var i = 0;
                result += entag("tr", "<" + tag + columnStyle[0] + "> " + 
                                new Regex(@"/ *\| */").Replace(row, _ => {
                                    ++i;
                                    return " </" + tag + "><" + tag + columnStyle[i] + "> ";
                                }) + " </" + tag + ">") + "\n";
                
                // Skip the header-separator row
                if (r == startRow) { 
                    ++r; 
                    tag = "td";
                }
            }
            
            result = entag("table", result, protect("class=\"table\""));

            if (caption != null) {
                var processedCaption = createTarget(caption, protect);
                caption = entag("center", entag("div", processedCaption.caption, protect("class=\"tablecaption\"")));
                if (option.captionAbove_table) {
                    result = processedCaption.target + caption + result;
                } else {
                    result = "\n" + processedCaption.target + result + caption;
                }
            }

            return entag("div", result, "class='table'");
        });

        return s;
    }









    private record replaceListsStackEntry(int indentLevel, string indentChars, string tag);
    public string replaceLists(string s, Func<string, string> protect)
    {
        // Identify task list bullets in a few patterns and reformat them to a standard format for
        // easier processing.
        s = new Regex(@"^(\s*)(?:-\s*)?(?:\[ \]|\u2610)(\s+)").Replace(s, match => $"{match.Groups[1].Value}\u2610{match.Groups[2].Value}");
        s = new Regex(@"^(\s*)(?:-\s*)?(?:\[[xX]\]|\u2611)(\s+)").Replace(s, match => $"{match.Groups[1].Value}\u2611{match.Groups[2].Value}");
            
        // Identify list blocks:
        // Blank line or line ending in colon, line that starts with #., *, +, -, ☑, or ☐
        // and then any number of lines until another blank line
        var BLANK_LINES = @"\n\s*\n";

        // Preceding line ending in a colon

        // \u2610 is the ballot box (unchecked box) character
        var PREFIX     = @"[:,]\s*\n";
        var LIST_BLOCK_REGEXP = 
            new Regex("(" + PREFIX + "|" + BLANK_LINES + @"|<p>\s*\n|<br/>\s*\n?)" +
                        @"((?:[ \t]*(?:\d+\.|-|\+|\*|\u2611|\u2610)(?:[ \t]+.+\n(?:[ \t]*\n)?)+)+)", RegexOptions.Multiline);

        var keepGoing = true;

        var ATTRIBS = new Dictionary<char, string>
        {
            {'+', protect("class=\"plus\"")},
            {'-', protect("class=\"minus\"")},
            {'*', protect("class=\"asterisk\"")},
            {'\u2611', protect("class=\"checked\"")},
            {'\u2610', protect("class=\"unchecked\"")}
        };
        var NUMBER_ATTRIBS = protect("class=\"number\"");

        // Sometimes the list regexp grabs too much because subsequent lines are indented *less*
        // than the first line. So, if that case is found, re-run the regexp.
        while (keepGoing)
        {
            keepGoing = false;
            s = LIST_BLOCK_REGEXP.Replace(s, (match) => {
                var prefix = match.Groups[1].Value;
                var block = match.Groups[2].Value;
                var result = prefix;
                
                // Contains {indentLevel, tag}
                var stack = new List<replaceListsStackEntry>();
                replaceListsStackEntry? current = new(indentLevel: -1, indentChars:string.Empty, tag: string.Empty);
                
                foreach(var line in block.Split('\n'))
                {
                    var trimmed     = new Regex(@"^\s*").Replace(line, "");
                    
                    var indentLevel = line.Length - trimmed.Length;
                    
                    // Add a CSS class based on the type of list bullet
                    var isUnordered = false;
                    if (ATTRIBS.TryGetValue(trimmed.Length > 0 ? trimmed[0]: '\0', out var attribs))
                    {
                        isUnordered = true;
                    }
                    else
                    {
                        attribs = NUMBER_ATTRIBS;
                    }

                    var isOrdered   = new Regex(@"^\d+\.[ \t]").IsMatch(trimmed);
                    var isBlank     = trimmed == "";
                    var start       = isOrdered ? " " + protect("start=" + new Regex(@"^\d+").Match(trimmed).Groups[0].Value) : "";

                    if (isOrdered || isUnordered) {
                        // Add the indentation for the bullet itself
                        indentLevel += 2;
                    }

                    if (current == null) {
                        // Went below top-level indent
                        result += "\n" + line;
                    } else if (! isOrdered && ! isUnordered && (isBlank || (indentLevel >= current.indentLevel))) {
                        // Line without a marker
                        result += "\n" + current.indentChars + line;
                    } else {
                        if (indentLevel != current.indentLevel) {
                            // Enter or leave indentation level
                            if ((current.indentLevel != -1) && (indentLevel < current.indentLevel)) {
                                while (current!=null && (indentLevel < current.indentLevel)) {
                                    stack.pop();
                                    // End the current list and decrease indentation
                                    result += "\n</li></" + current.tag + ">";
                                    current = stack[stack.Count - 1];
                                }
                            } else {
                                // Start a new list that is more indented
                                current = new replaceListsStackEntry(indentLevel: indentLevel,
                                        tag:         isOrdered ? "ol" : "ul",
                                        // Subtract off the two indent characters we added above
                                        indentChars: line.ss(0, indentLevel - 2));
                                stack.push(current);
                                result += "\n<" + current.tag + start + ">";
                            }
                        } else if (current.indentLevel != -1) {
                            // End previous list item, if there was one
                            result += "\n</li>";
                        } // Indent level changed
                        
                        if (current != null) {
                            // Add the list item
                            result += "\n" + current.indentChars + "<li " + attribs + ">" + new Regex(@"^(\d+\.|-|\+|\*|\u2611|\u2610) ").Replace(trimmed, "");
                        } else {
                            // Just reached something that is *less* indented than the root--
                            // copy forward and then re-process that list
                            result += "\n" + line;
                            keepGoing = true;
                        }
                    }
                }; // For each line

                // Remove trailing whitespace
                result = new Regex(@"\s+$").Replace(result,"");
                
                // Finish the last item and anything else on the stack (if needed)
                for (current = stack.pop(); current!=null; current = stack.pop()) {
                    result += "</li></" + current.tag + ">";
                }
        
                return result + "\n\n";
            });
        } // while keep going

        return s;
    }











    static T[] Array<T>(params T[] arr)
    {
        return arr;
    }




    private class DateParseException : Exception {}

    /** 
    Identifies schedule lists, which look like:

      date: title
        events

      Where date must contain a day, month, and four-number year and may
      also contain a day of the week.  Note that the date must not be
      indented and the events must be indented.

      Multiple events per date are permitted.
    */
    private record replaceScheduleListsEntry(
        DateTime date,
        string title,
        int sourceOrder,
        bool parenthesized,
        string text
    );
    public string replaceScheduleLists(string str, Func<string, string> protect)
    {
        // Must open with something other than indentation or a list
        // marker.  There must be a four-digit number somewhere on the
        // line. Exclude lines that begin with an HTML tag...this will
        // avoid parsing headers that have dates in them.
        var BEGINNING = @"^(?:[^\|<>\s-\+\*\d].*[12]\d{3}(?!\d).*?|(?:[12]\d{3}(?!\.).*\d.*?)|(?:\d{1,3}(?!\.).*[12]\d{3}(?!\d).*?))";

        // There must be at least one more number in a date, a colon, and then some more text
        var DATE_AND_TITLE = "(" + BEGINNING + "):" + @"[ \t]+([^ \t\n].*)\n";

        // The body of the schedule item. It may begin with a blank line and contain
        // multiple paragraphs separated by blank lines...as long as there is indenting
        var EVENTS = @"(?:[ \t]*\n)?((?:[ \t]+.+\n(?:[ \t]*\n){0,3})*)";
        var ENTRY = DATE_AND_TITLE + EVENTS;

        var BLANK_LINE = "\n[ \t]*\n";
        var ENTRY_REGEXP = new Regex(ENTRY, RegexOptions.Multiline);

        var rowAttribs = protect("valign=\"top\"");
        var dateTDAttribs = protect("style=\"width:100px;padding-right:15px\" rowspan=\"2\"");
        var eventTDAttribs = protect("style=\"padding-bottom:25px\"");

        var DAY_NAME = Array("Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday").Select(keyword).ToImmutableArray();
        var MONTH_NAME = Array("jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec").Select(keyword).ToImmutableArray();
        var MONTH_FULL_NAME = Array("January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December").Select(keyword).ToImmutableArray();

        string clean(string s) { return s.ToLowerInvariant().Replace(".", ""); }
        var LOWERCASE_MONTH_NAME = MONTH_NAME.Select(clean).ToImmutableArray();
        var LOWERCASE_MONTH_FULL_NAME = MONTH_FULL_NAME.Select(clean).ToImmutableArray();

        // Allow a period (and capture it) after each word, but eliminate
        // the periods that are in abbreviations so that they do not appear
        // in the regexp as wildcards or word breaks
        var MONTH_NAME_LIST = "\\b"
            + string.Join("(?:\\.|\\b)|\\b", MONTH_NAME.Concat(MONTH_FULL_NAME))
            .rp(new Regex(@"([^\\])\."), m => m.Groups[1].Value) + "(?:\\.|\\b)";

        // Used to mark the center of each day. Not close to midnight to avoid daylight
        // savings problems.
        var standardHour = 9;

        try
        {
            var scheduleNumber = 0;
            str = str.rp(new Regex(BLANK_LINE + "(" + ENTRY + "){2,}", RegexOptions.Multiline),
            schedule =>
            {
                ++scheduleNumber;
                // Each entry has the form {date:date, title:string, text:string}
                List<replaceScheduleListsEntry> entryArray = new();

                // Now parse the schedule into individual day entries

                var anyWeekendEvents = false;

                schedule.Value.rp
                (ENTRY_REGEXP,
                entry =>
                {
                    var date = entry.Groups[1].Value;
                    var title = entry.Groups[2].Value;
                    var events = entry.Groups[3].Value;
                    // Remove the day from the date (we'll reconstruct it below). This is actually unnecessary, since we
                    // explicitly compute the value anyway and the parser is robust to extra characters, but it aides
                    // in debugging.
                    // 
                    // date = date.rp(new Regex(@"(?:(?:sun|mon|tues|wednes|thurs|fri|satur)day|(?:sun|mon|tue|wed|thu|fri|sat)\.?|(?:su|mo|tu|we|th|fr|sa)),?", RegexOptions.IgnoreCase), "");

                    // Parse the date. The Javascript Date class's parser is useless because it
                    // is locale dependent, so we do this with a regexp.

                    var year = "";
                    var month = "";
                    var day = "";
                    var parenthesized = false;

                    date = date.Trim();

                    if ((date.StartsWith("(")) && (date.EndsWith(")")))
                    {
                        // This is a parenthesized entry
                        date = date.Substring(1, date.Length - 2);
                        parenthesized = true;
                    }

                    // DD MONTH YYYY
                    var DD_MONTH_YYYY = new Regex("([0123]?\\d)\\D+([01]?\\d|" + MONTH_NAME_LIST + ")\\D+([12]\\d{3})", RegexOptions.IgnoreCase);
                    var match = DD_MONTH_YYYY.Match(date);

                    if (match.Success)
                    {
                        day = match.Groups[1].Value;
                        month = match.Groups[2].Value;
                        year = match.Groups[3].Value;
                    }
                    else
                    {
                        // YYYY MONTH DD
                        match = new Regex("([12]\\d{3})\\D+([01]?\\d|" + MONTH_NAME_LIST + ")\\D+([0123]?\\d)", RegexOptions.IgnoreCase).Match(date);
                        if (match.Success)
                        {
                            day = match.Groups[3].Value; month = match.Groups[2].Value; year = match.Groups[1].Value;
                        }
                        else
                        {
                            // MONTH DD YYYY
                            match = new Regex("(" + MONTH_NAME_LIST + ")\\D+([0123]?\\d)\\D+([12]\\d{3})", RegexOptions.IgnoreCase).Match(date);
                            if (match.Success)
                            {
                                day = match.Groups[2].Value; month = match.Groups[1].Value; year = match.Groups[3].Value;
                            }
                            else
                            {
                                throw new DateParseException();
                            }
                        }
                    }

                    // Reconstruct standardized date format
                    date = day + "&nbsp;" + keyword(month) + "&nbsp;" + year;

                    // Detect the month
                    if (int.TryParse(month, out var monthNumber))
                    {
                        monthNumber -= 1;
                    }
                    else
                    {
                        var target = clean(month);
                        monthNumber = LOWERCASE_MONTH_NAME.IndexOf(target);
                        if (monthNumber == -1)
                        {
                            monthNumber = LOWERCASE_MONTH_FULL_NAME.IndexOf(target);
                        }
                    }

                    var dateVal = new DateTime(int.Parse(year), monthNumber, int.Parse(day), standardHour, 0, 0);
                    // Reconstruct the day of the week
                    var dayOfWeek = dateVal.DayOfWeek;
                    date = DAY_NAME[(int)dayOfWeek] + "<br/>" + date;

                    anyWeekendEvents = anyWeekendEvents || (dayOfWeek == DayOfWeek.Sunday) || (dayOfWeek == DayOfWeek.Saturday);

                    entryArray.push(new replaceScheduleListsEntry(
                        date: dateVal,
                        title: title,
                        sourceOrder: entryArray.Count,
                        parenthesized: parenthesized,

                        // Don't show text if parenthesized with no body
                        text: parenthesized
                            ? ""
                            :
                                entag("tr",
                                    entag("td",
                                        "<a " + protect("class=\"target\" name=\"schedule" + scheduleNumber + "_" + dateVal.Year
                                                        + "-" + (dateVal.Month + 1) + "-" + dateVal.Day + "\"")
                                              + ">&nbsp;</a>" + date, dateTDAttribs)
                                    + entag("td", entag("b", title))
                                    , rowAttribs
                                )
                                + entag("tr", entag("td", "\n\n" + events, eventTDAttribs), rowAttribs)
                            )
                        );

                    return "";
                });

                // Shallow copy the entries to bypass sorting if needed
                var sourceEntryArray = option.sortScheduleLists ? entryArray : entryArray.ToImmutableArray().ToList();

                // Sort by date
                entryArray.Sort((a, b) =>
                    {
                        // Javascript's sort is not specified to be
                        // stable, so we have to preserve
                        // sourceOrder in ties.
                        var ta = (int)a.date.TimeSinceEpoch();
                        var tb = (int)b.date.TimeSinceEpoch();
                        return (ta == tb) ? (a.sourceOrder - b.sourceOrder) : (ta - tb);
                    });

                var MILLISECONDS_PER_DAY = 1000 * 60 * 60 * 24;

                // May be slightly off due to daylight savings time
                var approximateDaySpan = (entryArray[entryArray.Count - 1].date.TimeSinceEpoch() - entryArray[0].date.TimeSinceEpoch()) / MILLISECONDS_PER_DAY;

                var today = DateTime.Now;
                // Move back to midnight
                today = new DateTime(today.Year, today.Month, today.Day, standardHour, 0, 0);

                var calendar = "";
                // Make a calendar view with links, if suitable
                if ((approximateDaySpan > 14) && (approximateDaySpan / entryArray.Count < 16))
                {
                    var DAY_HEADER_ATTRIBS = protect("colspan=\"2\" width=\"14%\" style=\"padding-top:5px;text-align:center;font-style:italic\"");
                    var DATE_ATTRIBS = protect("width=\"1%\" height=\"30px\" style=\"text-align:right;border:1px solid #EEE;border-right:none;\"");
                    var FADED_ATTRIBS = protect("width=\"1%\" height=\"30px\" style=\"color:#BBB;text-align:right;\"");
                    var ENTRY_ATTRIBS = protect("width=\"14%\" style=\"border:1px solid #EEE;border-left:none;\"");
                    var PARENTHESIZED_ATTRIBS = protect("class=\"parenthesized\"");

                    // Find the first day of the first month
                    var date = entryArray[0].date;
                    var index = 0;

                    var hideWeekends = !anyWeekendEvents && option.hideEmptyWeekends;
                    Func<DateTime, bool> showDate = hideWeekends
                        ? date =>
                        {
                            return date.DayOfWeek switch
                            {
                                DayOfWeek.Sunday => true,
                                DayOfWeek.Saturday => true,
                                _ => false
                            };
                        }
                        : _ => true;

                    bool sameDay(DateTime d1, DateTime d2)
                    {
                        // Account for daylight savings time
                        return (Math.Abs(d1.TimeSinceEpoch() - d2.TimeSinceEpoch()) < MILLISECONDS_PER_DAY / 2);
                    }

                    // Go to the first of the month
                    date = new DateTime(date.Year, date.Month, 1, standardHour, 0, 0);

                    while (date.TimeSinceEpoch() < entryArray[entryArray.Count - 1].date.TimeSinceEpoch())
                    {

                        // Create the calendar header
                        calendar += "<table " + protect("class=\"calendar\"") + ">\n" +
                            entag("tr", entag("th", MONTH_FULL_NAME[date.Month] + " " + date.Year, protect("colspan=\"14\""))) + "<tr>";

                        foreach (var name in (hideWeekends ? DAY_NAME.slice(1, 6) : DAY_NAME))
                        {
                            calendar += entag("td", name, DAY_HEADER_ATTRIBS);
                        }
                        calendar += "</tr>";

                        // Go back into the previous month to reach a Sunday. Check the time at noon
                        // to avoid problems with daylight saving time occurring early in the morning
                        while (date.DayOfWeek != 0)
                        {
                            date = date.Subtract(TimeSpan.FromMilliseconds(MILLISECONDS_PER_DAY));
                        }

                        // Insert the days from the previous month
                        if (date.Day != 1)
                        {
                            calendar += "<tr " + rowAttribs + ">";
                            while (date.Day != 1)
                            {
                                if (showDate(date)) { calendar += "<td " + FADED_ATTRIBS + ">" + date.Day + "</td><td>&nbsp;</td>"; }
                                date = date.Add(TimeSpan.FromMilliseconds(date.TimeSinceEpoch() + MILLISECONDS_PER_DAY));
                            }
                        }

                        // Run until the end of the month
                        do
                        {
                            if (date.DayOfWeek == DayOfWeek.Sunday)
                            {
                                // Sunday, start a row
                                calendar += "<tr " + rowAttribs + ">";
                            }

                            if (showDate(date))
                            {
                                var attribs = "";
                                if (sameDay(date, today))
                                {
                                    attribs = protect("class=\"today\"");
                                }

                                // Insert links as needed from entries
                                var contents = "";

                                for (var entry = entryArray[index]; entry != null && sameDay(entry.date, date); ++index, entry = index < entryArray.Count ? entryArray[index] : null)
                                {
                                    if (contents.Length > 0) { contents += "<br/>"; }
                                    if (entry.parenthesized)
                                    {
                                        // Parenthesized with no body, no need for a link
                                        contents += entag("span", entry.title, PARENTHESIZED_ATTRIBS);
                                    }
                                    else
                                    {
                                        contents += entag("a", entry.title, protect("href=\"#schedule" + scheduleNumber + "_" + date.Year + "-" + (date.Month + 1) + "-" + date.Day + "\""));
                                    }
                                }

                                if (contents.Length > 0)
                                {
                                    calendar += entag("td", entag("b", date.Day.ToString()), DATE_ATTRIBS + attribs) + entag("td", contents, ENTRY_ATTRIBS + attribs);
                                }
                                else
                                {
                                    calendar += "<td " + DATE_ATTRIBS + attribs + "></a>" + date.Day + "</td><td " + ENTRY_ATTRIBS + attribs + "> &nbsp; </td>";
                                }
                            }

                            if (date.DayOfWeek == DayOfWeek.Saturday)
                            {
                                // Saturday, end a row
                                calendar += "</tr>";
                            }

                            // Go to (approximately) the next day
                            date = date.Add(TimeSpan.FromMilliseconds(MILLISECONDS_PER_DAY));
                        } while (date.Day > 1);

                        // Finish out the week after the end of the month
                        if (date.DayOfWeek != 0)
                        {
                            while (date.DayOfWeek != 0)
                            {
                                if (showDate(date)) { calendar += "<td " + FADED_ATTRIBS + ">" + date.Day + "</td><td>&nbsp</td>"; }
                                date = date.Add(TimeSpan.FromMilliseconds(MILLISECONDS_PER_DAY));
                            }

                            calendar += "</tr>";
                        }

                        calendar += "</table><br/>\n";

                        // Go to the first of the (new) month
                        date = new DateTime(date.Year, date.Month, 1, standardHour, 0, 0);

                    } // Until all days covered
                } // if add calendar

                // Construct the schedule
                var sch = "";
                foreach (var entry in sourceEntryArray)
                {
                    sch += entry.text;
                }

                return "\n\n" + calendar + entag("table", sch, protect("class=\"schedule\"")) + "\n\n";
            });
        }
        catch (DateParseException)
        {
            // Maybe this wasn't a schedule after all, since we couldn't parse a date. Don't alarm
            // the user, though
        }

        return str;
    }






    /**
    Term
    :     description, which might be multiple 
        lines and include blanks.

    Next Term

    becomes

    <dl>
    <dt>Term</dt>
    <dd> description, which might be multiple 
        lines and include blanks.</dd>
    <dt>Next Term</dt>
    </dl>

    ... unless it is very short, in which case it becomes a table.

    */
    private class replaceDefinitionListsEntry
    {
        public string term { get; set; }
        public string definition { get; set; }

        public replaceDefinitionListsEntry(string term, string definition)
        {
            this.term = term;
            this.definition = definition;
        }
    };
    public string replaceDefinitionLists(string s, Func<string, string> protect)
    {
        var TERM       = @"^.+\n:(?=[ \t])";

        // Definition can contain multiple paragraphs
        var DEFINITION = @"(\s*\n|[: \t].+\n)+";

        s = s.rp(new Regex("(" + TERM + DEFINITION + ")+", RegexOptions.Multiline),
                block => {
                    
                    List<replaceDefinitionListsEntry> list = new();

                    // Parse the block
                    replaceDefinitionListsEntry? currentEntry = null;
    
                    foreach(var (lline, i) in block.Value.Split("\n").WihIndex() )
                    {
                        var line = lline;
                        // What kind of line is this?
                        if (line.Trim().Length == 0) {
                            if (currentEntry != null) {
                                // Empty line
                                currentEntry.definition += "\n";
                            }
                        } else if (! new Regex(@"\s").IsMatch(line[0].ToString()) && (line[0] != ':')) {
                            currentEntry = new (term: line, definition: "");
                            list.push(currentEntry);
                        } else {
                            // Add the line to the current definition, stripping any single leading ":"
                            if (line[0] == ':') { line = " " + line.Substring(1); }
                            if(currentEntry != null)
                                currentEntry.definition += line + "\n";
                            else
                                throw new Exception("BUG: currentTry is null");
                        }
                    }

                    var longestDefinition = 0;
                    foreach(var entry in list)
                    {
                        if (new Regex(@"\n\s*\n").IsMatch(entry.definition.Trim())) {
                            // This definition contains multiple paragraphs. Force it into long mode
                            longestDefinition = int.MaxValue;
                        } else {
                            // Normal case
                            longestDefinition = Math.Max(longestDefinition, unescapeHTMLEntities(removeHTMLTags(entry.definition)).Length);
                        }
                    };

                    var result = "";
                    var definitionStyle = option.definitionStyle;
                    if ((definitionStyle == "short") || ((definitionStyle != "long") && (longestDefinition < 160))) {
                        var rowAttribs = protect("valign=top");
                        // This list has short definitions. Format it as a table
                        foreach(var entry in list)
                        {
                            result += entag("tr",
                                            entag("td", entag("dt", entry.term)) + 
                                            entag("td", entag("dd", entag("p", entry.definition))), 
                                            rowAttribs);
                        }
                        result = entag("table", result);

                    } else {
                        foreach(var entry in list)
                        {
                            // Leave *two* blanks at the start of a
                            // definition so that subsequent processing
                            // can detect block formatting within it.
                            result += entag("dt", entry.term) + entag("dd", entag("p", entry.definition));
                        }
                    }

                    return entag("dl", result);

                });

        return s;
    }





private static string escapeRegExpCharacters(string str)
{
    return Regex.Escape(str);
}




/** Converts <>&" to their HTML escape sequences */
static string escapeHTMLEntities(string str) {
    return str
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        ;
}





/** 
    Find the specified delimiterRegExp used as a quote (e.g., *foo*)
    and replace it with the HTML tag and optional attributes.
*/
string replaceMatched(string str, string delimiterRegExp, string tag, string? attribs)
{
    var delimiter = delimiterRegExp;
    var flanking = "[^ \\t\\n" + delimiter + "]";
    var pattern  = "([^A-Za-z0-9])(" + delimiter + ")" +
        "(" + flanking + ".*?(\\n.+?)*?)" + 
        delimiter + "(?![A-Za-z0-9])";

    return str.rp(new Regex(pattern), match =>
                          match.Groups[1].Value+ "<" + tag + (attribs!=null ? " " + attribs : "") +
                          ">"+ match.Groups[3].Value + "</" + tag + ">");
}
    









    /** Inserts a table of contents in the document and then returns
        [string, table], where the table maps strings to levels. */
    public (string, Dictionary<string, string>) insertTableOfContents(string s, Func<string, string> protect, Func<string, string> exposer)
    {
        // Gather headers for table of contents (TOC). We
        // accumulate a long and short TOC and then choose which
        // to insert at the end.
        var fullTOC = "<a href=\"#\" class=\"tocTop\">(Top)</a><br/>\n";
        var shortTOC = "";

        // names of parent sections
        List<string> nameStack = new();
        
        // headerCounter[i] is the current counter for header level (i - 1)
        var headerCounter = new List<int>{0};
        var currentLevel = 0;
        var numAboveLevel1 = 0;

        var table = new Dictionary<string, string>();
        s = s.rp(new Regex(@"<h([1-6])>(.*?)<\/h\1>", RegexOptions.IgnoreCase), header => {
            var level = int.Parse(header.Groups[1].Value);
            var text = header.Groups[2].Value.Trim();
            
            // If becoming more nested:
            for (var i = currentLevel; i < level; ++i) {
                nameStack[i] = "";
                headerCounter[i] = 0;
            }
            
            // If becoming less nested:
            headerCounter.splice(level, currentLevel - level);
            nameStack.splice(level, currentLevel - level);
            currentLevel = level;

            ++headerCounter[currentLevel - 1];
            
            // Generate a unique name for this element
            var number = string.Join(".", headerCounter);

            // legacy, for when toc links were based on
            // numbers instead of mangled names
            var oldname = "toc" + number;

            var cleanText = removeHTMLTags(exposer(text)).Trim().ToLowerInvariant();
            
            table[cleanText] = number;

            // Remove links from the title itself
            text = text.rp(new Regex(@"<a\s.*>(.*?)<\/a>"), x => x.Groups[1].Value);

            nameStack[currentLevel - 1] = mangle(cleanText);

            var name = string.Join("/", nameStack);

            // Only insert for the first three levels
            if (level <= 3) {
                fullTOC += "&nbsp;&nbsp;".Repeat(level-1) + "<a href=\"#" + name
                    + "\" class=\"level" + level + "\"><span class=\"tocNumber\">" + number + "&nbsp; </span>" + text + "</a><br/>\n";
                
                if (level == 1) {
                    shortTOC += " &middot; <a href=\"#" + name + "\">" + text + "</a>";
                } else {
                    ++numAboveLevel1;
                }
            }

            return entag("a", "&nbsp;", protect("class=\"target\" name=\"" + name + "\"")) +
                entag("a", "&nbsp;", protect("class=\"target\" name=\"" + oldname + "\"")) +
                header;
        });

        if (shortTOC.Length > 0) {
            // Strip the leading " &middot; "
            shortTOC = shortTOC.Substring(10);
        }
        
        var numLevel1 = headerCounter[0];
        var numHeaders = numLevel1 + numAboveLevel1;

        // The location of the first header is indicative of the Length of
        // the abstract...as well as where we insert. The first header may be accompanied by
        // <a name> tags, which we want to appear before.
        var firstHeaderLocation = new Regex(@"((<a\s+\S+>&nbsp;<\/a>)\s*)*?<h\d>").Match(s).Index;
        if (firstHeaderLocation == -1) { firstHeaderLocation = 0; }

        var AFTER_TITLES = "<div class=\"afterTitles\"></div>";
        var insertLocation = s.IndexOf(AFTER_TITLES);
        if (insertLocation == -1) {
            insertLocation = 0;
        } else {
            insertLocation += AFTER_TITLES.Length;
        }

        // Which TOC style should we use?
        var tocStyle = option.tocStyle;

        var TOC = "";
        if (tocStyle == TocStyle.auto)
        {
            if (((numHeaders < 4) && (numLevel1 <= 1)) || (s.Length < 2048)) {
                // No TOC; this document is really short
                tocStyle = TocStyle.none;
            } else if ((numLevel1 < 7) && (numHeaders / numLevel1 < 2.5)) {
                // We can use the short TOC
                tocStyle = TocStyle.kshort;
            } else if ((firstHeaderLocation == -1) || (firstHeaderLocation / 55 > numHeaders)) {
                // The abstract is long enough to float alongside, and there
                // are not too many levels.        
                // Insert the medium-Length TOC floating
                tocStyle = TocStyle.medium;
            } else {
                // This is a long table of contents or a short abstract
                // Insert a long toc...right before the first header
                tocStyle = TocStyle.klong;
            }
        }

        switch (tocStyle) {
        case TocStyle.none:
            break;

        case TocStyle.kshort:
            TOC = "<div class=\"shortTOC\">" + shortTOC + "</div>";
            break;

        case TocStyle.medium:
            TOC = "<div class=\"mediumTOC\"><center><b>" + keyword("Contents") + "</b></center><p>" + fullTOC + "</p></div>";
            break;

        case TocStyle.klong:
            insertLocation = firstHeaderLocation;
            TOC = "<div class=\"longTOC\"><div class=\"tocHeader\">" + keyword("Contents") + "</div><p>" + fullTOC + "</p></div>";
            break;

        default:
            throw new Exception("markdeepOptions.tocStyle = \"" + tocStyle + "\" specified in your document is not a legal value");
        }

        s = s.ss(0, insertLocation) + TOC + s.Substring(insertLocation);

        return (s, table);
    }









    /** Returns true if there are at least two newlines in each of the arguments */
    static bool isolated(string? preSpaces, string? postSpaces)
    {
        if (preSpaces!=null && postSpaces!=null)
        {
            var newlineRegex = new Regex(@"\n");
            return newlineRegex.Matches(preSpaces).Count > 1 && newlineRegex.Matches(postSpaces).Count > 1;
        }
        else
        {
            return false;
        }
    }



    /** Workaround for IE11 */
    static char[] strToArray(string s)
    {
        return s.ToCharArray();
    }

    /**
    Adds whitespace at the end of each line of str, so that all lines have equal Length in
    unicode characters (which is not the same as JavaScript characters when high-index/escape
    characters are present).
    */
    // todo(Gustav): test this
    static string equalizeLineLengths(string str) {
        var lineArray = str.Split('\n').ToList();

        if ((lineArray.Count > 0) && (lineArray[lineArray.Count - 1] == "")) {
            // Remove the empty last line generated by split on a trailing newline
            lineArray.pop();
        }

        var longest = lineArray.Max(line => line.Length);

        // Worst case spaces needed for equalizing Lengths
        var spaces = new string(' ', longest + 1);

        var result = "";
        foreach(var line in lineArray)
        {
            // Append the needed number of spaces onto each line, and
            // reconstruct the output with newlines
            result += line + spaces.Substring(line.Length) + '\n';
        }

        return result;
    }







    /** Returns true if this character is a "letter" under the ASCII definition */
    static bool isASCIILetter(char c)
    {
        var code = (int) c;
        return ((code >= 65) && (code <= 90)) || ((code >= 97) && (code <= 122));
    }


    /** Invoke as new Vec2(v) to clone or new Vec2(x, y) to create from coordinates.
        Can also invoke without new for brevity. */

    /** Pixels per character */
    const int SCALE   = 8;

    /** Multiply Y coordinates by this when generating the final SVG
        result to account for the aspect ratio of text files. This
        MUST be 2 */
    const int ASPECT = 2;





    const double EPSILON = 1e-6;

    // The order of the following is based on rotation angles
    // and is used for ArrowSet.toSVG
    const string ARROW_HEAD_CHARACTERS            = ">v<^";
    const string POINT_CHARACTERS                 = "o*◌○◍●";
    const string JUMP_CHARACTERS                  = "()";
    const string UNDIRECTED_VERTEX_CHARACTERS     = "+";
    const string VERTEX_CHARACTERS                = UNDIRECTED_VERTEX_CHARACTERS + ".'";

    // GRAY[i] is the Unicode block character for (i+1)/4 level gray
    const string GRAY_CHARACTERS = "\u2591\u2592\u2593\u2588";

    // TRI[i] is a right-triangle rotated by 90*i
    const string TRI_CHARACTERS  = "\u25E2\u25E3\u25E4\u25E5";

    const string DECORATION_CHARACTERS            = ARROW_HEAD_CHARACTERS + POINT_CHARACTERS + JUMP_CHARACTERS + GRAY_CHARACTERS + TRI_CHARACTERS;

    static bool isUndirectedVertex(char c) { return UNDIRECTED_VERTEX_CHARACTERS.Contains(c); }
    static bool isVertex(char c)           { return VERTEX_CHARACTERS.Contains(c); }
    static bool isTopVertex(char c)        { return isUndirectedVertex(c) || (c == '.'); }
    static bool isBottomVertex(char c)     { return isUndirectedVertex(c) || (c == '\''); }
    static bool isVertexOrLeftDecoration(char c){ return isVertex(c) || (c == '<') || isPoint(c); }
    static bool isVertexOrRightDecoration(char c){return isVertex(c) || (c == '>') || isPoint(c); }
    static bool isArrowHead(char c)        { return ARROW_HEAD_CHARACTERS.Contains(c); }
    static bool isGray(char c)             { return GRAY_CHARACTERS.Contains(c); }
    static bool isTri(char c)              { return TRI_CHARACTERS.Contains(c); }

    // "D" = Diagonal slash (/), "B" = diagonal Backslash (\)
    // Characters that may appear anywhere on a solid line
    static bool isSolidHLine(char c)       { return (c == '-') || isUndirectedVertex(c) || isJump(c); }
    static bool isSolidVLineOrJumpOrPoint(char c) { return isSolidVLine(c) || isJump(c) || isPoint(c); }
    static bool isSolidVLine(char c)       { return (c == '|') || isUndirectedVertex(c); }
    static bool isSolidDLine(char c)       { return (c == '/') || isUndirectedVertex(c); }
    static bool isSolidBLine(char c)       { return (c == '\\') || isUndirectedVertex(c); }
    static bool isJump(char c)             { return JUMP_CHARACTERS.Contains(c); }
    static bool isPoint(char c)            { return POINT_CHARACTERS.Contains(c); }
    static bool isDecoration(char c)       { return DECORATION_CHARACTERS.Contains(c); }
    static bool isEmpty(char c)            { return c == ' '; }


















    ///////////////////////////////////////////////////////////////////////////////
    // Math library





    public class Vec2
    {
        public double x { get; set; }
        public double y { get; set; }


        public Vec2(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        public Vec2()
        {
            this.x = 0;
            this.y = 0;
        }

        public Vec2(Vec2 rhs)
        {
            this.x = rhs.x;
            this.y = rhs.y;
        }

        public string toSVG() { return "" + (this.x* SCALE) + "," + (this.y* SCALE * ASPECT) + " "; }
        public override string ToString()
        {
            return toSVG();
        }
    }




    public class Grid
    {
        // Elements are true when consumed
        private List<bool> _used = new();

        private readonly char[] str;
        public int width { get; init; }
        public int height { get; init; }

        /** Converts a "rectangular" string defined by newlines into 2D
            array of characters. Grids are immutable. */
        public Grid(string str)
        {
            this.height  = str.Split('\n').Length;
            if (str[str.Length - 1] == '\n') { --this.height; }

            // Convert the string to an array to better handle greater-than 16-bit unicode
            // characters, which JavaScript does not process correctly with indices. Do this after
            // the above string processing.
            this.str = strToArray(str);
            this.width = this.str.IndexOf('\n');
        }

        /** Mark this location. Takes a Vec2 or (x, y) */
        public void setUsed(Vec2 v) { setUsed(v.x, v.y); }
        public void setUsed(double x, double y)
        {
            if ((x >= 0) && (x < this.width) && (y >= 0) && (y < this.height))
            {
                // Match the source string indexing
                this._used[(int)y * (this.width + 1) + (int)x] = true;
            }
        }

        /** Returns ' ' for out of bounds values */
        public char grid(Vec2 v) { return grid(v.x, v.y); }
        public char grid(double x, double y)
        {
            return ((x >= 0) && (x < this.width) && (y >= 0) && (y < this.height))
                ? str[(int)y * (this.width + 1) + (int)x]
                : ' '
                ;
        }
        
        public bool isUsed(Vec2 v) { return isUsed(v.x, v.y); }
        public bool isUsed(double x, double y)
        {
            return (this._used[(int)y * (this.width + 1) + (int)x] == true);
        }
        
        /** Returns true if there is a solid vertical line passing through (x, y) */
        public bool isSolidVLineAt(Vec2 v) { return isSolidVLineAt(v.x, v.y); }
        public bool isSolidVLineAt(double x, double y)
        {
            var up = grid(x, y - 1);
            var c  = grid(x, y);
            var dn = grid(x, y + 1);
            
            var uprt = grid(x + 1, y - 1);
            var uplt = grid(x - 1, y - 1);
            
            if (isSolidVLine(c)) {
                // Looks like a vertical line...does it continue?
                return (isTopVertex(up)    || (up == '^') || isSolidVLine(up) || isJump(up) ||
                        isBottomVertex(dn) || (dn == 'v') || isSolidVLine(dn) || isJump(dn) ||
                        isPoint(up) || isPoint(dn) || (grid(x, y - 1) == '_') || (uplt == '_') ||
                        (uprt == '_') ||
                        
                        // Special case of 1-high vertical on two curved corners 
                        ((isTopVertex(uplt) || isTopVertex(uprt)) &&
                        (isBottomVertex(grid(x - 1, y + 1)) || isBottomVertex(grid(x + 1, y + 1)))));
                
            } else if (isTopVertex(c) || (c == '^')) {
                // May be the top of a vertical line
                return isSolidVLine(dn) || (isJump(dn) && (c != '.'));
            } else if (isBottomVertex(c) || (c == 'v')) {
                return isSolidVLine(up) || (isJump(up) && (c != '\''));
            } else if (isPoint(c)) {
                return isSolidVLine(up) || isSolidVLine(dn);
            } 
            
            return false;
        }
    
    
        /** Returns true if there is a solid middle (---) horizontal line
            passing through (x, y). Ignores underscores. */
        public bool isSolidHLineAt(Vec2 v) { return isSolidHLineAt(v.x, v.y); }
        public bool isSolidHLineAt(double x, double y)
        {
            var ltlt = grid(x - 2, y);
            var lt   = grid(x - 1, y);
            var c    = grid(x + 0, y);
            var rt   = grid(x + 1, y);
            var rtrt = grid(x + 2, y);
            
            if (isSolidHLine(c) || (isSolidHLine(lt) && isJump(c))) {
                // Looks like a horizontal line...does it continue? We need three in a row.
                if (isSolidHLine(lt)) {
                    return isSolidHLine(rt) || isVertexOrRightDecoration(rt) || 
                        isSolidHLine(ltlt) || isVertexOrLeftDecoration(ltlt);
                } else if (isVertexOrLeftDecoration(lt)) {
                    return isSolidHLine(rt);
                } else {
                    return isSolidHLine(rt) && (isSolidHLine(rtrt) || isVertexOrRightDecoration(rtrt));
                }

            } else if (c == '<') {
                return isSolidHLine(rt) && isSolidHLine(rtrt);
                
            } else if (c == '>') {
                return isSolidHLine(lt) && isSolidHLine(ltlt);
                
            } else if (isVertex(c)) {
                return ((isSolidHLine(lt) && isSolidHLine(ltlt)) || 
                        (isSolidHLine(rt) && isSolidHLine(rtrt)));
            }
            
            return false;
        }
        
        
        /** Returns true if there is a solid backslash line passing through (x, y) */
        public bool isSolidBLineAt(Vec2 v) { return isSolidBLineAt(v.x, v.y); }
        public bool isSolidBLineAt(double x, double y) 
        {
            var c = grid(x, y);
            var lt = grid(x - 1, y - 1);
            var rt = grid(x + 1, y + 1);
            
            if (c == '\\') {
                // Looks like a diagonal line...does it continue? We need two in a row.
                return (isSolidBLine(rt) || isBottomVertex(rt) || isPoint(rt) || (rt == 'v') ||
                        isSolidBLine(lt) || isTopVertex(lt) || isPoint(lt) || (lt == '^') ||
                        (grid(x, y - 1) == '/') || (grid(x, y + 1) == '/') || (rt == '_') || (lt == '_')); 
            } else if (c == '.') {
                return (rt == '\\');
            } else if (c == '\'') {
                return (lt == '\\');
            } else if (c == '^') {
                return rt == '\\';
            } else if (c == 'v') {
                return lt == '\\';
            } else if (isVertex(c) || isPoint(c) || (c == '|')) {
                return isSolidBLine(lt) || isSolidBLine(rt);
            }
            else
            {
                // throw new Exception($"unhandled char {c} in diagram");
                return false;
            }
        }
        

        /** Returns true if there is a solid diagonal line passing through (x, y) */
        public bool isSolidDLineAt(Vec2 v) { return isSolidDLineAt(v.x, v.y); }
        public bool isSolidDLineAt(double x, double y)
        {
            var c = grid(x, y);
            var lt = grid(x - 1, y + 1);
            var rt = grid(x + 1, y - 1);
            
            if (c == '/' && ((grid(x, y - 1) == '\\') || (grid(x, y + 1) == '\\'))) {
                // Special case of tiny hexagon corner
                return true;
            } else if (isSolidDLine(c)) {
                // Looks like a diagonal line...does it continue? We need two in a row.
                return (isSolidDLine(rt) || isTopVertex(rt) || isPoint(rt) || (rt == '^') || (rt == '_') ||
                        isSolidDLine(lt) || isBottomVertex(lt) || isPoint(lt) || (lt == 'v') || (lt == '_')); 
            } else if (c == '.') {
                return (lt == '/');
            } else if (c == '\'') {
                return (rt == '/');
            } else if (c == '^') {
                return lt == '/';
            } else if (c == 'v') {
                return rt == '/';
            } else if (isVertex(c) || isPoint(c) || (c == '|')) {
                return isSolidDLine(lt) || isSolidDLine(rt);
            }
            return false;
        }

        public override string ToString()
        {
            return str.ToString() ?? string.Empty;
        }
    }
        























    class Path
    {
        public Vec2 A {get; init;}
        public Vec2 B {get; init;}
        public Vec2? C {get; init;}
        public Vec2? D {get; init;}
        public bool dashed { get; init; }

        /** A 1D curve. If C is specified, the result is a bezier with
            that as the tangent control point */
        public Path(Vec2 A, Vec2 B, Vec2? C = null, Vec2? D = null, bool dashed = false)
        {
            this.A = A;
            this.B = B;
            if (C != null) {
                this.C = C;
                if (D != null) {
                    this.D = D;
                } else {
                    this.D = C;
                }
            }

            this.dashed = dashed;
        }

        public bool isVertical()
        {
            return this.B.x == this.A.x;
        }

        public bool isHorizontal()
        {
            return this.B.y == this.A.y;
        }

        /** Diagonal lines look like: / See also backDiagonal */
        public bool isDiagonal()
        {
            var dx = this.B.x - this.A.x;
            var dy = this.B.y - this.A.y;
            return (Math.Abs(dy + dx) < EPSILON);
        }

        public bool isBackDiagonal()
        {
            var dx = this.B.x - this.A.x;
            var dy = this.B.y - this.A.y;
            return (Math.Abs(dy - dx) < EPSILON);
        }

        public bool isCurved()
        {
            return this.C != null;
        }

        /** Does this path have any end at (x, y) */
        public bool endsAt(Vec2 v) { return endsAt(v.x, v.y); }
        public bool endsAt(double x, double y)
        {
            return ((this.A.x == x) && (this.A.y == y)) ||
                ((this.B.x == x) && (this.B.y == y));
        }

        /** Does this path have an up end at (x, y) */
        public bool upEndsAt(Vec2 v) { return upEndsAt(v.x, v.y); }
        public bool upEndsAt(double x, double y)
        {
            return this.isVertical() && (this.A.x == x) && (Math.Min(this.A.y, this.B.y) == y);
        }

        /** Does this path have an up end at (x, y) */
        public bool diagonalUpEndsAt(Vec2 v) { return diagonalUpEndsAt(v.x, v.y); }
        public bool diagonalUpEndsAt(double x, double y)
        {
            if (! this.isDiagonal()) { return false; }
            if (this.A.y < this.B.y) {
                return (this.A.x == x) && (this.A.y == y);
            } else {
                return (this.B.x == x) && (this.B.y == y);
            }
        }

        /** Does this path have a down end at (x, y) */
        public bool diagonalDownEndsAt(Vec2 v) { return diagonalDownEndsAt(v.x, v.y); }
        public bool diagonalDownEndsAt(double x, double y)
        {
            if (! this.isDiagonal()) { return false; }
            if (this.B.y < this.A.y) {
                return (this.A.x == x) && (this.A.y == y);
            } else {
                return (this.B.x == x) && (this.B.y == y);
            }
        }

        /** Does this path have an up end at (x, y) */
        public bool backDiagonalUpEndsAt(Vec2 v) { return backDiagonalUpEndsAt(v.x, v.y); }
        public bool backDiagonalUpEndsAt(double x, double y)
        {
            if (! this.isBackDiagonal()) { return false; }
            if (this.A.y < this.B.y) {
                return (this.A.x == x) && (this.A.y == y);
            } else {
                return (this.B.x == x) && (this.B.y == y);
            }
        }

        /** Does this path have a down end at (x, y) */
        public bool backDiagonalDownEndsAt(Vec2 v) { return backDiagonalDownEndsAt(v.x, v.y); }
        public bool backDiagonalDownEndsAt(double x, double y)
        {
            if (! this.isBackDiagonal()) { return false; }
            if (this.B.y < this.A.y) {
                return (this.A.x == x) && (this.A.y == y);
            } else {
                return (this.B.x == x) && (this.B.y == y);
            }
        }

        /** Does this path have a down end at (x, y) */
        public bool downEndsAt(Vec2 v) { return downEndsAt(v.x, v.y); }
        public bool downEndsAt(double x, double y)
        {
            return this.isVertical() && (this.A.x == x) && (Math.Max(this.A.y, this.B.y) == y);
        }

        /** Does this path have a left end at (x, y) */
        public bool leftEndsAt(Vec2 v) { return leftEndsAt(v.x, v.y); }
        public bool leftEndsAt(double x, double y)
        {
            return this.isHorizontal() && (this.A.y == y) && (Math.Min(this.A.x, this.B.x) == x);
        }

        /** Does this path have a right end at (x, y) */
        public bool rightEndsAt(Vec2 v) { return rightEndsAt(v.x, v.y); }
        public bool rightEndsAt(double x, double y)
        {
            return this.isHorizontal() && (this.A.y == y) && (Math.Max(this.A.x, this.B.x) == x);
        }

        public bool verticalPassesThrough(Vec2 v) { return verticalPassesThrough(v.x, v.y); }
        public bool verticalPassesThrough(double x, double y)
        {
            return this.isVertical() && 
                (this.A.x == x) && 
                (Math.Min(this.A.y, this.B.y) <= y) &&
                (Math.Max(this.A.y, this.B.y) >= y);
        }

        public bool horizontalPassesThrough(Vec2 v) { return horizontalPassesThrough(v.x, v.y); }
        public bool horizontalPassesThrough(double x, double y)
        {
            return this.isHorizontal() && 
                (this.A.y == y) && 
                (Math.Min(this.A.x, this.B.x) <= x) &&
                (Math.Max(this.A.x, this.B.x) >= x);
        }
        
        /** Returns a string suitable for inclusion in an SVG tag */
        public string toSVG()
        {
            var svg = "<path d=\"M " + this.A;

            if (this.isCurved()) {
                svg += "C " + this.C + this.D + this.B;
            } else {
                svg += "L " + this.B;
            }
            svg += "\" style=\"fill:none;\"";
            if (this.dashed) {
                svg += " stroke-dasharray=\"3,6\"";
            }
            svg += "/>";
            return svg;
        }
    }


























    /** A group of 1D curves. This was designed so that all of the
        methods can later be implemented in O(1) time, but it
        currently uses O(n) implementations for source code
        simplicity. */
    class PathSet
    {
        private List<Path> _pathArray = new();
        
        public PathSet() {}

        public void insert(Path path)
        {
            this._pathArray.push(path);
        }

        /** Returns true if method(x, y) 
            returns true on any element of _pathAray */
        public bool upEndsAt               (double x, double y) => _pathArray.Any(p => p.upEndsAt(x, y));
        public bool diagonalUpEndsAt       (double x, double y) => _pathArray.Any(p => p.diagonalUpEndsAt(x, y));
        public bool backDiagonalUpEndsAt   (double x, double y) => _pathArray.Any(p => p.backDiagonalUpEndsAt(x, y));
        public bool diagonalDownEndsAt     (double x, double y) => _pathArray.Any(p => p.diagonalDownEndsAt(x, y));
        public bool backDiagonalDownEndsAt (double x, double y) => _pathArray.Any(p => p.backDiagonalDownEndsAt(x, y));
        public bool downEndsAt             (double x, double y) => _pathArray.Any(p => p.downEndsAt(x, y));
        public bool leftEndsAt             (double x, double y) => _pathArray.Any(p => p.leftEndsAt(x, y));
        public bool rightEndsAt            (double x, double y) => _pathArray.Any(p => p.rightEndsAt(x, y));
        public bool endsAt                 (double x, double y) => _pathArray.Any(p => p.endsAt(x, y));
        public bool verticalPassesThrough  (double x, double y) => _pathArray.Any(p => p.verticalPassesThrough(x, y));
        public bool horizontalPassesThrough(double x, double y) => _pathArray.Any(p => horizontalPassesThrough(x, y));
        

        /** Returns an SVG string */
        public string toSVG()
        {
            var svg = "";
            for (var i = 0; i < this._pathArray.Count; ++i) {
                svg += this._pathArray[i].toSVG() + "\n";
            }
            return svg;
        }
    }


    record DecorationSetEntry(Markdeep.Vec2 C, char type, double angle);
    class DecorationSet
    {
        private List<DecorationSetEntry> _decorationArray = new();
        public DecorationSet()
        {
        }

        /** insert(x, y, type, <angle>)  
            insert(vec, type, <angle>)

            angle is the angle in degrees to rotate the result */
        public void insert(double x, double y, char type, double angle = 0)
        {
            // if (type == undefined) { type = y; y = x.y; x = x.x; }

            if (! isDecoration(type)) {
                throw new Exception("Illegal decoration character: " + type); 
            }
            var d = new DecorationSetEntry(C: new Vec2(x, y), type: type, angle:angle);

            // Put arrows at the front and points at the back so that
            // arrows always draw under points

            if (isPoint(type)) {
                this._decorationArray.push(d);
            } else {
                this._decorationArray.unshift(d);
            }
        }


        public string toSVG()
        {
            var svg = "";
            for (var i = 0; i < this._decorationArray.Count; ++i) {
                var decoration = this._decorationArray[i];
                var C = decoration.C;
                
                if (isJump(decoration.type)) {
                    // Slide jumps
                    var dx = (decoration.type == ')') ? +0.75 : -0.75;
                    var up  = new Vec2(C.x, C.y - 0.5);
                    var dn  = new Vec2(C.x, C.y + 0.5);
                    var cup = new Vec2(C.x + dx, C.y - 0.5);
                    var cdn = new Vec2(C.x + dx, C.y + 0.5);

                    svg += "<path d=\"M " + dn + " C " + cdn + cup + up + "\" style=\"fill:none;\"/>";

                } else if (isPoint(decoration.type)) {
                    var cls = decoration.type switch { '*' => "closed", 'o' => "open", '◌' => "dotted", '○' => "open", '◍' => "shaded", '●' => "closed", _ => throw new Exception("invalid type")};
                    svg += "<circle cx=\"" + (C.x * SCALE) + "\" cy=\"" + (C.y * SCALE * ASPECT) +
                        "\" r=\"" + (SCALE - STROKE_WIDTH) + "\" class=\"" + cls + "dot\"/>";
                } else if (isGray(decoration.type)) {
                    
                    var shade = Math.Round((3 - GRAY_CHARACTERS.IndexOf(decoration.type)) * 63.75);
                    svg += "<rect x=\"" + ((C.x - 0.5) * SCALE) + "\" y=\"" + ((C.y - 0.5) * SCALE * ASPECT) + "\" width=\"" + SCALE + "\" height=\"" + (SCALE * ASPECT) + "\" stroke=\"none\" fill=\"rgb(" + shade + "," + shade + "," + shade +")\"/>";

                } else if (isTri(decoration.type)) {
                    // 30-60-90 triangle
                    var index = TRI_CHARACTERS.IndexOf(decoration.type);
                    var xs  = 0.5 - (index & 1);
                    var ys  = 0.5 - (index >> 1);
                    xs *= Math.Sign(ys);
                    var tip = new Vec2(C.x + xs, C.y - ys);
                    var up  = new Vec2(C.x + xs, C.y + ys);
                    var dn  = new Vec2(C.x - xs, C.y + ys);
                    svg += "<polygon points=\"" + tip + up + dn + "\" style=\"stroke:none\"/>\n";
                } else { // Arrow head
                    var tip = new Vec2(C.x + 1, C.y);
                    var up =  new Vec2(C.x - 0.5, C.y - 0.35);
                    var dn =  new Vec2(C.x - 0.5, C.y + 0.35);
                    svg += "<polygon points=\"" + tip + up + dn + 
                        "\"  style=\"stroke:none\" transform=\"rotate(" + decoration.angle + "," + C + ")\"/>\n";
                }
            }
            return svg;
        }
    }








































    /** Converts diagramString, which is a Markdeep diagram without the surrounding asterisks, to
        SVG (HTML). Lines may have ragged Lengths.

        alignmentHint is the float alignment desired for the SVG tag,
        which can be 'floatleft', 'floatright', or ''
    */
    string diagramToSVG(string adiagramString, AlignmentHint alignmentHint) {
        // Clean up diagramString if line endings are ragged
        var diagramString = equalizeLineLengths(adiagramString);

        // Temporarily replace 'o' that is surrounded by other text
        // with another character to avoid processing it as a point 
        // decoration. This will be replaced in the final svg and is
        // faster than checking each neighborhood each time.
        const char HIDE_O = '\ue004';
        diagramString = diagramString.rp(new Regex(@"([a-zA-Z]{2})o"), m => m.Groups[1].Value + HIDE_O);
        diagramString = diagramString.rp(new Regex(@"o([a-zA-Z]{2})"), m => HIDE_O + m.Groups[1].Value);
        diagramString = diagramString.rp(new Regex(@"([a-zA-Z\ue004])o([a-zA-Z\ue004])"), m=> m.Groups[1].Value + HIDE_O + m.Groups[2].Value);

        var DIAGONAL_ANGLE = Math.Atan(1.0 / ASPECT) * 180 / Math.PI;
    
        

        ////////////////////////////////////////////////////////////////////////////

        void findPaths(Grid grid, PathSet pathSet) {
            // Does the line from A to B contain at least one c?
            bool lineContains(Vec2 A, Vec2 B, char c)
            {
                var dx = Math.Sign(B.x - A.x);
                var dy = Math.Sign(B.y - A.y);
                double x, y;

                for (x = A.x, y = A.y; (x != B.x) || (y != B.y); x += dx, y += dy) {
                    if (grid.grid(x, y) == c) { return true; }
                }

                // Last point
                return (grid.grid(x, y) == c);
            }

            // Find all solid vertical lines. Iterate horizontally
            // so that we never hit the same line twice
            for (var x = 0; x < grid.width; ++x) {
                for (var y = 0; y < grid.height; ++y) {
                    if (grid.isSolidVLineAt(x, y)) {
                        // This character begins a vertical line...now, find the end
                        var A = new Vec2(x, y);
                        do  { grid.setUsed(x, y); ++y; } while (grid.isSolidVLineAt(x, y));
                        var B = new Vec2(x, y - 1);
                        
                        var up = grid.grid(A);
                        var upup = grid.grid(A.x, A.y - 1);

                        if (! isVertex(up) && ((upup == '-') || (upup == '_') ||
                                            (upup == '┳') ||
                                            (grid.grid(A.x - 1, A.y - 1) == '_') ||
                                            (grid.grid(A.x + 1, A.y - 1) == '_') || 
                                            isBottomVertex(upup)) || isJump(upup)) {
                            // Stretch up to almost reach the line above (if there is a decoration,
                            // it will finish the gap)
                            A.y -= 0.5;
                        }

                        var dn = grid.grid(B);
                        var dndn = grid.grid(B.x, B.y + 1);
                        if (! isVertex(dn) && ((dndn == '-') || (dndn == '┻') || isTopVertex(dndn)) || isJump(dndn) ||
                            (grid.grid(B.x - 1, B.y) == '_') || (grid.grid(B.x + 1, B.y) == '_')) {
                            // Stretch down to almost reach the line below
                            B.y += 0.5;
                        }

                        // Don't insert degenerate lines
                        if ((A.x != B.x) || (A.y != B.y)) {
                            pathSet.insert(new Path(A, B));
                        }

                        // Continue the search from the end value y+1
                    } 

                    // Some very special patterns for the short lines needed on
                    // circuit diagrams. Only invoke these if not also on a curve
                    //      _  _    
                    //    -'    '-   -'
                    else if ((grid.grid(x, y) == '\'') &&
                        (((grid.grid(x - 1, y) == '-') && (grid.grid(x + 1, y - 1) == '_') &&
                        ! isSolidVLineOrJumpOrPoint(grid.grid(x - 1, y - 1))) ||
                        ((grid.grid(x - 1, y - 1) == '_') && (grid.grid(x + 1, y) == '-') &&
                        ! isSolidVLineOrJumpOrPoint(grid.grid(x + 1, y - 1))))) {
                        pathSet.insert(new Path(new Vec2(x, y - 0.5), new Vec2(x, y)));
                    }

                    //    _.-  -._  
                    else if ((grid.grid(x, y) == '.') &&
                            (((grid.grid(x - 1, y) == '_') && (grid.grid(x + 1, y) == '-') && 
                            ! isSolidVLineOrJumpOrPoint(grid.grid(x + 1, y + 1))) ||
                            ((grid.grid(x - 1, y) == '-') && (grid.grid(x + 1, y) == '_') &&
                            ! isSolidVLineOrJumpOrPoint(grid.grid(x - 1, y + 1))))) {
                        pathSet.insert(new Path(new Vec2(x, y), new Vec2(x, y + 0.5)));
                    }

                    // For drawing resistors: -.╱
                    else if ((grid.grid(x, y) == '.') &&
                            (grid.grid(x - 1, y) == '-') &&
                            (grid.grid(x + 1, y) == '╱')) {
                        pathSet.insert(new Path(new Vec2(x, y), new Vec2(x + 0.5, y + 0.5)));
                    }
                    
                    // For drawing resistors: ╱'-
                    else if ((grid.grid(x, y) == '\'') &&
                            (grid.grid(x + 1, y) == '-') &&
                            (grid.grid(x - 1, y) == '╱')) {
                        pathSet.insert(new Path(new Vec2(x, y), new Vec2(x - 0.5, y - 0.5)));
                    }

                } // y
            } // x
            
            // Find all solid horizontal lines 
            for (var y = 0; y < grid.height; ++y) {
                for (var x = 0; x < grid.width; ++x) {
                    if (grid.isSolidHLineAt(x, y)) {
                        // Begins a line...find the end
                        var A = new Vec2(x, y);
                        do { grid.setUsed(x, y); ++x; } while (grid.isSolidHLineAt(x, y));
                        var B = new Vec2(x - 1, y);

                        // Detect adjacent box-drawing characters and Lengthen the edge
                        if (grid.grid(B.x + 1, B.y) == '┫') { B.x += 0.5; }
                        if (grid.grid(A.x - 1, A.y) == '┣') { A.x -= 0.5; }

                        // Detect curves and shorten the edge
                        if ( ! isVertex(grid.grid(A.x - 1, A.y)) && 
                            ((isTopVertex(grid.grid(A)) && isSolidVLineOrJumpOrPoint(grid.grid(A.x - 1, A.y + 1))) ||
                            (isBottomVertex(grid.grid(A)) && isSolidVLineOrJumpOrPoint(grid.grid(A.x - 1, A.y - 1))))) {
                            ++A.x;
                        }

                        if ( ! isVertex(grid.grid(B.x + 1, B.y)) && 
                            ((isTopVertex(grid.grid(B)) && isSolidVLineOrJumpOrPoint(grid.grid(B.x + 1, B.y + 1))) ||
                            (isBottomVertex(grid.grid(B)) && isSolidVLineOrJumpOrPoint(grid.grid(B.x + 1, B.y - 1))))) {
                            --B.x;
                        }

                        // Only insert non-degenerate lines
                        if ((A.x != B.x) || (A.y != B.y)) {
                            pathSet.insert(new Path(A, B));
                        }
                        
                        // Continue the search from the end x+1
                    }
                }
            } // y

            // Find all solid left-to-right downward diagonal lines (BACK DIAGONAL)
            for (var i = -grid.height; i < grid.width; ++i) {
                for (double x = i, y = 0; y < grid.height; ++y, ++x) {
                    if (grid.isSolidBLineAt(x, y)) {
                        // Begins a line...find the end
                        var A = new Vec2(x, y);
                        do { ++x; ++y; } while (grid.isSolidBLineAt(x, y));
                        var B = new Vec2(x - 1, y - 1);

                        // Ensure that the entire line wasn't just vertices
                        if (lineContains(A, B, '\\')) {
                            for (var j = A.x; j <= B.x; ++j) {
                                grid.setUsed(j, A.y + (j - A.x)); 
                            }

                            var top = grid.grid(A);
                            var up = grid.grid(A.x, A.y - 1);
                            var uplt = grid.grid(A.x - 1, A.y - 1);
                            if ((up == '/') || (uplt == '_') || (up == '_') || 
                                (! isVertex(top)  && 
                                (isSolidHLine(uplt) || isSolidVLine(uplt)))) {
                                // Continue half a cell more to connect for:
                                //  ___   ___
                                //  \        \    /      ----     |
                                //   \        \   \        ^      |^
                                A.x -= 0.5; A.y -= 0.5;
                            } else if (isPoint(uplt)) {
                                // Continue 1/4 cell more to connect for:
                                //
                                //  o
                                //   ^
                                //    \
                                A.x -= 0.25; A.y -= 0.25;
                            }
                            
                            var bottom = grid.grid(B);
                            var dnrt = grid.grid(B.x + 1, B.y + 1);
                            if ((grid.grid(B.x, B.y + 1) == '/') || (grid.grid(B.x + 1, B.y) == '_') || 
                                (grid.grid(B.x - 1, B.y) == '_') || 
                                (! isVertex(grid.grid(B)) &&
                                (isSolidHLine(dnrt) || isSolidVLine(dnrt)))) {
                                // Continue half a cell more to connect for:
                                //                       \      \ |
                                //  \       \     \       v      v|
                                //   \__   __\    /      ----     |
                                
                                B.x += 0.5; B.y += 0.5;
                            } else if (isPoint(dnrt)) {
                                // Continue 1/4 cell more to connect for:
                                //
                                //    \
                                //     v
                                //      o
                                
                                B.x += 0.25; B.y += 0.25;
                            }
                            
                            pathSet.insert(new Path(A, B));
                            // Continue the search from the end x+1,y+1
                        } // lineContains
                    }
                }
            } // i


            // Find all solid left-to-right upward diagonal lines (DIAGONAL)
            for (var i = -grid.height; i < grid.width; ++i) {
                for (int x = i, y = grid.height - 1; y >= 0; --y, ++x) {
                    if (grid.isSolidDLineAt(x, y)) {
                        // Begins a line...find the end
                        var A = new Vec2(x, y);
                        do { ++x; --y; } while (grid.isSolidDLineAt(x, y));
                        var B = new Vec2(x - 1, y + 1);

                        if (lineContains(A, B, '/')) {
                            // This is definitely a line. Commit the characters on it
                            for (var j = A.x; j <= B.x; ++j) {
                                grid.setUsed(j, A.y - (j - A.x)); 
                            }

                            var up = grid.grid(B.x, B.y - 1);
                            var uprt = grid.grid(B.x + 1, B.y - 1);
                            var bottom = grid.grid(B);
                            if ((up == '\\') || (up == '_') || (uprt == '_') || 
                                (! isVertex(grid.grid(B)) &&
                                (isSolidHLine(uprt) || isSolidVLine(uprt)))) {
                                
                                // Continue half a cell more to connect at:
                                //     __   __  ---     |
                                //    /      /   ^     ^|
                                //   /      /   /     / |
                                
                                B.x += 0.5; B.y -= 0.5;
                            } else if (isPoint(uprt)) {
                                
                                // Continue 1/4 cell more to connect at:
                                //
                                //       o
                                //      ^
                                //     /
                                
                                B.x += 0.25; B.y -= 0.25;
                            }
                            
                            var dnlt = grid.grid(A.x - 1, A.y + 1);
                            var top = grid.grid(A);
                            if ((grid.grid(A.x, A.y + 1) == '\\') || (grid.grid(A.x - 1, A.y) == '_') || (grid.grid(A.x + 1, A.y) == '_') ||
                                (! isVertex(grid.grid(A)) &&
                                (isSolidHLine(dnlt) || isSolidVLine(dnlt)))) {

                                // Continue half a cell more to connect at:
                                //               /     \ |
                                //    /  /      v       v|
                                // __/  /__   ----       | 
                                
                                A.x -= 0.5; A.y += 0.5;
                            } else if (isPoint(dnlt)) {
                                
                                // Continue 1/4 cell more to connect at:
                                //
                                //       /
                                //      v
                                //     o
                                
                                A.x -= 0.25; A.y += 0.25;
                            }
                            pathSet.insert(new Path(A, B));

                            // Continue the search from the end x+1,y-1
                        } // lineContains
                    }
                }
            } // y
            
            
            // Now look for curved corners. The syntax constraints require
            // that these can always be identified by looking at three
            // horizontally-adjacent characters.
            for (var y = 0; y < grid.height; ++y) {
                for (var x = 0; x < grid.width; ++x) {
                    var c = grid.grid(x, y);

                    // Note that because of undirected vertices, the
                    // following cases are not exclusive
                    if (isTopVertex(c)) {
                        // -.
                        //   |
                        if (isSolidHLine(grid.grid(x - 1, y)) && isSolidVLine(grid.grid(x + 1, y + 1))) {
                            grid.setUsed(x - 1, y); grid.setUsed(x, y); grid.setUsed(x + 1, y + 1);
                            pathSet.insert(new Path(new Vec2(x - 1, y), new Vec2(x + 1, y + 1), 
                                                    new Vec2(x + 1.1, y), new Vec2(x + 1, y + 1)));
                        }

                        //  .-
                        // |
                        if (isSolidHLine(grid.grid(x + 1, y)) && isSolidVLine(grid.grid(x - 1, y + 1))) {
                            grid.setUsed(x - 1, y + 1); grid.setUsed(x, y); grid.setUsed(x + 1, y);
                            pathSet.insert(new Path(new Vec2(x + 1, y), new Vec2(x - 1, y + 1),
                                                    new Vec2(x - 1.1, y), new Vec2(x - 1, y + 1)));
                        }
                    }
                    
                    // Special case patterns:
                    //   .  .   .  .    
                    //  (  o     )  o
                    //   '  .   '  '
                    if (((c == ')') || isPoint(c)) && (grid.grid(x - 1, y - 1) == '.') && (grid.grid(x - 1, y + 1) == '\'')) {
                        grid.setUsed(x, y); grid.setUsed(x - 1, y - 1); grid.setUsed(x - 1, y + 1);
                        pathSet.insert(new Path(new Vec2(x - 2, y - 1), new Vec2(x - 2, y + 1),
                                                new Vec2(x + 0.6, y - 1), new Vec2(x + 0.6, y + 1)));
                    }

                    if (((c == '(') || isPoint(c)) && (grid.grid(x + 1, y - 1) == '.') && (grid.grid(x + 1, y + 1) == '\'')) {
                        grid.setUsed(x, y); grid.setUsed(x + 1, y - 1); grid.setUsed(x + 1, y + 1);
                        pathSet.insert(new Path(new Vec2(x + 2, y - 1), new Vec2(x + 2, y + 1),
                                                new Vec2(x - 0.6, y - 1), new Vec2(x - 0.6, y + 1)));
                    }

                    if (isBottomVertex(c)) {
                        //   |
                        // -' 
                        if (isSolidHLine(grid.grid(x - 1, y)) && isSolidVLine(grid.grid(x + 1, y - 1))) {
                            grid.setUsed(x - 1, y); grid.setUsed(x, y); grid.setUsed(x + 1, y - 1);
                            pathSet.insert(new Path(new Vec2(x - 1, y), new Vec2(x + 1, y - 1),
                                                    new Vec2(x + 1.1, y), new Vec2(x + 1, y - 1)));
                        }

                        // | 
                        //  '-
                        if (isSolidHLine(grid.grid(x + 1, y)) && isSolidVLine(grid.grid(x - 1, y - 1))) {
                            grid.setUsed(x - 1, y - 1); grid.setUsed(x, y); grid.setUsed(x + 1, y);
                            pathSet.insert(new Path(new Vec2(x + 1, y), new Vec2(x - 1, y - 1),
                                                    new Vec2(x - 1.1, y), new Vec2(x - 1, y - 1)));
                        }
                    }
                
                } // for x
            } // for y

            // Find low horizontal lines marked with underscores. These
            // are so simple compared to the other cases that we process
            // them directly here without a helper function. Process these
            // from top to bottom and left to right so that we can read
            // them in a single sweep.
            // 
            // Exclude the special case of double underscores going right
            // into an ASCII character, which could be a source code
            // identifier such as __FILE__ embedded in the diagram.
            for (var y = 0; y < grid.height; ++y) {
                for (var x = 0; x < grid.width - 2; ++x) {
                    var lt = grid.grid(x - 1, y);

                    if ((grid.grid(x, y) == '_') && (grid.grid(x + 1, y) == '_') && 
                        (! isASCIILetter(grid.grid(x + 2, y)) || (lt == '_')) && 
                        (! isASCIILetter(lt) || (grid.grid(x + 2, y) == '_'))) {

                        var ltlt = grid.grid(x - 2, y);
                        var A = new Vec2(x - 0.5, y + 0.5);

                        if ((lt == '|') || (grid.grid(x - 1, y + 1) == '|') ||
                            (lt == '.') || (grid.grid(x - 1, y + 1) == '\'')) {
                            // Extend to meet adjacent vertical
                            A.x -= 0.5;

                            // Very special case of overrunning into the side of a curve,
                            // needed for logic gate diagrams
                            if ((lt == '.') && 
                                ((ltlt == '-') ||
                                (ltlt == '.')) &&
                                (grid.grid(x - 2, y + 1) == '(')) {
                                A.x -= 0.5;
                            }
                        } else if (lt == '/') {
                            A.x -= 1.0;
                        }

                        // Detect overrun of a tight double curve
                        if ((lt == '(') && (ltlt == '(') &&
                            (grid.grid(x, y + 1) == '\'') && (grid.grid(x, y - 1) == '.')) {
                            A.x += 0.5;
                        }
                        lt = ltlt = '\0';

                        do { grid.setUsed(x, y); ++x; } while (grid.grid(x, y) == '_');

                        var B = new Vec2(x - 0.5, y + 0.5);
                        var c = grid.grid(x, y);
                        var rt = grid.grid(x + 1, y);
                        var dn = grid.grid(x, y + 1);

                        if ((c == '|') || (dn == '|') || (c == '.') || (dn == '\'')) {
                            // Extend to meet adjacent vertical
                            B.x += 0.5;

                            // Very special case of overrunning into the side of a curve,
                            // needed for logic gate diagrams
                            if ((c == '.') && 
                                ((rt == '-') || (rt == '.')) &&
                                (grid.grid(x + 1, y + 1) == ')')) {
                                B.x += 0.5;
                            }
                        } else if ((c == '\\')) {
                            B.x += 1.0;
                        }

                        // Detect overrun of a tight double curve
                        if ((c == ')') && (rt == ')') && (grid.grid(x - 1, y + 1) == '\'') && (grid.grid(x - 1, y - 1) == '.')) {
                            B.x += -0.5;
                        }

                        pathSet.insert(new Path(A, B));
                    }
                } // for x
            } // for y
        } // findPaths


        void findDecorations(Grid grid, PathSet pathSet, DecorationSet decorationSet) {
            bool isEmptyOrVertex(char c) { return (c == ' ') || new Regex(@"[^a-zA-Z0-9]|[ov]").IsMatch(c.ToString()); }
            bool isLetter(char c) { var x = (int)c.ToString().ToUpperInvariant()[0]; return (x > 64) && (x < 91); }
                        
            /** Is the point in the center of these values on a line? Allow points that are vertically
                adjacent but not horizontally--they wouldn't fit anyway, and might be text. */
            bool onLine(char up, char dn, char lt, char rt) {
                return ((isEmptyOrVertex(dn) || isPoint(dn)) &&
                        (isEmptyOrVertex(up) || isPoint(up)) &&
                        isEmptyOrVertex(rt) &&
                        isEmptyOrVertex(lt));
            }

            for (var x = 0; x < grid.width; ++x) {
                for (var j = 0; j < grid.height; ++j) {
                    var c = grid.grid(x, j);
                    var y = j;

                    if (isJump(c)) {

                        // Ensure that this is really a jump and not a stray character
                        if (pathSet.downEndsAt(x, y - 0.5) &&
                            pathSet.upEndsAt(x, y + 0.5)) {
                            decorationSet.insert(x, y, c);
                            grid.setUsed(x, y);
                        }

                    } else if (isPoint(c)) {
                        var up = grid.grid(x, y - 1);
                        var dn = grid.grid(x, y + 1);
                        var lt = grid.grid(x - 1, y);
                        var rt = grid.grid(x + 1, y);
                        var llt = grid.grid(x - 2, y);
                        var rrt = grid.grid(x + 2, y);

                        if (pathSet.rightEndsAt(x - 1, y) ||   // Must be at the end of a line...
                            pathSet.leftEndsAt(x + 1, y) ||    // or completely isolated NSEW
                            pathSet.downEndsAt(x, y - 1) ||
                            pathSet.upEndsAt(x, y + 1) ||

                            pathSet.upEndsAt(x, y) ||    // For points on vertical lines 
                            pathSet.downEndsAt(x, y) ||  // that are surrounded by other characters
                            
                            onLine(up, dn, lt, rt)) {

                            decorationSet.insert(x, y, c);
                            grid.setUsed(x, y);
                        }
                    } else if (isGray(c)) {
                        decorationSet.insert(x, y, c);
                        grid.setUsed(x, y);
                    } else if (isTri(c)) {
                        decorationSet.insert(x, y, c);
                        grid.setUsed(x, y);
                    } else { // Arrow heads

                        // If we find one, ensure that it is really an
                        // arrow head and not a stray character by looking
                        // for a connecting line.
                        double dx = 0.0;
                        if ((c == '>') && (pathSet.rightEndsAt(x, y) || 
                                            pathSet.horizontalPassesThrough(x, y))) {
                            if (isPoint(grid.grid(x + 1, y))) {
                                // Back up if connecting to a point so as to not
                                // overlap it
                                dx = -0.5;
                            }
                            decorationSet.insert(x + dx, y, '>', 0);
                            grid.setUsed(x, y);
                        } else if ((c == '<') && (pathSet.leftEndsAt(x, y) ||
                                                pathSet.horizontalPassesThrough(x, y))) {
                            if (isPoint(grid.grid(x - 1, y))) {
                                // Back up if connecting to a point so as to not
                                // overlap it
                                dx = 0.5;
                            }
                            decorationSet.insert(x + dx, y, '>', 180); 
                            grid.setUsed(x, y);
                        } else if (c == '^') {
                            // Because of the aspect ratio, we need to look
                            // in two slots for the end of the previous line
                            if (pathSet.upEndsAt(x, y - 0.5)) {
                                decorationSet.insert(x, y - 0.5, '>', 270); 
                                grid.setUsed(x, y);
                            } else if (pathSet.upEndsAt(x, y)) {
                                decorationSet.insert(x, y, '>', 270);
                                grid.setUsed(x, y);
                            } else if (pathSet.diagonalUpEndsAt(x + 0.5, y - 0.5)) {
                                decorationSet.insert(x + 0.5, y - 0.5, '>', 270 + DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.diagonalUpEndsAt(x + 0.25, y - 0.25)) {
                                decorationSet.insert(x + 0.25, y - 0.25, '>', 270 + DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.diagonalUpEndsAt(x, y)) {
                                decorationSet.insert(x, y, '>', 270 + DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.backDiagonalUpEndsAt(x, y)) {
                                decorationSet.insert(x, y, c, 270 - DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.backDiagonalUpEndsAt(x - 0.5, y - 0.5)) {
                                decorationSet.insert(x - 0.5, y - 0.5, c, 270 - DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.backDiagonalUpEndsAt(x - 0.25, y - 0.25)) {
                                decorationSet.insert(x - 0.25, y - 0.25, c, 270 - DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.verticalPassesThrough(x, y)) {
                                // Only try this if all others failed
                                decorationSet.insert(x, y - 0.5, '>', 270); 
                                grid.setUsed(x, y);
                            }
                        } else if (c == 'v') {
                            if (pathSet.downEndsAt(x, y + 0.5)) {
                                decorationSet.insert(x, y + 0.5, '>', 90); 
                                grid.setUsed(x, y);
                            } else if (pathSet.downEndsAt(x, y)) {
                                decorationSet.insert(x, y, '>', 90);
                                grid.setUsed(x, y);
                            } else if (pathSet.diagonalDownEndsAt(x, y)) {
                                decorationSet.insert(x, y, '>', 90 + DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.diagonalDownEndsAt(x - 0.5, y + 0.5)) {
                                decorationSet.insert(x - 0.5, y + 0.5, '>', 90 + DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.diagonalDownEndsAt(x - 0.25, y + 0.25)) {
                                decorationSet.insert(x - 0.25, y + 0.25, '>', 90 + DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.backDiagonalDownEndsAt(x, y)) {
                                decorationSet.insert(x, y, '>', 90 - DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.backDiagonalDownEndsAt(x + 0.5, y + 0.5)) {
                                decorationSet.insert(x + 0.5, y + 0.5, '>', 90 - DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.backDiagonalDownEndsAt(x + 0.25, y + 0.25)) {
                                decorationSet.insert(x + 0.25, y + 0.25, '>', 90 - DIAGONAL_ANGLE);
                                grid.setUsed(x, y);
                            } else if (pathSet.verticalPassesThrough(x, y)) {
                                // Only try this if all others failed
                                decorationSet.insert(x, y + 0.5, '>', 90); 
                                grid.setUsed(x, y);
                            }
                        } // arrow heads
                    } // decoration type
                } // y
            } // x
        } // findArrowHeads

        // Cases where we want to redraw at graphical unicode character
        // to adjust its weight or shape for a conventional application
        // in constructing a diagram.
        void findReplacementCharacters(Markdeep.Grid grid, PathSet pathSet) {
            for (var x = 0; x < grid.width; ++x) {
                for (var y = 0; y < grid.height; ++y) {
                    if (grid.isUsed(x, y)) continue;
                    var c = grid.grid(x, y);
                    switch (c) {
                    case '╱':
                        pathSet.insert(new Path(new Vec2(x - 0.5, y + 0.5), new Vec2(x + 0.5, y - 0.5)));
                        grid.setUsed(x, y);
                        break;
                    case '╲':
                        pathSet.insert(new Path(new Vec2(x - 0.5, y - 0.5), new Vec2(x + 0.5, y + 0.5)));
                        grid.setUsed(x, y);
                        break;
                    }
                }
            }
        } // findReplacementCharacters

        var grid = new Markdeep.Grid(diagramString);

        var pathSet = new PathSet();
        var decorationSet = new DecorationSet();

        findPaths(grid, pathSet);
        findReplacementCharacters(grid, pathSet);
        findDecorations(grid, pathSet, decorationSet);

        var svg = "<svg class=\"diagram\" xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" height=\"" + 
            ((grid.height + 1) * SCALE * ASPECT) + "\" width=\"" + ((grid.width + 1) * SCALE) + "\"";

        if (alignmentHint == AlignmentHint.floatleft) {
            svg += " style=\"float:left;margin:15px 30px 15px 0;\"";
        } else if (alignmentHint == AlignmentHint.floatright) {
            svg += " style=\"float:right;margin:15px 0 15px 30px;\"";
        } else if (alignmentHint == AlignmentHint.center) {
            svg += " style=\"margin:0 auto 0 auto;\"";
        }

        svg += "><g transform=\"translate(" + new Vec2(1, 1) + ")\">\n";

        if (DEBUG_SHOW_GRID) {
            svg += "<g style=\"opacity:0.1\">\n";
            for (var x = 0; x < grid.width; ++x) {
                for (var y = 0; y < grid.height; ++y) {
                    svg += "<rect x=\"" + ((x - 0.5) * SCALE + 1) + "\" + y=\"" + ((y - 0.5) * SCALE * ASPECT + 2) + "\" width=\"" + (SCALE - 2) + "\" height=\"" + (SCALE * ASPECT - 2) + "\" style=\"fill:";
                    if (grid.isUsed(x, y)) {
                        svg += "red;";
                    } else if (grid.grid(x, y) == ' ') {
                        svg += "gray;opacity:0.05";
                    } else {
                        svg += "blue;";
                    }
                    svg += "\"/>\n";
                }
            }
            svg += "</g>\n";
        }
        
        svg += pathSet.toSVG();
        svg += decorationSet.toSVG();

        // Convert any remaining characters
        if (! DEBUG_HIDE_PASSTHROUGH)
        {
            svg += "<g transform=\"translate(0,0)\">";
            for (var y = 0; y < grid.height; ++y)
            {
                for (var x = 0; x < grid.width; ++x)
                {
                    var c = grid.grid(x, y);
                    if (c == '\u2B22' || c == '\u2B21')
                    {
                        // Enlarge hexagons so that they fill a grid
                        svg += "<text text-anchor=\"middle\" x=\"" + (x * SCALE) + "\" y=\"" + (4 + y * SCALE * ASPECT) + "\" style=\"font-size:20.5px\">" + escapeHTMLEntities(c.ToString()) +  "</text>";
                    } else if ((c != ' ') && ! grid.isUsed(x, y)) {
                        svg += "<text text-anchor=\"middle\" x=\"" + (x * SCALE) + "\" y=\"" + (4 + y * SCALE * ASPECT) + "\">" + escapeHTMLEntities(c.ToString()) +  "</text>";
                    } // if
                } // y
            } // x
            svg += "</g>";
        }

        if (DEBUG_SHOW_SOURCE) {
            // Offset the characters a little for easier viewing
            svg += "<g transform=\"translate(2,2)\">\n";
            for (var x = 0; x < grid.width; ++x) {
                for (var y = 0; y < grid.height; ++y) {
                    var c = grid.grid(x, y);
                    if (c != ' ') {
                        svg += "<text text-anchor=\"middle\" x=\"" + (x * SCALE) + "\" y=\"" + (4 + y * SCALE * ASPECT) + "\" style=\"fill:#F00;font-family:Menlo,monospace;font-size:12px;text-align:center\">" + escapeHTMLEntities(c.ToString()) +  "</text>";
                    } // if
                } // y
            } // x
            svg += "</g>";
        } // if

        svg += "</g></svg>";

        svg = svg.Replace(HIDE_O, 'o');


        return svg;
    }


    private const string CHARS = "0123456789abcdefghijklmnopqrstuwxyz";
    public static int Convert_ToInt32(string s, int b)
    {
        int r = 0;
        foreach (var c in s)
        {
            var v = CHARS.IndexOf(c);
            if(v == -1) { throw new Exception($"Invalid char {c} parsing {s} of base {b}"); }
            r = v + r * b;
        }

        return r;
    }

    public static string Convert_ToString(int ii, int b)
    {
        var r = "";
        var i = ii;
        while (i > 0)
        {
            var m = i % b;
            r = CHARS[m] + r;
            i = (i - m) / b;
        }

        return r;
    }






















    /**
    Performs Markdeep processing on str, which must be a string or a
    DOM element.  Returns a string that is the HTML to display for the
    body. The result does not include the header: Markdeep stylesheet
    and script tags for including a math library, or the Markdeep
    signature footer.

    Optional argument elementMode defaults to true. This avoids turning a bold first word into a 
    title or introducing a table of contents. Section captions are unaffected by this argument.
    Set elementMode = false if processing a whole document instead of an internal node.

 */
    public record Highlight(string Code, string Language);
    // private record referenceLinkTableEntry(string link, bool used);
    private class referenceLinkTableEntry
    {
        public string link { get; set; }
        public bool used { get; set; }

        public referenceLinkTableEntry(string link, bool used)
        {
            this.link = link;
            this.used = used;
        }
    }
    public string markdeepToHTML(string str, Func<Highlight, string> highlighter, Action<string> console_log, string window_location_href, bool elementMode = true) {
        // Map names to the number used for end notes, in the order
        // encountered in the text.
        Dictionary<string, int> endNoteTable = new();
        var endNoteCount = 0;

        // Reference links
        Dictionary<string, referenceLinkTableEntry> referenceLinkTable = new();

        // In the private use area
        const char PROTECT_CHARACTER = '\ue010';

        // Use base 32 for encoding numbers, which is efficient in terms of 
        // characters but avoids 'x' to avoid the pattern \dx\d, which Markdeep would
        // beautify as a dimension
        var PROTECT_RADIX     = 32;
        List<string> protectedStringArray = new();

        // Gives 1e6 possible sequences in base 32, which should be sufficient
        var PROTECT_DIGITS    = 4;

        // Put the protect character at BOTH ends to avoid having the protected number encoding
        // look like an actual number to further markdown processing
        var PROTECT_REGEXP    = new Regex(PROTECT_CHARACTER + "[0-9a-w]{" + PROTECT_DIGITS + "," + PROTECT_DIGITS + "}" + PROTECT_CHARACTER);

        /** Given an arbitrary string, returns an escaped identifier
            string to temporarily replace it with to prevent Markdeep from
            processing the contents. See expose() */
        string protect(string s)
        {
            // Generate the replacement index, converted to an alphanumeric string
            var i = Convert_ToString(protectedStringArray.push(s) - 1, PROTECT_RADIX);

            // Ensure fixed Length
            while (i.Length < PROTECT_DIGITS) {
                i = "0" + i;
            }

            return PROTECT_CHARACTER + i + PROTECT_CHARACTER;
        }

        var exposeRan = false;
        /** Given the escaped identifier string from protect(), returns
            the orginal string. */
        string expose(string i)
        {
            // Strip the escape character and parse, then look up in the
            // dictionary.
            var j = Convert_ToInt32(i.ss(1, i.Length - 1), PROTECT_RADIX);
            exposeRan = true;
            return protectedStringArray[j];
        }

        /** First-class function to pass to String.replace to protect a
            sequence defined by a regular expression. */
        string protector(Match match)
        {
            string protectee = match.S1();
            return protect(protectee);
        }

        string protectorWithPrefix(Match match)
        {
            var (prefix, protectee) = match.Get2();
            return prefix + protect(protectee);
        }

        // SECTION HEADERS
        // This is common code for numbered headers. No-number ATX headers are processed
        // separately
        MatchEvaluator makeHeaderFunc(int level)
        {
            return (Match match) =>
            {
                var header = match.Get1();
                return "\n\n</p>\n<a " + protect("class=\"target\" name=\"" + mangle(removeHTMLTags(header.rp(PROTECT_REGEXP, m => expose(m.Value)))) + "\"") + 
                    ">&nbsp;</a>" + entag("h" + level, header) + "\n<p>\n\n";
            };
        }

        /*
        if (elementMode == undefined) { 
            elementMode = true;
        }
        
        
        if (str.innerHTML != undefined) {
            str = str.innerHTML;
        }
        */

        // Prefix a newline so that blocks beginning at the top of the
        // document are processed correctly
        str = "\n\n" + str;

        // Replace pre-formatted script tags that are used to protect
        // less-than signs, e.g., in std::vector<Value>
        str = str.rp(new Regex(@"<script\s+type\s*=\s*['""]preformatted['""]\s*>([\s\S]*?)<\/script>", RegexOptions.IgnoreCase), m => m.Groups[1].Value);

        string replaceDiagrams(string str)
        {
            var result = extractDiagram(str);
            if (result.diagramString.Length > 0)
            {
                var CAPTION_REGEXP = new Regex(@"^[ \n]*[ \t]*\[[^\n]+\][ \t]*(?=\n)");
                result.afterString = result.afterString.rp(CAPTION_REGEXP, captionMatch =>
                {
                    var caption = captionMatch.Value;
                    // put target at the top
                    var processedCaption = createTarget(caption, protect);
                    result.beforeString = result.beforeString + processedCaption.target;

                    // Strip whitespace and enclosing brackets from the caption
                    caption = caption.Trim();
                    caption = caption.ss(1, caption.Length - 1);
                    
                    result.caption = entag("center", entag("div", processedCaption.caption, protect("class=\"imagecaption\"")));
                    return "";
                });

                var diagramSVG = diagramToSVG(result.diagramString, result.alignmentHint);
                var captionAbove = option.captionAbove_diagram;

                return result.beforeString +
                    (result.caption != null && captionAbove ? result.caption : "") +
                    diagramSVG +
                    (result.caption != null && ! captionAbove ? result.caption : "") + "\n" +
                    replaceDiagrams(result.afterString);
            } else {
                return str;
            }
        }

        // CODE FENCES, with styles. Do this before other processing so that their code is
        // protected from further Markdown processing
        void stylizeFence(string cssClass, string symbol)
        {
            var pattern = new Regex("\n([ \\t]*)" + symbol + "{3,}([ \\t]*\\S*)([ \\t]+.+)?\n([\\s\\S]+?)\n\\1" + symbol + "{3,}[ \t]*\n([ \\t]*\\[.+(?:\n.+){0,3}\\])?");
            
            str = str.rp(pattern, match => {
                (string indent, string? lang, string? cssSubClass, string? sourceCode, string? caption) = match.Get4Op1();
                
                Target? processedCaption = null;

                if (caption != null) {
                    caption = caption.Trim();

                    processedCaption = createTarget(caption, protect);
                    caption = processedCaption.caption;
                    caption = entag("center", "<div " + protect("class=\"listingcaption " + cssClass + "\"") + ">" + caption + "</div>") + "\n";
                }
                // Remove the block's own indentation from each line of sourceCode
                sourceCode = sourceCode.rp(new Regex("(^|\n)" + indent), m => m.Groups[1].Value);

                var captionAbove = option.captionAbove_listing;
                string? nextSourceCode, nextLang, nextCssSubClass;
                List<string> body = new();

                // Process multiple-listing blocks
                do {
                    nextSourceCode = nextLang = nextCssSubClass = null;
                    sourceCode = sourceCode.rp(new Regex("\\n([ \\t]*)" + symbol + "{3,}([ \\t]*\\S+)([ \\t]+.+)?\n([\\s\\S]*)"),
                           match => {
                               var (indent, lang, cssSubClass, everythingElse) = match.Get4();
                               nextLang = lang;
                               nextCssSubClass = cssSubClass;
                               nextSourceCode = everythingElse;
                               return "";
                           });

                    // Highlight and append this block
                    lang = lang!=null ? lang.Trim() : null;
                    string result;
                    if (lang == "none") {
                        result = highlighter(new Highlight(Code: sourceCode, Language: "none"));
                    } else if (lang == null) {
                        result = highlighter(new Highlight(Code: sourceCode, Language:"auto"));
                    } else {
                        try {
                            result = highlighter(new Highlight(Code: sourceCode, Language: lang));
                        } catch (Exception) {
                            // Some unknown language specified. Force to no formatting.
                            result = highlighter(new Highlight(Code: sourceCode, Language: "none"));
                        }
                    }
                    
                    string highlighted = result;

                    // Mark each line as a span to support line numbers
                    highlighted = highlighted.rp(new Regex(@"^(.*)$", RegexOptions.Multiline),
                        m => entag("span", "", "class=\"line\"") + m.Groups[1].Value);

                    if (cssSubClass != null && cssSubClass.Length > 0) {
                        highlighted = entag("div", highlighted, "class=\"" + cssSubClass + "\"");
                    }

                    body.push(highlighted);

                    // Advance the next nested block
                    sourceCode = nextSourceCode;
                    lang = nextLang;
                    cssSubClass = nextCssSubClass;
                } while (sourceCode != null);

                // Insert paragraph close/open tags, since browsers force them anyway around pre tags
                // We need the indent in case this is a code block inside a list that is indented.
                return "\n" + indent + "</p>" + (processedCaption!= null ? processedCaption.target : "") + (caption != null && captionAbove ? caption : "") +
                    protect(entag("pre", entag("code", string.Join("", body)), "class=\"listing " + cssClass + "\"")) +
                    (caption!=null && ! captionAbove ? caption : "") + "<p>\n";
            });
        };

        stylizeFence("tilde", "~");
        stylizeFence("backtick", "`");
        
        // Highlight explicit inline code
        str = str.rp(new Regex(@"<code\s+lang\s*=\s*[""']?([^""'\)\[\]\n]+)[""'?]\s*>(.*)<\/code>", RegexOptions.IgnoreCase), match => {
            var (lang, body) = match.Get2();
            return entag("code", highlighter(new Highlight(Code: body, Language:lang)), "lang=" + lang);
        });
        
        // Protect raw <CODE> content
        str = str.rp(new Regex(@"(<code\b.*?<\/code>)", RegexOptions.IgnoreCase), protector);

        // Remove XML/HTML COMMENTS
        // https://html.spec.whatwg.org/multipage/syntax.html#comments
        str = str.rp(new Regex(@"<!--((?!->|>)[\s\S]*?)-->"), "");

        str = replaceDiagrams(str);
        
        // Protect SVG blocks (including the ones we just inserted)
        str = str.rp(new Regex(@"<svg( .*?)?>([\s\S]*?)<\/svg>", RegexOptions.IgnoreCase), match => {
            var (attribs, body) = match.Get2();
            return "<svg" + protect(attribs) + ">" + protect(body) + "</svg>";
        });
        
        // Protect STYLE blocks
        str = str.rp(new Regex(@"<style>([\s\S]*?)<\/style>", RegexOptions.IgnoreCase),  match => {
            var body = match.Get1();
            return entag("style", protect(body));
        });

        // Protect the very special case of img tags with newlines and
        // breaks in them AND mismatched angle brackets. This happens for
        // Gravizo graphs.
        str = str.rp(new Regex(@"<img\s+src=([""'])[\s\S]*?\1\s*>", RegexOptions.IgnoreCase), match => {
            var quote = match.Get1();
            // Strip the "<img " and ">", and then protect the interior:
            return "<img " + protect(match.Value.ss(5, match.Length - 1)) + ">";
        });

        // INLINE CODE: Surrounded in (non-escaped!) back ticks on a single line.  Do this before any other
        // processing except for diagrams to protect code blocks from further interference. Don't process back ticks
        // inside of code fences. Allow a single newline, but not wrapping further because that
        // might just pick up quotes used as other punctuation across lines. Explicitly exclude
        // cases where the second quote immediately preceeds a number, e.g., "the old `97"
        var inlineLang = option.inlineCodeLang;
        var inlineCodeRegexp = new Regex(@"(^|[^\\])`(.*?(?:\n.*?)?[^\n\\`])`(?!\d)");
        if (inlineLang != null) {
            // Syntax highlight as well as converting to code. Protect
            // so that the hljs output isn't itself escaped below.
            var filenameRegexp = new Regex(@"^[a-zA-Z]:\\|^\/[a-zA-Z_\.]|^[a-z]{3,5}:\/\/");
            str = str.rp(inlineCodeRegexp, match => {
                var (before, body) = match.Get2();
                if (filenameRegexp.IsMatch(body)) {
                    // This looks like a filename, don't highlight it
                    return before + entag("code", body);
                } else {
                    return before + protect(entag("code", highlighter(new Highlight(Language: inlineLang, Code:body))));
                }
            });
        } else {
            str = str.rp(inlineCodeRegexp, m=> m.S1() + entag("code", m.S2()));
        }

        // Unescape escaped backticks
        str = str.rp(new Regex(@"\\`"), "`");
        
        // CODE: Escape angle brackets inside code blocks (including the ones we just introduced),
        // and then protect the blocks themselves
        str = str.rp(new Regex(@"(<code(?: .*?)?>)([\s\S]*?)<\/code>", RegexOptions.IgnoreCase), match => {
            var (open, inlineCode) = match.Get2();
            return protect(open + escapeHTMLEntities(inlineCode) + "</code>");
        });
        
        // PRE: Protect pre blocks
        str = str.rp(new Regex(@"(<pre\b[\s\S]*?<\/pre>)", RegexOptions.IgnoreCase), protector);
        
        // Protect raw HTML attributes from processing
        str = str.rp(new Regex(@"(<\w[^ \n<>]*?[ \t]+)(.*?)(?=\/?>)"), protectorWithPrefix);

        // End of processing literal blocks
        /////////////////////////////////////////////////////////////////////////////

        // Temporarily hide $$ MathJax LaTeX blocks from Markdown processing (this must
        // come before single $ block detection below)
        str = str.rp(new Regex(@"(\$\$[\s\S]+?\$\$)"), protector);

        // Convert LaTeX $ ... $ to MathJax, but verify that this
        // actually looks like math and not just dollar
        // signs. Don't rp double-dollar signs. Do this only
        // outside of protected blocks.

        // Also allow LaTeX of the form $...$ if the close tag is not US$ or Can$
        // and there are spaces outside of the dollar signs.
        //
        // Test: " $3 or US$2 and 3$, $x$ $y + \n 2x$ or ($z$) $k$. or $2 or $2".match(pattern) = 
        // ["$x$", "$y +  2x$", "$z$", "$k$"];
        str = str.rp(new Regex(@"((?:[^\w\d]))\$(\S(?:[^\$]*?\S(?!US|Can))??)\$(?![\w\d])"), m=> $"{m.S1()}\\({m.S2()}\\)");

        //
        // Literally: find a non-dollar sign, non-number followed
        // by a dollar sign and a space.  Then, find any number of
        // characters until the same pattern reversed, allowing
        // one punctuation character before the final space. We're
        // trying to exclude things like Canadian 1$ and US $1
        // triggering math mode.

        str = str.rp(new Regex(@"((?:[^\w\d]))\$([ \t][^\$]+?[ \t])\$(?![\w\d])"), m => $"{m.S1()}\\({m.S2()}\\)");

        // Temporarily hide MathJax LaTeX blocks from Markdown processing
        str = str.rp(new Regex(@"(\\\([\s\S]+?\\\))"), protector);
        str = str.rp(new Regex(@"(\\begin\{equation\}[\s\S]*?\\end\{equation\})"), protector);
        str = str.rp(new Regex(@"(\\begin\{eqnarray\}[\s\S]*?\\end\{eqnarray\})"), protector);
        str = str.rp(new Regex(@"(\\begin\{equation\*\}[\s\S]*?\\end\{equation\*\})"), protector);

        // HEADERS
        //
        // We consume leading and trailing whitespace to avoid creating an extra paragraph tag
        // around the header itself.

        // Setext-style H1: Text with ====== right under it
        str = str.rp(new Regex(@"(?:^|\s*\n)(.+?)\n[ \t]*={3,}[ \t]*\n"), makeHeaderFunc(1));
        
        // Setext-style H2: Text with -------- right under it
        str = str.rp(new Regex(@"(?:^|\s*\n)(.+?)\n[ \t]*-{3,}[ \t]*\n"), makeHeaderFunc(2));

        // ATX-style headers:
        //
        //  # Foo #
        //  # Foo
        //  (# Bar)
        //
        // If note that '#' in the title are only stripped if they appear at the end, in
        // order to allow headers with # in the title.

        for (var i = 6; i > 0; --i) {
            str = str.rp(new Regex(@"^\s*/" + "#{" + i + "," + i +"}(?:[ \t])([^\n]+?)#*[ \t]*\n", RegexOptions.Multiline), 
                     makeHeaderFunc(i));

            // No-number headers
            str = str.rp(new Regex(@"^\s*" + "\\(#{" + i + "," + i +"}\\)(?:[ \t])([^\n]+?)\\(?#*\\)?\\n[ \t]*\n", RegexOptions.Multiline), 
                         m => "\n</p>\n" + entag("div", m.S1(), protect("class=\"nonumberh" + i + "\"")) + "\n<p>\n\n");
        }

        // HORIZONTAL RULE: * * *, - - -, _ _ _
        str = str.rp(new Regex(@"\n[ \t]*((\*|-|_)[ \t]*){3,}[ \t]*\n"), "\n<hr/>\n");

        // PAGE BREAK or HORIZONTAL RULE: +++++
        str = str.rp(new Regex(@"\n[ \t]*\+{5,}[ \t]*\n"), "\n<hr " + protect("class=\"pagebreak\"") + "/>\n");

        // ADMONITION: !!! (class) (title)\n body
        str = str.rp(new Regex(@"^!!![ \t]*([^\s""'><&\:]*)\:?(.*)\n([ \t]{3,}.*\s*\n)*", RegexOptions.Multiline), matchMatch =>
        {
            var match = matchMatch.Value;
            var (cssClass, title) = matchMatch.Get1Op1();
            // Have to extract the body by splitting match because the regex doesn't capture the body correctly in the multi-line case
            match = match.Trim();
            return "\n\n" + entag("div", ((title!=null
                ? entag("div", title, protect("class=\"admonition-title\"")) + "\n"
                : "") + match.Substring(match.IndexOf("\n"))).Trim(), protect("class=\"admonition " + cssClass.ToLower().Trim() + "\"")) + "\n\n";
        });

        // FANCY QUOTE in a blockquote:
        // > " .... "
        // >    -- Foo

        var FANCY_QUOTE = protect("class=\"fancyquote\"");
        str = str.rp(new Regex(@"\n>[ \t]*""(.*(?:\n>.*)*)""[ \t]*(?:\n>[ \t]*)?(\n>[ \t]{2,}\S.*)?\n"),
                     match => {
                        var (quote, author) = match.Get1Op1();
                         return entag("blockquote", 
                                      entag("span",
                                            quote.rp(new Regex(@"\n>"), "\n"), 
                                            FANCY_QUOTE) + 
                                      (author!=null ? entag("span",
                                                      author.rp(new Regex(@"\n>"), "\n"),
                                                      protect("class=\"author\"")) : ""),
                                      FANCY_QUOTE);
                    });

        // BLOCKQUOTE: > in front of a series of lines
        // Process iteratively to support nested blockquotes
        var foundBlockquote = false;
        do {
            foundBlockquote = false;
            str = str.rp(new Regex(@"(?:\n>.*){2,}"), match => {
                // Strip the leading ">"
                foundBlockquote = true;
                return entag("blockquote", match.Value.rp(new Regex(@"\n>"), "\n"));
            });
        } while (foundBlockquote);


        // FOOTNOTES/ENDNOTES: [^symbolic name]. Disallow spaces in footnote names to
        // make parsing unambiguous. Consume leading space before the footnote.
        string endNote(string match, string symbolicNameA)
        {
            var symbolicName = symbolicNameA.ToLower().Trim();

            if (! (endNoteTable.ContainsKey(symbolicName)))
            {
                ++endNoteCount;
                endNoteTable[symbolicName] = endNoteCount;
            }

            return "<sup><a " + protect("href=\"#endnote-" + symbolicName + "\"") + 
                ">" + endNoteTable[symbolicName] + "</a></sup>";
        }    
        str = str.rp(new Regex(@"[ \t]*\[\^([^\]\n\t ]+)\](?!:)"), m => endNote(m.Value, m.Get1()));
        str = str.rp(new Regex(@"(\S)[ \t]*\[\^([^\]\n\t ]+)\]"), match => {
            var pre = match.Groups[1].Value;
            var symbolicNameA = match.Groups[2].Value;
            return pre + endNote(match.Value, symbolicNameA);
        });


        // CITATIONS: [#symbolicname]
        // The bibliography entry:
        str = str.rp(new Regex(@"\n\[#(\S+)\]:[ \t]+((?:[ \t]*\S[^\n]*\n?)*)"), match => {
            var (symbolicName, entry) = match.Get2();
            symbolicName = symbolicName.Trim();
            return "<div " + protect("class=\"bib\"") + ">[<a " + protect("class=\"target\" name=\"citation-" + symbolicName.ToLower() + "\"") + 
                ">&nbsp;</a><b>" + symbolicName + "</b>] " + entry + "</div>";
        });
        
        // A reference:
        // (must process AFTER the definitions, since the syntax is a subset)
        str = str.rp(new Regex(@"\[(#[^\)\(\[\]\.#\s]+(?:\s*,\s*#(?:[^\)\(\[\]\.#\s]+))*)\]"), match => {
            // Parse the symbolicNameList
            var symbolicNameList = match.S1().Split(",");
            var s = "[";
            for (var i = 0; i < symbolicNameList.Length; ++i) {
                // Strip spaces and # signs
                var name = symbolicNameList[i].rp(new Regex(@"#| "), "");
                s += entag("a", name, protect("href=\"#citation-" + name.ToLower() + "\""));
                if (i < symbolicNameList.Length - 1) { s += ", "; }
            }
            return s + "]";
        });
        

        // TABLES: line with | over line containing only | and -
        // (process before reference links to avoid ambiguity on the captions)
        str = replaceTables(str, protect);

        // REFERENCE-LINK TABLE: [foo]: http://foo.com
        // (must come before reference images and reference links in processing)
        str = str.rp(new Regex(@"^\[([^\^#].*?)\]:(.*?)$", RegexOptions.Multiline), match => {
            var (symbolicName, url) = match.Get2();
            referenceLinkTable[symbolicName.ToLower().Trim()] = new referenceLinkTableEntry(link: url.Trim(), used: false);
            return "";
        });

        // EMAIL ADDRESS: <foo@bar.baz> or foo@bar.baz if it doesn't look like a URL
        str = str.rp(new Regex(@"(?:<|(?!<)\b)(\S+@(\S+\.)+?\S{2,}?)(?:$|>|(?=<)|(?=\s)(?!>))"), match => {
            var addr = match.Groups[1].Value;
            if (new Regex(@"http:|ftp:|https:|svn:|:\/\/|\.html|\(|\)|\]").IsMatch(match.Value)) {
                // This is a hyperlink to a url with an @ sign, not an email address
                return match.Value;
            } else {
                return "<a " + protect("href=\"mailto:" + addr + "\"") + ">" + addr + "</a>";
            }
        });

        // Common code for formatting images
        string formatImage(string ignored, string url, string attribs = "")
        {
            // Detect videos
            if (new Regex(@"\.(mp4|m4v|avi|mpg|mov|webm)$", RegexOptions.IgnoreCase).IsMatch(url))
            {
                // This is video. Any attributes provided will override the defaults given here
                return "<video " + protect("class=\"markdeep\" src=\"" + url + "\"" + attribs + " width=\"480px\" controls=\"true\"") + "></video>";
            }

            if (new Regex(@"\.(mp3|mp2|ogg|wav|m4a|aac|flac)$", RegexOptions.IgnoreCase).IsMatch(url))
            {
                // Audio
                return "<audio " + protect("class=\"markdeep\" controls " + attribs + "><source src=\"" + url + "\"") + "></audio>";
            }

            var hash = new Regex(
                @"^https:\/\/(?:www\.)?(?:youtube\.com\/\S*?v=|youtu\.be\/)([\w\d-]+)\??(?:t=(\d*))?(&.*)?$",
                RegexOptions.IgnoreCase).Match(url);
            
            if (hash.Success)
            {
                if (hash.Groups.Count == 4){
                    // YouTube video with timestamp
                    return "<iframe " + protect("class=\"markdeep\" src=\"https://www.youtube.com/embed/" + hash.S1() + "?start=" + hash.S2() + "\"" + attribs + " width=\"480px\" height=\"300px\" frameborder=\"0\" allowfullscreen webkitallowfullscreen mozallowfullscreen") + "></iframe>";
                }
                else{
                    // YouTube video from the begining
                    return "<iframe " + protect("class=\"markdeep\" src=\"https://www.youtube.com/embed/" + hash.S1() + "\"" + attribs + " width=\"480px\" height=\"300px\" frameborder=\"0\" allowfullscreen webkitallowfullscreen mozallowfullscreen") + "></iframe>";
                } 
            }

            hash = new Regex(@"^https:\/\/(?:www\.)?vimeo.com\/\S*?\/([\w\d-]+)$", RegexOptions.IgnoreCase).Match(url);
            if (hash.Success)
            {
                // Vimeo video
                return "<iframe " + protect("class=\"markdeep\" src=\"https://player.vimeo.com/video/" + hash.S1() + "\"" + attribs + " width=\"480px\" height=\"300px\" frameborder=\"0\" allowfullscreen webkitallowfullscreen mozallowfullscreen") + "></iframe>";
            }

            // Image (trailing space is needed in case attribs must be quoted by the
            // browser...without the space, the browser will put the closing slash in the
            // quotes.)

            var classList = "markdeep";
            // Remove classes from attribs
            attribs = attribs.rp(new Regex(@"class *= *([""'])([^'""]+)\1"), match => {
                var (quote, cls) = match.Get2();
                classList += " " + cls;
                return "";
            });
            attribs = attribs.rp(new Regex(@"class *= *([^""' ]+)"), match => {
                var cls = match.Get1();
                classList += " " + cls;
                return "";
            });
            
            var img = "<img " + protect("class=\"" + classList + "\" src=\"" + url + "\"" + attribs) + " />";
            if (option.autoLinkImages) {
                img = entag("a", img, protect("href=\"" + url + "\" target=\"_blank\""));
            }

            return img;
        }

        // Reformat equation links that have brackets: eqn [foo] --> eqn \ref{foo} so that
        // mathjax can process them.
        str = str.rp(new Regex(@"\b(equation|eqn\.|eq\.)\s*\[([^\s\]]+)\]", RegexOptions.IgnoreCase), match => {
            var (eq, label) = match.Get2();
            return eq + " \\ref{" + label + "}";
        });


        // Reformat figure links that have subfigure labels in parentheses, to avoid them being
        // processed as links
        str = str.rp(new Regex(@"\b(figure|fig\.|table|tbl\.|listing|lst\.)\s*\[([^\s\]]+)\](?=\()", RegexOptions.IgnoreCase), match => {
            return match.Value + "<span></span>";
        });


        // Process links before images so that captions can contain links

        // Detect gravizo URLs inside of markdown images and protect them, 
        // which will cause them to be parsed sort-of reasonably. This is
        // a really special case needed to handle the newlines and potential
        // nested parentheses. Use the pattern from http://blog.stevenlevithan.com/archives/regex-recursion
        // (could be extended to multiple nested parens if needed)
        str = str.rp(new Regex(@"\(http:\/\/g.gravizo.com\/(.*g)\?((?:[^\(\)]|\([^\(\)]*\))*)\)", RegexOptions.IgnoreCase), match => {
            var protocol = match.Get1();
            var url = match.Groups[1].Value;
            return "(http://g.gravizo.com/" + protocol + "?" + Uri.EscapeDataString(url) + ")";
        });

        // HYPERLINKS: [text](url attribs)
        str = str.rp(new Regex(@"(^|[^!])\[([^\[\]]+?)\]\((""?)([^<>\s""]*?)\3(\s+[^\)]*?)?\)"), match => {
            var (pre, text, maybeQuote, url, attribs) = match.Get5();
            // todo(Gustav): test without attribute
            // attribs = attribs || "";
            return pre + "<a " + protect("href=\" + url + \"" + attribs) + ">" + text + "</a>" + maybeShowLabel(url);
        });

        // EMPTY HYPERLINKS: [](url)
        str = str.rp(new Regex(@"(^|[^!])\[[ \t]*?\]\((""?)([^<>\s""]+?)\2\)"), match => {
            var (pre, maybeQuote, url) = match.Get3();
            return pre + "<a " + protect("href=\"" + url + "\"") + ">" + url + "</a>";
        });

        // REFERENCE LINK
        str = str.rp(new Regex(@"(^|[^!])\[([^\[\]]+)\]\[([^\[\]]*)\]"), match => {
            var (pre, text, symbolicName) = match.Get3();
            // Empty symbolic name is replaced by the label text
            if (string.IsNullOrWhiteSpace(symbolicName)) {
                symbolicName = text;
            }
            
            symbolicName = symbolicName.ToLower().Trim();
            if (referenceLinkTable.TryGetValue(symbolicName, out var t) == false) {
                console_log("Reference link '" + symbolicName + "' never defined");
                return "?";
            } else {
                t.used = true;
                return pre + "<a " + protect("href=\"" + t.link + "\"") + ">" + text + "</a>";
            }
        });
        
        // Temporarily protect image captions (or things that look like
        // them) because the following code is really slow at parsing
        // captions since they have regexps that are complicated to
        // evaluate due to branching.
        //
        // The regexp is really just /.*?\n{0,5}.*/, but that executes substantially more slowly on Chrome.
        str = str.rp(new Regex(@"!\[([^\n\]].*?\n?.*?\n?.*?\n?.*?\n?.*?)\]([\[\(])"), match => {
            var (caption, bracket) = match.Get2();
            return "![" + protect(caption) + "]" + bracket;
        });
        
        // REFERENCE IMAGE: ![...][ref attribs]
        // Rewrite as a regular image for further processing below.
        str = str.rp(new Regex(@"(!\[.*?\])\[([^<>\[\]\s]+?)([ \t][^\n\[\]]*?)?\]"), match => {
            var (caption, symbolicName, attribs) = match.Get2Op1();
            symbolicName = symbolicName.ToLower().Trim();
            if (referenceLinkTable.TryGetValue(symbolicName, out var t) == false) {
                console_log("Reference image '" + symbolicName + "' never defined");
                return "?";
            } else {
                t.used = true;
                // todo(Gustav): port note: attribs isn't part of t, but is a unused capture
                var s = caption + "(" + t.link + (attribs ?? "") + ")";
                return s;
            }
        });

        
        // IMAGE GRID: Rewrite rows and grids of images into a grid
        var imageGridAttribs = protect("width=\"100%\"");
        var imageGridRowAttribs = protect("valign=\"top\"");
        // This regex is the pattern for multiple images followed by an optional single image in case the last row is ragged
        // with only one extra
        str = str.rp(new Regex(@"(?:\n(?:[ \t]*!\[.*?\]\((""?)[^<>\s]+?(?:[^\n\)]*?)?\))+[ \t]*){2,}\n"), match => {
            var table = "";

            // Break into rows:
            // Parse each row:
            foreach(var rrrow in match.Value.Split("\n"))
            {
                var row = rrrow.Trim();
                if (row.Length > 0)
                {
                    // Parse each image
                    table += entag("tr", row.rp(new Regex(@"[ \t]*!\[.*?\]\([^\)\s]+([^\)]*?)?\)"), image => {
                        var attribs = image.Get1();
                        //if (! /width|height/i.IsMatch(attribs) {
                            // Add a bogus "width" attribute to force the images to be hyperlinked to their
                            // full-resolution versions
                        //}
                        return entag("td", "\n\n"+ image + "\n\n");
                    }), imageGridRowAttribs);
                }
            }

            return "\n" + entag("table", table, imageGridAttribs) + "\n";
        });

        // SIMPLE IMAGE: ![](url attribs)
        str = str.rp(new Regex(@"(\s*)!\[\]\((""?)([^""<>\s]+?)\2(\s[^\)]*?)?\)(\s*)"), match => {
            var (preSpaces, maybeQuote, url, attribs, postSpaces) = match.Get5();
            var img = formatImage(match.Value, url, attribs);

            if (isolated(preSpaces, postSpaces)) {
                // In a block by itself: center
                img = entag("center", img);
            }

            return preSpaces + img + postSpaces;
        });

        // Explicit loop so that the output will be re-processed, preserving spaces between blocks.
        // Note that there is intentionally no global flag on the first regexp since we only want
        // to process the first occurance.
        var loop = true;
        var imageCaptionAbove = option.captionAbove_image;
        while (loop) {
            loop = false;

            // CAPTIONED IMAGE: ![caption](url attribs)
            str = str.rp(new Regex(@"(\s*)!\[(.+?)\]\((""?)([^""<>\s]+?)\3(\s[^\)]*?)?\)(\s*)"), match => {
                var (preSpaces, caption, maybeQuote, url, attribs, postSpaces) = match.Get6();
                loop = true;
                var divStyle = "";
                var iso = isolated(preSpaces, postSpaces);

                // Only floating images get their size attributes moved to the whole box
                if (attribs.Length>0 && ! iso) {
                    // Move any width *attribute* specification to the box itself
                    attribs = attribs.rp(new Regex(@"((?:max-)?width)\s*:\s*[^;'""]*"), attribMatch => {
                        var attrib = attribMatch.Get1();
                        divStyle = attribMatch + ";";
                        return attrib + ":100%";
                    });
                    
                    // Move any width *style* specification to the box itself
                    attribs = attribs.rp(new Regex(@"((?:max-)?width)\s*=\s*('\S+?'|""\S+?"")"), attribMatch => {
                        var (attrib, expr) = attribMatch.Get2();
                        // Strip the quotes
                        divStyle = attrib + ":" + expr.ss(1, expr.Length - 1) + ";";
                        return "style=\"" + attrib + ":100%\" ";
                    });
                }

                var img = formatImage(match.Value, url, attribs);

                if (iso) {
                    // In its own block: center
                    preSpaces += "<center>";
                    postSpaces = "</center>" + postSpaces;
                } else {
                    // Embedded: float
                    divStyle += "float:right;margin:4px 0px 0px 25px;";
                }
                var floating = !iso;

                var processedCaption = createTarget(expose(caption), protect);
                
                caption = entag("center", entag("span", processedCaption.caption + maybeShowLabel(url), protect("class=\"imagecaption\"")));

                // This code used to put floating images in <span> instead of <div>,
                // but it wasn't clear why and this broke centered captions
                return preSpaces + 
                entag("div", processedCaption.target + (imageCaptionAbove ? caption : "") + img + (! imageCaptionAbove ? caption : ""), protect("class=\"image\" style=\"" + divStyle + "\"")) + 
                postSpaces;
            });
        } // while replacements made
        
        ////////////////////////////////////////////

        // Process these after links, so that URLs with underscores and tildes are protected.

        // STRONG: Must run before italic, since they use the
        // same symbols. **b** __b__
        str = replaceMatched(str, @"\*\*", "strong", protect("class=\"asterisk\""));
        str = replaceMatched(str, @"__", "strong", protect("class=\"underscore\""));

        // EM (ITALICS): *i* _i_
        str = replaceMatched(str, @"\*", "em", protect("class=\"asterisk\""));
        str = replaceMatched(str, @"_", "em", protect("class=\"underscore\""));
        
        // STRIKETHROUGH: ~~text~~
        str = str.rp(new Regex(@"\~\~([^~].*?)\~\~"), m=>entag("del", m.S1()));

        // SMART DOUBLE QUOTES: "a -> localized &ldquo;   z"  -> localized &rdquo;
        // Allow situations such as "foo"==>"bar" and foo:"bar", but not 3' 9"
        if (option.smartQuotes) {
            str = str.rp(new Regex(@"(^|[ \t->])("")(?=\w)", RegexOptions.Multiline), m => m.S1() + keyword("&ldquo;"));
            str = str.rp(new Regex(@"([A-Za-z\.,:;\?!=<])("")(?=$|\W)", RegexOptions.Multiline), m => m.S1() + keyword("&rdquo;"));
        }
        
        // ARROWS:
        str = str.rp(new Regex(@"(\s|^)<==(\s)"), m=> $"{m.S1()}\u21D0{m.S2()}");
        str = str.rp(new Regex(@"(\s|^)->(\s)"), m=> $"{m.S1()}&rarr;{m.S2()}");
        // (this requires having removed HTML comments first)
        str = str.rp(new Regex(@"(\s|^)-->(\s)"), m=> $"{m.S1()}&xrarr;{m.S2()}");
        str = str.rp(new Regex(@"(\s|^)==>(\s)"), m=> $"{m.S1()}\u21D2{m.S2()}");
        str = str.rp(new Regex(@"(\s|^)<-(\s)"), m=> $"{m.S1()}&larr;{m.S2()}");
        str = str.rp(new Regex(@"(\s|^)<--(\s)"), m=> $"{m.S1()}&xlarr;{m.S2()}");
        str = str.rp(new Regex(@"(\s|^)<==>(\s)"), m=> $"{m.S1()}\u21D4{m.S2()}");
        str = str.rp(new Regex(@"(\s|^)<->(\s)"), m=> $"{m.S1()}\u2194{m.S2()}");

        // EM DASH: ---
        // (exclude things that look like table delimiters!)
        str = str.rp(new Regex(@"([^-!\:\|])---([^->\:\|])"), m => $"{m.S1()}&mdash;{m.S2()}");

        // other EM DASH: -- (we don't support en dash...it is too short and looks like a minus)
        // (exclude things that look like table delimiters!)
        str = str.rp(new Regex(@"([^-!\:\|])--([^->\:\|])"), m => $"{m.S1()}&mdash;{m.S2()}");

        // NUMBER x NUMBER:
        str = str.rp(new Regex(@"(\d+[ \t]?)x(?=[ \t]?\d+)"), m => $"{m.S1()}&times;");

        // MINUS: -4 or 2 - 1
        str = str.rp(new Regex(@"([\s\(\[<\|])-(\d)"), m => $"{m.S1()}&minus;{m.S2()}");
        str = str.rp(new Regex(@"(\d) - (\d)"), m => $"{m.S1()} &minus; {m.S2()}");

        // EXPONENTS: ^1 ^-1 (no decimal places allowed)
        str = str.rp(new Regex(@"\^([-+]?\d+)\b"), m => $"<sup>{m.S1()}</sup>");

        // PAGE BREAK:
        str = str.rp(new Regex(@"(^|\s|\b)\\(pagebreak|newpage)(\b|\s|$)", RegexOptions.IgnoreCase), protect("<div style=\"page-break-after:always\"> </div>\n"));
        
        // SCHEDULE LISTS: date : title followed by indented content
        str = replaceScheduleLists(str, protect);

        // DEFINITION LISTS: Word followed by a colon list
        // Use <dl><dt>term</dt><dd>definition</dd></dl>
        // https://developer.mozilla.org/en-US/docs/Web/HTML/Element/dl
        //
        // Process these before lists so that lists within definition lists
        // work correctly
        str = replaceDefinitionLists(str, protect);

        // LISTS: lines with -, +, *, or number.
        str = replaceLists(str, protect);

        // DEGREE: ##-degree
        str = str.rp(new Regex(@"(\d+?)[ \t-]degree(?:s?)"), m => m.S1() + "&deg;");

        // PARAGRAPH: Newline, any amount of space, newline...as long as there isn't already
        // a paragraph break there.
        str = str.rp(new Regex(@"(?:<p>)?\n\s*\n+(?!<\/p>)", RegexOptions.IgnoreCase),
                    match => new Regex(@"^<p>", RegexOptions.IgnoreCase).IsMatch(match.Value) ? match.Value : "\n\n</p><p>\n\n");

        // Remove empty paragraphs (mostly avoided by the above, but some can still occur)
        str = str.rp(new Regex(@"<p>[\s\n]*<\/p>", RegexOptions.IgnoreCase), "");


        // FOOTNOTES/ENDNOTES
        str = str.rp(new Regex(@"\n\[\^(\S+)\]: ((?:.+?\n?)*)"), match => {
            var (symbolicName, note) = match.Get2();
            symbolicName = symbolicName.ToLower().Trim();
            if (endNoteTable.ContainsKey(symbolicName)) {
                return "\n<div " + protect("class=\"endnote\"") + "><a " + 
                    protect("class=\"target\" name=\"endnote-" + symbolicName + "\"") + 
                    ">&nbsp;</a><sup>" + endNoteTable[symbolicName] + "</sup> " + note + "</div>";
            } else {
                return "\n";
            }
        });
        

        // SECTION LINKS: XXX section, XXX subsection.
        // Do this by rediscovering the headers and then recursively
        // searching for links to them. Process after other
        // forms of links to avoid ambiguity.
        
        var allHeaders = new Regex(@"<h([1-6])>(.*?)<\/h\1>", RegexOptions.IgnoreCase).Matches(str);
        foreach(Match hheader in allHeaders)
        {
            var header = hheader.Value;
            header = removeHTMLTags(header.ss(4, header.Length - 5)).Trim();
            var link = "<a " + protect("href=\"#" + mangle(header) + "\"") + ">";

            var sectionExp = "(" + keyword("section") + "|" + keyword("subsection") + "|" + keyword("chapter") + ")";
            var headerExp = "(\\b" + escapeRegExpCharacters(header) + ")";
            
            // Search for links to this section
            str = str.rp(new Regex(headerExp + "\\s+" + sectionExp, RegexOptions.IgnoreCase), m => $"{link}{m.S1()}</a> {m.S2()}");
            str = str.rp(new Regex(sectionExp + "\\s+" + headerExp, RegexOptions.IgnoreCase), m => $"{m.S1()} {link}{m.S2()}</a>");
        }
        // FIGURE, TABLE, DIAGRAM, and LISTING references:
        // (must come after figure/table/listing processing, obviously)
        str = str.rp(new Regex("\\b(fig\\.|tbl\\.|lst\\.|" + keyword("figure") + "|"
            + keyword("table") + "|" + keyword("listing") + "|" + keyword("diagram") + ")\\s+\\[([^\\s\\]]+)\\]", RegexOptions.IgnoreCase),
            match =>
        {
            var (_type, _ref) = match.Get2();
            // Fix abbreviations
            var type = _type.ToLower();
            switch (type) {
            case "fig.": type = keyword("figure").ToLower(); break;
            case "tbl.": type = keyword("table").ToLower(); break;
            case "lst.": type = keyword("listing").ToLower(); break;
            }

            // Clean up the reference
            var refer = type + "_" + mangle(_ref.ToLower().Trim());
            
            if (refTable.TryGetValue(refer, out var t))
            {
                t.used = true;
                return "<a " + protect("href=\"#" + refer + "\"") + ">" + _type + "&nbsp;" + t.number + maybeShowLabel(_ref) + "</a>";
            } else {
                console_log("Reference to undefined '" + type + " [" + _ref + "]'");
                return _type + " ?";
            }
        });

        // URL: <http://baz> or http://baz
        // Must be detected after [link]() processing 
        str = str.rp(new Regex(@"(?:<|(?!<)\b)(\w{3,6}:\/\/.+?)(?:$|>|(?=<)|(?=\s|\u00A0)(?!<))"), match => {
            var url = match.Get1();
            var extra = "";
            if (url[url.Length - 1] == '.') {
                // Accidentally sucked in a period at the end of a sentence
                url = url.ss(0, url.Length - 1);
                extra = ".";
            }
            // svn and perforce URLs are not hyperlinked. All others (http/https/ftp/mailto/tel, etc. are)
            return "<a " + ((url[0] != 's' && url[0] != 'p') ? protect("href=\"" + url + "\" class=\"url\"") : "") + ">" + url + "</a>" + extra;
        });

        if (! elementMode) {
            var TITLE_PATTERN = @"^\s*(?:<\/p><p>)?\s*<strong.*?>([^ \t\*].*?[^ \t\*])<\/strong>(?:<\/p>)?[ \t]*\n";
            
            var ALL_SUBTITLES_PATTERN = @"([ {4,}\t][ \t]*\S.*\n)*";

            // Detect a bold first line and make it into a title; detect indented lines
            // below it and make them subtitles
            str = str.rp(
                new Regex(TITLE_PATTERN + ALL_SUBTITLES_PATTERN),
                match => {
                    var title = match.Get1();
                    title = title.Trim();

                    // rp + new Regex won't give us the full list of
                    // subtitles, only the last one. So, we have to
                    // re-process match.
                    var subtitles = match.Value.Substring(match.Value.IndexOf("\n", match.Value.IndexOf("</strong>")));
                    subtitles = subtitles.Length>0 ? subtitles.rp(new Regex(@"[ \t]*(\S.*?)\n"), m => $"<div class=\"subtitle\"> {m.S1()} </div>\n") : "";
                    
                    // Remove all tags from the title when inside the <TITLE> tag, as well
                    // as unicode characters that don't render well in tabs and window bars.
                    // These regexps look like they are full of spaces but are actually various
                    // unicode space characters. http://jkorpela.fi/chars/spaces.html
                    var titleTag = removeHTMLTags(title).rp(new Regex(@"[     ]"), "").rp(new Regex(@"         　"), " ");
                    
                    return entag("title", titleTag) + maybeShowLabel(window_location_href, "center") +
                        "<div class=\"title\"> " + title + 
                        " </div>\n" + subtitles + "<div class=\"afterTitles\"></div>\n";
                });
        } // if ! noTitles

        // Remove any bogus leading close-paragraph tag inserted by our extra newlines
        str = str.rp(new Regex(@"^\s*<\/p>"), "");


        // If not in element mode and not an INSERT child, maybe add a TOC
        if (! elementMode) {
            (str, var toc) = insertTableOfContents(str, protect, text => text.rp(PROTECT_REGEXP, m=> expose(m.Value)));
            // SECTION LINKS: Replace sec. [X], section [X], subsection [X]
            str = str.rp(new Regex("\\b(" + keyword("sec") + "\\.|" + keyword("section") + "|" + keyword("subsection") + "|" + keyword("chapter") + ")\\s\\[(.+?)\\]", RegexOptions.IgnoreCase), 
                        match => {
                            var (prefix, refer) = match.Get2();
                            if (toc.TryGetValue(refer.ToLower().Trim(), out var link)) {
                                return prefix + " <a " + protect("href=\"#toc" + link + "\"") + ">" + link + "</a>";
                            } else {
                                return prefix + " ?";
                            }
                        });
        }

        // Expose all protected values. We may need to do this
        // recursively, because pre and code blocks can be nested.
        var maxIterations = 50;

        // todo(Gustav): is this loop ever run more than once?
        exposeRan = true;
        while ((str.IndexOf(PROTECT_CHARACTER) + 1)!=0 && exposeRan && (maxIterations > 0)) {
            exposeRan = false;
            str = str.rp(PROTECT_REGEXP, m => expose(m.Value));
            --maxIterations;
        }
        
        if (maxIterations <= 0) { console_log("WARNING: Ran out of iterations while expanding protected substrings"); }

        // Warn about unused references
        foreach(var key in referenceLinkTable.Keys)
        {
            if (! referenceLinkTable[key].used) {
                console_log("Reference link '[" + key + "]' is defined but never used");
            }
        }

        foreach(var key in refTable.Keys)
        {
            if (! refTable[key].used) {
                console_log("'" + refTable[key].source + "' is never referenced");
            }
        }

        if (option.linkAPIDefinitions) {
            // API DEFINITION LINKS
            
            Dictionary<string, bool> apiDefined = new();

            // Find link targets for APIs, which look like:
            // '<dt><code...>variablename' followed by (, [, or <
            //
            // If there is syntax highlighting because we're documenting
            // keywords for the language supported by HLJS, then there may
            // be an extra span around the variable name.
            str = str.rp(new Regex(@"<dt><code(\b[^<>\n]*)>(<span class=""[a-zA-Z\-_0-9]+"">)?([A-Za-z_][A-Za-z_\.0-9:\->]*)(<\/span>)?([\(\[<])"), match => {
                var (prefix, syntaxHighlight, name, syntaxHighlightEnd, next) = match.Get5();
                var linkName = name + (next == "<" ? "" : next == "(" ? "-fcn" : next == "[" ? "-array" : next);
                apiDefined[linkName] = true;
                // The 'ignore' added to the code tag below is to
                // prevent the link finding code from finding this (since
                // we don't have lookbehinds in JavaScript to recognize
                // the <dt>)
                return "<dt><a name=\"apiDefinition-" + linkName + "\"></a><code ignore " + prefix + ">" + syntaxHighlight + name + syntaxHighlightEnd + next;
            });

            // Hide links that are also inside of a <h#>...</h#>, where we don't want them
            // modified by API links. Assume that these are on a single line. The space in
            // the close tag prevents the next regexp from matching.
            str = str.rp(new Regex(@"<h([1-9])>(.*<code\b[^<>\n]*>.*)<\/code>(.*<\/h\1>)"), m => $"<h{m.S1()}>{m.S2()}</code >{m.S3()}");

            // Now find potential links, which look like:
            // '<code...>variablename</code>' and may contain () or [] after the variablename
            // They may also have an extra syntax-highlighting span
            str = str.rp(new Regex(@"<code(?! ignore)\b[^<>\n]*>(<span class=""[a-zA-Z\-_0-9]+"">)?([A-Za-z_][A-Za-z_\.0-9:\->]*)(<\/span>)?(\(\)|\[\])?<\/code>"), match => {
                var (syntaxHighlight, name, syntaxHighlightEnd, next) = match.Get4();
                var linkName = name + (next.Length>0 ? (next[0] == '(' ? "-fcn" : next[0] == '[' ? "-array" : next[0]) : "");
                return (apiDefined.GetValueOrDefault(linkName, false)) ? entag("a", match.Value, "href=\"#apiDefinition-" + linkName + "\"") : match.Value;
            });
        }
               
        return "<span class=\"md\">" + entag("p", str) + "</span>";
    }





}