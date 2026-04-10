using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GhsMarkdown.Cross.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GhsMarkdown.Cross.Services;

public class ExportService
{
    private readonly MarkdownParsingService _parsingService;

    public ExportService(MarkdownParsingService parsingService)
    {
        _parsingService = parsingService;
    }

    public string GeneratePreviewHtml(ExportFormat format, string markdownContent, string themedCss)
    {
        return format switch
        {
            ExportFormat.HtmlStyled => BuildStyledHtml(markdownContent, themedCss),
            ExportFormat.HtmlClean => BuildCleanHtml(markdownContent),
            ExportFormat.PlainText => BuildPlainTextPreview(markdownContent),
            ExportFormat.PdfStyled => BuildStyledHtml(markdownContent, themedCss),
            ExportFormat.Docx => BuildDocxPreview(markdownContent),
            _ => BuildStyledHtml(markdownContent, themedCss)
        };
    }

    public async Task<ExportResult> ExportAsync(ExportFormat format, string markdownContent,
        string themedCss, string filePath)
    {
        try
        {
            switch (format)
            {
                case ExportFormat.HtmlStyled:
                    await File.WriteAllTextAsync(filePath, BuildStyledHtml(markdownContent, themedCss),
                        new UTF8Encoding(true));
                    break;

                case ExportFormat.HtmlClean:
                    await File.WriteAllTextAsync(filePath, BuildCleanHtml(markdownContent),
                        new UTF8Encoding(true));
                    break;

                case ExportFormat.PlainText:
                    await File.WriteAllTextAsync(filePath, markdownContent, Encoding.UTF8);
                    break;

                case ExportFormat.Docx:
                    await Task.Run(() => GenerateDocx(markdownContent, filePath));
                    break;

                case ExportFormat.PdfStyled:
                    // PDF export is handled by the view layer via ExportWithWebViewFunc
                    // which has access to NativeWebView.PrintToPdfStreamAsync().
                    // If we reach here, the delegate was not wired up.
                    return new ExportResult { Success = false,
                        ErrorMessage = "WebView not available for PDF export." };
            }

            return new ExportResult { Success = true, FilePath = filePath };
        }
        catch (Exception ex)
        {
            return new ExportResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public string BuildStyledHtml(string markdownContent, string themedCss)
    {
        var doc = _parsingService.Parse(markdownContent);
        var bodyHtml = _parsingService.RenderToHtml(doc);
        return "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<style>\n" +
               "html, body { background: #FFFFFF; color: #1A1A1A; }\n" +
               themedCss +
               "\nbody { margin: 2rem; font-family: sans-serif; line-height: 1.7; }\n" +
               "</style>\n</head>\n<body>\n" +
               bodyHtml +
               "\n</body>\n</html>";
    }

    private string BuildCleanHtml(string markdownContent)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var bodyHtml = Markdig.Markdown.ToHtml(markdownContent, pipeline);
        return "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<style>\n" +
               "html, body { background: #FFFFFF; color: #1A1A1A; font-family: Georgia, serif; " +
               "font-size: 16px; line-height: 1.7; max-width: 680px; margin: 0 auto; padding: 2rem; }\n" +
               "</style>\n</head>\n<body>\n" +
               bodyHtml +
               "\n</body>\n</html>";
    }

    private static string BuildPlainTextPreview(string markdownContent)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(markdownContent);
        return "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<style>\n" +
               "html, body { background: #FFFFFF; color: #1A1A1A; " +
               "font-family: 'Cascadia Code', Consolas, monospace; font-size: 13px; " +
               "line-height: 1.6; margin: 0; padding: 1.5rem; white-space: pre-wrap; }\n" +
               "</style>\n</head>\n<body>" +
               encoded +
               "\n</body>\n</html>";
    }

