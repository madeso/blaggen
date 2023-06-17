
using ColorCode.Styling;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
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
        return new Document(Markdig.Markdown.Parse(content), pipeline);
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
