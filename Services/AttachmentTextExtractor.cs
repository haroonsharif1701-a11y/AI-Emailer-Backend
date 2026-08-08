using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;

namespace AiEmailer.Api.Services;

public class AttachmentTextExtractor : IAttachmentTextExtractor
{
    public Task<string> ExtractTextAsync(Stream fileStream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".pdf" => Task.FromResult(ExtractPdf(fileStream)),
            ".docx" => Task.FromResult(ExtractDocx(fileStream)),
            ".xlsx" => Task.FromResult(ExtractXlsx(fileStream)),
            ".png" or ".jpg" or ".jpeg" => throw new NotSupportedException(
                "Image OCR isn't set up yet. Scanned images need a Tesseract OCR pipeline " +
                "(engine + trained data files), which is a separate setup from plain text " +
                "extraction — ask if you want that added next."),
            _ => throw new NotSupportedException($"Unsupported file type '{ext}'."),
        };
    }

    private static string ExtractPdf(Stream fileStream)
    {
        using var document = PdfDocument.Open(fileStream);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private static string ExtractDocx(Stream fileStream)
    {
        using var document = WordprocessingDocument.Open(fileStream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        return body?.InnerText ?? "";
    }

    private static string ExtractXlsx(Stream fileStream)
    {
        using var document = SpreadsheetDocument.Open(fileStream, false);
        var sb = new StringBuilder();
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null) return "";

        // Shared strings are stored once and referenced by index from cells —
        // without this lookup, text cells come back as raw numeric IDs.
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
            .Elements<SharedStringItem>().ToArray() ?? Array.Empty<SharedStringItem>();

        foreach (var sheet in workbookPart.Workbook.Descendants<Sheet>())
        {
            if (sheet.Id?.Value is null) continue;
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);

            foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
            {
                var values = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var text = cell.CellValue?.InnerText ?? "";
                    if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(text, out var idx)
                        && idx < sharedStrings.Length)
                    {
                        text = sharedStrings[idx].InnerText;
                    }
                    if (!string.IsNullOrWhiteSpace(text)) values.Add(text);
                }
                if (values.Count > 0) sb.AppendLine(string.Join(" | ", values));
            }
        }

        return sb.ToString();
    }
}