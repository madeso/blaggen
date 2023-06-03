using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Blaggen;

public class Markdown
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public MarkdownDocument Parse(string content)
    {
        return Markdig.Markdown.Parse(content);
    }

    public string ToHtml(MarkdownDocument document)
    {
        return document.ToHtml(pipeline);
    }

    public string ToPlainText(MarkdownDocument document)
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
