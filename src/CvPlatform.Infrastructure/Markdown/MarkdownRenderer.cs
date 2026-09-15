using CvPlatform.Application.Markdown;
using Ganss.Xss;
using Markdig;

namespace CvPlatform.Infrastructure.Markdown;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;
    private readonly HtmlSanitizer _sanitizer;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedSchemes.Add("mailto");
    }

    public string ToSafeHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;
        var html = Markdig.Markdown.ToHtml(markdown, _pipeline);
        return _sanitizer.Sanitize(html);
    }
}
