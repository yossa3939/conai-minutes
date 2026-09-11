using ConAI.Web.Configuration;
using ConAI.Web.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace ConAI.Web.Tests;

public class ReferenceDocumentConverterTests
{
    private readonly IReferenceDocumentConverter _converter =
        new ReferenceDocumentConverter(Options.Create(new UploadOptions()));

    private static MemoryStream CreateDocx(params string[] paragraphs)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Wordprocessing.Body();
            foreach (var text in paragraphs)
            {
                body.AppendChild(new Wordprocessing.Paragraph(
                    new Wordprocessing.Run(new Wordprocessing.Text(text))));
            }

            mainPart.Document = new Wordprocessing.Document(body);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateXlsx(params string[] values)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Spreadsheet.Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new Spreadsheet.SheetData();

            foreach (var value in values)
            {
                sheetData.AppendChild(new Spreadsheet.Row(
                    new Spreadsheet.Cell
                    {
                        DataType = Spreadsheet.CellValues.String,
                        CellValue = new Spreadsheet.CellValue(value)
                    }));
            }

            worksheetPart.Worksheet = new Spreadsheet.Worksheet(sheetData);
            workbookPart.Workbook.AppendChild(new Spreadsheet.Sheets(new Spreadsheet.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "予算"
            }));
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void docxの段落を改行区切りで取り出す()
    {
        using var stream = CreateDocx("一行目です。", "二行目です。");

        var result = _converter.ExtractText(".docx", stream);

        Assert.Contains("一行目です。", result);
        Assert.Contains("二行目です。", result);
    }

    [Fact]
    public void xlsxのセルとシート名を取り出す()
    {
        using var stream = CreateXlsx("売上", "1200");

        var result = _converter.ExtractText(".xlsx", stream);

        Assert.Contains("予算", result);
        Assert.Contains("売上", result);
        Assert.Contains("1200", result);
    }

    [Fact]
    public void 変換対象外の拡張子は変換しない()
    {
        Assert.True(_converter.CanConvert(".DOCX"));
        Assert.False(_converter.CanConvert(".pdf"));
        Assert.False(_converter.CanConvert(".mp4"));
    }

    [Fact]
    public void 参考資料のテキストは上限で打ち切る()
    {
        var options = Options.Create(new UploadOptions { MaxExtractedTextChars = 100 });
        var converter = new ReferenceDocumentConverter(options);
        var paragraphs = Enumerable.Range(1, 50).Select(i => $"段落{i:00}。").ToArray();

        using var stream = CreateDocx(paragraphs);

        var result = converter.ExtractText(".docx", stream);

        // 全 50 段落（約 250 文字）を渡しても、上限 100 + 断り書きの範囲に収まること
        var notice = $"{Environment.NewLine}（以降は長すぎるため省略しました）";
        Assert.True(result.Length <= 100 + notice.Length, $"長さ {result.Length}");
        Assert.Contains("（以降は長すぎるため省略しました）", result);
        Assert.DoesNotContain("段落50", result);
    }
}