    private static string BuildDocxPreview(string markdownContent)
    {
        return "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<style>\n" +
               "body { background: #F5F5F5; color: #1A1A1A; font-family: sans-serif; margin: 2rem; line-height: 1.6; }\n" +
               "h1,h2,h3,h4,h5,h6 { color: #333; }\n" +
               "code { font-family: 'Courier New', monospace; background: #E8E8E8; padding: 2px 4px; }\n" +
               "pre { background: #E8E8E8; padding: 1rem; }\n" +
               "blockquote { border-left: 3px solid #999; padding-left: 1rem; color: #555; font-style: italic; }\n" +
               "</style>\n</head>\n<body>\n" +
               Markdig.Markdown.ToHtml(markdownContent) +
               "\n</body>\n</html>";
    }

    private void GenerateDocx(string markdownContent, string filePath)
    {
        var doc = _parsingService.Parse(markdownContent);

        using var wordDoc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = wordDoc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        foreach (var block in doc)
        {
            WriteBlock(body, block);
        }
    }

    private static void WriteBlock(Body body, Markdig.Syntax.Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var hPara = new Paragraph();
                var hProps = new ParagraphProperties(new ParagraphStyleId { Val = $"Heading{heading.Level}" });
                hPara.Append(hProps);
                WriteInlines(hPara, heading.Inline);
                body.Append(hPara);
                break;

            case ParagraphBlock para:
                var pPara = new Paragraph();
                WriteInlines(pPara, para.Inline);
                body.Append(pPara);
                break;

            case ListBlock list:
                foreach (var item in list)
                {
                    if (item is ListItemBlock listItem)
                    {
                        foreach (var child in listItem)
                        {
                            if (child is ParagraphBlock itemPara)
                            {
                                var lPara = new Paragraph();
                                var lProps = new ParagraphProperties(
                                    new ParagraphStyleId { Val = "ListParagraph" });
                                lPara.Append(lProps);
                                WriteInlines(lPara, itemPara.Inline);
                                body.Append(lPara);
                            }
                        }
                    }
                }
                break;

            case FencedCodeBlock codeBlock:
                var codePara = new Paragraph();
                var codeRun = new Run();
                var codeRunProps = new RunProperties(
                    new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New" });
                codeRun.Append(codeRunProps);
                var codeText = new StringBuilder();
                foreach (var line in codeBlock.Lines)
                    codeText.AppendLine(line.ToString());
                codeRun.Append(new Text(codeText.ToString().TrimEnd()) { Space = SpaceProcessingModeValues.Preserve });
                codePara.Append(codeRun);
                body.Append(codePara);
                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    if (child is ParagraphBlock qPara)
                    {
                        var quotePara = new Paragraph();
                        var quoteProps = new ParagraphProperties(
                            new Indentation { Left = "720" });
                        quotePara.Append(quoteProps);
                        WriteInlines(quotePara, qPara.Inline);
                        body.Append(quotePara);
                    }
                }
                break;

            case ThematicBreakBlock:
                var hrPara = new Paragraph();
                var hrProps = new ParagraphProperties(
                    new ParagraphBorders(
                        new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "999999" }));
                hrPara.Append(hrProps);
                body.Append(hrPara);
                break;
        }
    }

    private static void WriteInlines(Paragraph para, ContainerInline? inlines)
    {
        if (inlines is null) return;
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    var run = new Run(new Text(literal.Content.ToString()) { Space = SpaceProcessingModeValues.Preserve });
                    para.Append(run);
                    break;

                case EmphasisInline emphasis:
                    foreach (var child in emphasis)
                    {
                        if (child is LiteralInline empLiteral)
                        {
                            var empRun = new Run();
                            var empProps = new RunProperties();
                            if (emphasis.DelimiterCount >= 2)
                                empProps.Append(new Bold());
                            else
                                empProps.Append(new Italic());
                            empRun.Append(empProps);
                            empRun.Append(new Text(empLiteral.Content.ToString()) { Space = SpaceProcessingModeValues.Preserve });
                            para.Append(empRun);
                        }
                    }
                    break;

                case CodeInline code:
                    var codeRun = new Run();
                    var codeProps = new RunProperties(
                        new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New" });
                    codeRun.Append(codeProps);
                    codeRun.Append(new Text(code.Content) { Space = SpaceProcessingModeValues.Preserve });
                    para.Append(codeRun);
                    break;

                case LineBreakInline:
                    para.Append(new Run(new Break()));
                    break;
            }
        }
    }
}
