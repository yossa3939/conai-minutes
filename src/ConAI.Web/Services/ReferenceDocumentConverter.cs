using System.Text;
using ConAI.Web.Configuration;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Presentation = DocumentFormat.OpenXml.Presentation;
using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace ConAI.Web.Services;

public interface IReferenceDocumentConverter
{
    bool CanConvert(string extension);

    string ExtractText(string extension, Stream content);
}

public sealed class ReferenceDocumentConverter : IReferenceDocumentConverter
{
    private static readonly HashSet<string> Convertible =
        new(StringComparer.OrdinalIgnoreCase) { ".docx", ".pptx", ".xlsx" };

    private readonly int _maxExtractedTextChars;

    public ReferenceDocumentConverter(IOptions<UploadOptions> options) =>
        _maxExtractedTextChars = options.Value.MaxExtractedTextChars;

    public bool CanConvert(string extension) => Convertible.Contains(extension);

    public string ExtractText(string extension, Stream content) => extension.ToLowerInvariant() switch
    {
        ".docx" => ExtractFromWord(content),
        ".pptx" => ExtractFromPresentation(content),
        ".xlsx" => ExtractFromSpreadsheet(content),
        _ => throw new NotSupportedException($"{extension} はテキスト抽出に対応していません。")
    };

    private string ExtractFromWord(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var paragraph in body.Descendants<Wordprocessing.Paragraph>())
        {
            var text = paragraph.InnerText;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // 読みながら累積長を見る。全部読んでから切るのでは zip 爆弾を止められない。
            var separator = builder.Length > 0 ? Environment.NewLine : string.Empty;
            if (builder.Length + separator.Length + text.Length > _maxExtractedTextChars)
            {
                return AppendTruncationNotice(builder);
            }

            builder.Append(separator).Append(text);
        }

        return builder.ToString();
    }

    private string ExtractFromPresentation(Stream content)
    {
        using var document = PresentationDocument.Open(content, false);
        var presentationPart = document.PresentationPart;
        var slideIdList = presentationPart?.Presentation?.SlideIdList;
        if (presentationPart is null || slideIdList is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var slideNumber = 1;

        foreach (var slideId in slideIdList.Elements<Presentation.SlideId>())
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId)
            {
                continue;
            }

            if (presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
            {
                continue;
            }

            if (!AppendLineWithinLimit(builder, $"--- スライド {slideNumber++} ---"))
            {
                return AppendTruncationNotice(builder);
            }

            foreach (var text in slidePart.Slide?.Descendants<Drawing.Text>() ?? Enumerable.Empty<Drawing.Text>())
            {
                if (!AppendLineWithinLimit(builder, text.Text))
                {
                    return AppendTruncationNotice(builder);
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string ExtractFromSpreadsheet(Stream content)
    {
        using var document = SpreadsheetDocument.Open(content, false);
        var workbookPart = document.WorkbookPart;
        var sheets = workbookPart?.Workbook?.Sheets;
        if (workbookPart is null || sheets is null)
        {
            return string.Empty;
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?
            .Elements<Spreadsheet.SharedStringItem>()
            .Select(item => item.InnerText)
            .ToArray() ?? [];

        var builder = new StringBuilder();

        foreach (var sheet in sheets.Elements<Spreadsheet.Sheet>())
        {
            if (sheet.Id?.Value is not { } relationshipId)
            {
                continue;
            }

            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            if (!AppendLineWithinLimit(builder, $"--- シート {sheet.Name} ---"))
            {
                return AppendTruncationNotice(builder);
            }

            foreach (var row in worksheetPart.Worksheet?.Descendants<Spreadsheet.Row>() ?? Enumerable.Empty<Spreadsheet.Row>())
            {
                var cells = row.Elements<Spreadsheet.Cell>().Select(cell => CellText(cell, sharedStrings));
                if (!AppendLineWithinLimit(builder, string.Join("\t", cells)))
                {
                    return AppendTruncationNotice(builder);
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>1 行を足しても上限に収まるときだけ追記する。収まらないときは false を返し、呼び出し側は列挙を打ち切る。</summary>
    private bool AppendLineWithinLimit(StringBuilder builder, string value)
    {
        if (builder.Length + value.Length + Environment.NewLine.Length > _maxExtractedTextChars)
        {
            return false;
        }

        builder.Append(value).Append(Environment.NewLine);
        return true;
    }

    /// <summary>打ち切ったことを生成側に伝える断り書きを末尾に足す。</summary>
    private static string AppendTruncationNotice(StringBuilder builder) =>
        builder.Append(Environment.NewLine)
            .Append("（以降は長すぎるため省略しました）")
            .ToString();

    private static string CellText(Spreadsheet.Cell cell, IReadOnlyList<string> sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? cell.InnerText;

        if (cell.DataType?.Value == Spreadsheet.CellValues.SharedString
            && int.TryParse(value, out var index)
            && index >= 0
            && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return value;
    }
}
