namespace GhsMarkdown.Cross.Models;

public enum ExportFormat
{
    PdfStyled,
    Docx,
    HtmlStyled,
    HtmlClean,
    PlainText
}

public class ExportResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? ErrorMessage { get; init; }
}
