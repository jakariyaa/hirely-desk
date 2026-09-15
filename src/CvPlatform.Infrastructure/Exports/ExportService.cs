using System.Globalization;
using System.Text;
using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Application.Exports;
using CvPlatform.Application.Positions;
using CvPlatform.Application.Profiles;
using ClosedXML.Excel;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CvPlatform.Infrastructure.Exports;

public sealed class ExportService(
    ICvService cvs,
    IPositionAccessService positionAccess,
    IProfileImageFetcher imageFetcher) : IExportService
{
    static ExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<Result<byte[]>> ExportCvPdfAsync(
        ActorContext actor, Guid cvId, string publicBaseUrl, CancellationToken ct = default)
    {
        var rendered = await cvs.GetRenderedAsync(actor, cvId, ct);
        if (!rendered.Succeeded || rendered.Value is null)
            return Result<byte[]>.Failure(rendered.Error.Code, rendered.Error.Message);

        var detail = rendered.Value;
        var qr = PngByteQRCodeHelper.GetQRCode(
            $"{publicBaseUrl.TrimEnd('/')}/cvs/{cvId}", QRCodeGenerator.ECCLevel.Q, 8);
        var photo = await imageFetcher.FetchAsync(detail.ProfilePhotoUrl, ct);

        try
        {
            return Result<byte[]>.Success(GeneratePdf(detail, qr, photo));
        }
        catch (Exception) when (photo is not null)
        {
            return Result<byte[]>.Success(GeneratePdf(detail, qr, null));
        }
    }

    public async Task<Result<byte[]>> ExportPositionsExcelAsync(
        ActorContext actor, CancellationToken ct = default)
    {
        var browsed = await positionAccess.BrowseAsync(
            actor, new PageRequest(1, PageRequest.MaxPageSize), null, ct);
        if (!browsed.Succeeded || browsed.Value is null)
            return Result<byte[]>.Failure(browsed.Error.Code, browsed.Error.Message);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Positions");
        var headers = new[] { "Title", "Company", "Level", "Visibility", "CVs", "Likes" };
        WriteHeaders(sheet, headers);
        var row = 2;
        foreach (var position in browsed.Value.Items)
        {
            WriteCell(sheet.Cell(row, 1), position.Title);
            WriteCell(sheet.Cell(row, 2), position.Company);
            WriteCell(sheet.Cell(row, 3), position.Level);
            WriteCell(sheet.Cell(row, 4), position.IsPublic ? "Public" : "Restricted");
            sheet.Cell(row, 5).Value = position.CvCount;
            sheet.Cell(row, 6).Value = position.LikeCount;
            row++;
        }
        StyleSheet(sheet, headers.Length, row - 1, row > 2);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Result<byte[]>.Success(stream.ToArray());
    }

    public async Task<Result<byte[]>> ExportPositionCvsExcelAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        var data = await cvs.ListForPositionExportAsync(actor, positionId, ct);
        if (!data.Succeeded || data.Value is null)
            return Result<byte[]>.Failure(data.Error.Code, data.Error.Message);

        var rows = data.Value;
        var columns = GetFieldColumns(rows);
        var headers = BuildHeaders(columns);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("CVs");
        WriteHeaders(sheet, headers);

        var row = 2;
        foreach (var exportRow in rows)
        {
            WriteExportRow(sheet, row++, exportRow, columns);
        }
        StyleSheet(sheet, headers.Length, row - 1, rows.Count > 0);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Result<byte[]>.Success(stream.ToArray());
    }

    public async Task<Result<byte[]>> ExportPositionCvsCsvAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        var data = await cvs.ListForPositionExportAsync(actor, positionId, ct);
        if (!data.Succeeded || data.Value is null)
            return Result<byte[]>.Failure(data.Error.Code, data.Error.Message);

        var rows = data.Value;
        var columns = GetFieldColumns(rows);
        var headers = BuildHeaders(columns);
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', headers.Select(EscapeCsv)));

        foreach (var exportRow in rows)
        {
            var values = BuildExportValues(exportRow, columns);
            csv.AppendLine(string.Join(',', values.Select(value => EscapeCsv(SafeSpreadsheetValue(value)))));
        }

        return Result<byte[]>.Success(new UTF8Encoding(true).GetBytes(csv.ToString()));
    }

    private static byte[] GeneratePdf(CvDetailDto detail, byte[] qr, byte[]? photo)
    {
        var displayName = GetDisplayName(detail);
        var sections = detail.Rows
            .Where(row => row.IsFilled && row.DataType != Core.Enums.AttributeDataType.Image)
            .Where(row => row.Name != ProfileAttributeNames.Name)
            .GroupBy(row => NormalizeSectionName(row.CategoryName))
            .OrderBy(group => SectionOrder(group.Key))
            .ThenBy(group => group.Key)
            .ToList();
        var projects = detail.Projects.Where(project => project.IsIncluded).ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.8f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Column(column =>
                        {
                            column.Item().Text(displayName).Bold().FontSize(25).FontColor(Colors.Blue.Darken3);
                            column.Item().PaddingTop(3).Text(detail.Cv.PositionTitle)
                                .FontSize(12).FontColor(Colors.Grey.Darken1);
                            if (!string.IsNullOrWhiteSpace(detail.Cv.Company))
                                column.Item().Text(detail.Cv.Company).FontSize(10).FontColor(Colors.Grey.Darken1);
                        });
                        row.ConstantItem(92).Column(side =>
                        {
                            if (photo is not null)
                                side.Item().Height(68).Width(68).AlignRight().Image(photo).FitHeight();
                            side.Item().PaddingTop(4).AlignRight().Height(24).Width(24).Image(qr).FitHeight();
                        });
                    });
                    header.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Blue.Medium);
                });

                page.Content().PaddingTop(12).Column(content =>
                {
                    content.Spacing(14);
                    foreach (var section in sections)
                    {
                        content.Item().EnsureSpace(45).Column(sectionColumn =>
                        {
                            sectionColumn.Item().Text(section.Key.ToUpperInvariant())
                                .Bold().FontSize(11).FontColor(Colors.Blue.Darken2);
                            sectionColumn.Item().PaddingTop(4).Column(fields =>
                            {
                                fields.Spacing(6);
                                foreach (var field in section)
                                {
                                    fields.Item().Column(fieldColumn =>
                                    {
                                        fieldColumn.Item().Text(field.Name).Bold().FontColor(Colors.Grey.Darken2);
                                        fieldColumn.Item().Text(FormatValue(field));
                                    });
                                }
                            });
                        });
                    }

                    content.Item().ShowEntire().Column(projectSection =>
                    {
                        projectSection.Item().Text("PROJECTS")
                            .Bold().FontSize(11).FontColor(Colors.Blue.Darken2);
                        if (projects.Count == 0)
                        {
                            projectSection.Item().PaddingTop(4).Text("No projects selected.")
                                .FontColor(Colors.Grey.Darken1);
                        }
                        else
                        {
                            projectSection.Item().PaddingTop(4).Column(projectsColumn =>
                            {
                                projectsColumn.Spacing(9);
                                foreach (var project in projects)
                                {
                                    projectsColumn.Item().EnsureSpace(35).Column(projectColumn =>
                                    {
                                        projectColumn.Item().Text(project.Name).Bold().FontSize(10.5f);
                                        var period = FormatPeriod(project.PeriodStart, project.PeriodEnd);
                                        if (period != "—")
                                            projectColumn.Item().Text(period).FontSize(9)
                                                .FontColor(Colors.Grey.Darken1);
                                        if (project.Tags is { Count: > 0 })
                                            projectColumn.Item().Text(string.Join(" • ", project.Tags))
                                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                                        if (!string.IsNullOrWhiteSpace(project.DescriptionMarkdown))
                                            projectColumn.Item().PaddingTop(2)
                                                .Text(StripMarkdown(project.DescriptionMarkdown));
                                    });
                                }
                            });
                        }
                    });
                });

                page.Footer().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text("Hirely Desk")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Page ").FontSize(8);
                        text.CurrentPageNumber().FontSize(8);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static IReadOnlyList<FieldColumn> GetFieldColumns(IReadOnlyList<CvExportRowDto> rows) =>
        rows.SelectMany(row => row.Rows)
            .GroupBy(row => row.AttributeDefinitionId)
            .Select(group => group.OrderBy(row => row.SortOrder).First())
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.AttributeDefinitionId)
            .Select(row => new FieldColumn(row.AttributeDefinitionId, row.Name))
            .ToList();

    private static string[] BuildHeaders(IReadOnlyList<FieldColumn> columns) =>
        ["CV ID", "Candidate", "Status", "Published At", .. columns.Select(column => column.Header), "Projects"];

    private static string[] BuildExportValues(
        CvExportRowDto exportRow, IReadOnlyList<FieldColumn> columns)
    {
        var valuesById = exportRow.Rows.ToDictionary(row => row.AttributeDefinitionId);
        var values = new List<string>
        {
            exportRow.Cv.Id.ToString(),
            exportRow.DisplayName ?? exportRow.Cv.CandidateName,
            exportRow.Cv.Status.ToString(),
            exportRow.Cv.PublishedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        };
        values.AddRange(columns.Select(column =>
            valuesById.TryGetValue(column.Id, out var field) ? FormatValue(field) : string.Empty));
        values.Add(string.Join(Environment.NewLine, exportRow.Projects.Select(FormatProject)));
        return [.. values];
    }

    private static void WriteExportRow(
        IXLWorksheet sheet, int row, CvExportRowDto exportRow, IReadOnlyList<FieldColumn> columns)
    {
        var values = BuildExportValues(exportRow, columns);
        for (var column = 0; column < values.Length; column++)
            WriteCell(sheet.Cell(row, column + 1), values[column]);
    }

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var column = 0; column < headers.Count; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
    }

    private static void StyleSheet(IXLWorksheet sheet, int columnCount, int lastRow, bool createTable)
    {
        var header = sheet.Range(1, 1, 1, columnCount);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#174A7C");
        sheet.SheetView.FreezeRows(1);
        if (lastRow >= 2)
            sheet.Range(2, 1, lastRow, columnCount).Style.Alignment.WrapText = true;
        if (createTable)
        {
            var table = sheet.Range(1, 1, lastRow, columnCount).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
            table.ShowAutoFilter = true;
        }
        sheet.Columns().AdjustToContents();
        foreach (var column in sheet.ColumnsUsed())
            column.Width = Math.Min(Math.Max(column.Width, 12), 45);
    }

    private static void WriteCell(IXLCell cell, string? value) =>
        cell.Value = SafeSpreadsheetValue(value ?? string.Empty);

    private static string SafeSpreadsheetValue(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? $"'{value}" : value;

    private static string EscapeCsv(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private static string GetDisplayName(CvDetailDto detail) =>
        string.IsNullOrWhiteSpace(detail.DisplayName)
            ? detail.Cv.CandidateName
            : detail.DisplayName;

    private static string NormalizeSectionName(string? category) =>
        string.IsNullOrWhiteSpace(category) || category.Equals("Me", StringComparison.OrdinalIgnoreCase)
            ? "Profile"
            : category;

    private static int SectionOrder(string section) => section switch
    {
        "Profile" => 0,
        "Experience" => 1,
        "Education" => 2,
        "Skills" => 3,
        "Languages" => 4,
        _ => 5,
    };

    private static string FormatValue(CvFieldRowDto field) => field switch
    {
        { StringValue: { } s } => s,
        { TextValue: { } t } => StripMarkdown(t),
        { NumericValue: { } n } => n.ToString("0.##", CultureInfo.InvariantCulture),
        { DateValue: { } d } => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        { PeriodStart: { } start, PeriodEnd: { } end } => FormatPeriod(start, end),
        { PeriodStart: { } start } => $"{start:yyyy/MM} – …",
        { PeriodEnd: { } end } => $"… – {end:yyyy/MM}",
        { BooleanValue: { } b } => b ? "Yes" : "No",
        { DropdownOption: { } dd } => dd,
        { ImageUrl: { } img } => img,
        _ => "—",
    };

    private static string FormatProject(CvProjectOptionDto project)
    {
        var parts = new List<string> { project.Name };
        var period = FormatPeriod(project.PeriodStart, project.PeriodEnd);
        if (period != "—")
            parts.Add(period);
        if (project.Tags is { Count: > 0 })
            parts.Add(string.Join(", ", project.Tags));
        if (!string.IsNullOrWhiteSpace(project.DescriptionMarkdown))
            parts.Add(StripMarkdown(project.DescriptionMarkdown));
        return string.Join(" — ", parts);
    }

    private static string FormatPeriod(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        ({ } s, { } e) => $"{s:yyyy/MM} – {e:yyyy/MM}",
        ({ } s, null) => $"{s:yyyy/MM} – …",
        (null, { } e) => $"… – {e:yyyy/MM}",
        _ => "—",
    };

    private static string StripMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown;
        var text = markdown.Replace("\r", " ").Replace("\n", " ").Trim();
        foreach (var token in new[] { "**", "__", "##", "#", "`", ">", "-", "*", "_" })
            text = text.Replace(token, "");
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        return text.Length > 2000 ? text[..2000] : text;
    }

    private sealed record FieldColumn(Guid Id, string Header);
}
