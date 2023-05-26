using Markdig;
using Markdig.Renderers;

namespace Blaggen;

public class Markdown
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public Markdig.Syntax.MarkdownDocument Parse(string content)
    {
        return Markdig.Markdown.Parse(content);
    }

    public string ToHtml(Markdig.Syntax.MarkdownDocument document)
    {
        return document.ToHtml(pipeline);
    }

    public string ToPlainText(Markdig.Syntax.MarkdownDocument document)
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

        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }
}
