namespace CvPlatform.Application.Markdown;

public interface IMarkdownRenderer
{
    string ToSafeHtml(string? markdown);
}
