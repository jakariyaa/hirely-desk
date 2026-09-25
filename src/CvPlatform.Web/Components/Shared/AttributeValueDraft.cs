namespace CvPlatform.Web.Components.Shared;

public sealed class AttributeValueDraft
{
    public string? String { get; set; }
    public string? Text { get; set; }
    public decimal? Numeric { get; set; }
    public DateOnly? Date { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public bool? Boolean { get; set; }
    public string? Dropdown { get; set; }
    public Guid? ImageValueId { get; set; }
    public string? ImageObjectKey { get; set; }
    public long Version { get; set; }
}
