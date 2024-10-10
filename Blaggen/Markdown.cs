
using ColorCode.Styling;
using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.ColorCode;

namespace Blaggen;

internal record ParsedMarkdown(MarkdownDocument Doc, MarkdownPipeline pipeline)
{
    internal string ToHtml()
    {
        return Doc.ToHtml(pipeline);
    }

    internal string ToPlainText()
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

internal class MarkdownParser
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        // todo(Gustav): fix colorcode or implement own to avoid specifying styling in the pipeline
        .UseColorCode()
        .Build();

    internal ParsedMarkdown Parse(string content)
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
                                        var html = """
                                            <svg width="100" height="100">
                                              <circle cx="50" cy="50" r="40" stroke="green" stroke-width="4" fill="yellow" />
                                            </svg>
                                            """;

                                        var container = new ContainerInline();
                                        container.AppendChild(new HtmlInline(html));
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

        return new ParsedMarkdown(doc, pipeline);
    }
}
