﻿
using ColorCode.Styling;
using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.ColorCode;

namespace Blaggen;

public interface IDocumentParser
{
    IDocument Parse(string markdownContent);
}

public interface IDocument
{
    string ToHtml();
    string ToPlainText();
}

public class MarkdownParser : IDocumentParser
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        // todo(Gustav): fix colorcode or implement own to avoid specifying styling in the pipeline
        .UseColorCode( StyleDictionary.DefaultLight )
        .Build();

    public record Document(MarkdownDocument Doc, MarkdownPipeline pipeline) : IDocument
    {
        public string ToHtml()
        {
            return Doc.ToHtml(pipeline);
        }

        public string ToPlainText()
        {
            // stolen from Markdig implementation of ToPlainText since that isn't exposed
            var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer)
            {
                EnableHtmlForBlock = false,
                EnableHtmlForInline = false,
                EnableHtmlEscape = false,
            };
            pipeline.Setup(renderer);

            renderer.Render(Doc);
            writer.Flush();
            return writer.ToString();
        }
    }

    public IDocument Parse(string content)
    {
        var src = content.Replace("\r", "");
        var doc = Markdig.Markdown.Parse(src);
        
        // todo(Gustav): should code block actions be here or a higher level (to allow switching between markdown/markdeep)
        // or should we just remove the markdown/markdeep option and go for a opinionated tool?
        for(var i  = 0; i < doc.Count; i+=1)
        {
            var item = doc[i];
            // Console.WriteLine(item.GetType());

            switch (item)
            {
                case Markdig.Syntax.FencedCodeBlock block:
                    // hacky shebang parsing
                    var firstLine = block.Lines.Lines[0].Slice.ToString().TrimStart();
                    if (firstLine.StartsWith("//!"))// || firstLine.StartsWith("#!"))
                    {
                        // hacky argument parsing... use spectre here?
                        var cmd = firstLine.Substring(3).TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (cmd.Length > 0 && cmd[0] == "blaggen")
                        {
                            if (cmd.Length > 1)
                            {
                                switch (cmd[1])
                                {
                                    case "check":
                                        block.Lines.RemoveAt(0);
                                        break;
                                    case "remove":
                                        doc.RemoveAt(i);
                                        i -= 1;
                                        break;
                                    case "replace":
                                        // var html = Markdig.Markdown.Parse("This is a *hacky* replacement...", pipeline);
                                        // var html = "This is some <b>neat</b> html.";

                                        var html2 = """
                                            <svg width="100" height="100">
                                              <circle cx="50" cy="50" r="40" stroke="green" stroke-width="4" fill="yellow" />
                                            </svg>
                                            """;

                                        var container = new ContainerInline();
                                        container.AppendChild(new HtmlInline(html2));
                                        doc[i] = new ParagraphBlock
                                        {
                                            Inline = container
                                        };
                                        break;
                                }
                            }
                        }
                    }
                    break;
                case Markdig.Syntax.CodeBlock block:
                    // todo(Gustav): support actions for general codeblocks too?
                    break;
            }
        }

        return new Document(doc, pipeline);
    }
}

public class MarkdeepParser : IDocumentParser
{
    public IDocument Parse(string content)
    {
        return new Document(content);
    }

    private static string Highlighter(Markdeep.Highlight h)
    {
        return Markdeep.escapeHTMLEntities(h.Code);
    }

    private static void Log(string m)
    {
        Console.WriteLine(m);
    }

    public record Document(string Source) : IDocument
    {
        public string ToHtml()
        {
            var src = Source.Replace("\r", "");
            return new Markdeep().markdeepToHTML(src, Highlighter, Log, "url");
        }

        public string ToPlainText()
        {
            return Source;
        }
    }
}
