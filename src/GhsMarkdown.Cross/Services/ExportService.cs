using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GhsMarkdown.Cross.Models;
using Markdig;
using Markdig.Extensions.Tables;
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

        // Add built-in heading styles so Word/LibreOffice recognize Heading1–6
        AddBuiltInStyles(mainPart);

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

            case Markdig.Extensions.Tables.Table mdTable:
                var tbl = new DocumentFormat.OpenXml.Wordprocessing.Table();

                // Table properties — simple grid with borders
                var tblProps = new TableProperties(
                    new TableBorders(
                        new TopBorder    { Val = BorderValues.Single, Size = 4, Color = "AAAAAA" },
                        new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "AAAAAA" },
                        new LeftBorder   { Val = BorderValues.Single, Size = 4, Color = "AAAAAA" },
                        new RightBorder  { Val = BorderValues.Single, Size = 4, Color = "AAAAAA" },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "AAAAAA" },
                        new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4, Color = "AAAAAA" }
                    ),
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }
                );
                tbl.Append(tblProps);

                foreach (var rowBlock in mdTable)
                {
                    if (rowBlock is not Markdig.Extensions.Tables.TableRow mdRow) continue;

                    var tblRow = new DocumentFormat.OpenXml.Wordprocessing.TableRow();

                    // Shade header row
                    if (mdRow.IsHeader)
                    {
                        var trProps = new TableRowProperties(
                            new TableHeader());
                        tblRow.Append(trProps);
                    }

                    foreach (var cellBlock in mdRow)
                    {
                        if (cellBlock is not Markdig.Extensions.Tables.TableCell mdCell) continue;

                        var tblCell = new DocumentFormat.OpenXml.Wordprocessing.TableCell();

                        // Cell properties
                        var tcProps = new TableCellProperties(
                            new TableCellWidth { Type = TableWidthUnitValues.Auto });

                        if (mdRow.IsHeader)
                        {
                            tcProps.Append(new Shading
                            {
                                Val = ShadingPatternValues.Clear,
                                Color = "auto",
                                Fill = "2F5496"
                            });
                        }
                        tblCell.Append(tcProps);

                        // Cell content — walk child blocks
                        foreach (var cellChild in mdCell)
                        {
                            if (cellChild is ParagraphBlock cellPara)
                            {
                                var cellP = new Paragraph();
                                if (mdRow.IsHeader)
                                {
                                    // Bold white text for header cells
                                    var cellPProps = new ParagraphProperties();
                                    cellP.Append(cellPProps);
                                    foreach (var inline in cellPara.Inline ?? Enumerable.Empty<Markdig.Syntax.Inlines.Inline>())
                                    {
                                        if (inline is LiteralInline lit)
                                        {
                                            var hRun = new Run();
                                            var hRunProps = new RunProperties(
                                                new Bold(),
                                                new Color { Val = "FFFFFF" });
                                            hRun.Append(hRunProps);
                                            hRun.Append(new Text(lit.Content.ToString())
                                                { Space = SpaceProcessingModeValues.Preserve });
                                            cellP.Append(hRun);
                                        }
                                        else
                                        {
                                            WriteInlines(cellP,
                                                cellPara.Inline);
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    WriteInlines(cellP, cellPara.Inline);
                                }
                                tblCell.Append(cellP);
                            }
                        }

                        // OpenXml requires at least one paragraph in every cell
                        if (!tblCell.Elements<Paragraph>().Any())
                            tblCell.Append(new Paragraph());

                        tblRow.Append(tblCell);
                    }

                    tbl.Append(tblRow);
                }

                body.Append(tbl);
                // Add an empty paragraph after the table so Word doesn't
                // merge it with the next block
                body.Append(new Paragraph());
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

    private static void AddBuiltInStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        for (int level = 1; level <= 6; level++)
        {
            var fontSize = level switch
            {
                1 => 48, // 24pt
                2 => 36, // 18pt
                3 => 28, // 14pt
                4 => 24, // 12pt
                5 => 22, // 11pt
                _ => 20  // 10pt
            };

            var style = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = $"Heading{level}",
                CustomStyle = false
            };
            style.Append(new StyleName { Val = $"heading {level}" });
            style.Append(new BasedOn { Val = "Normal" });
            style.Append(new NextParagraphStyle { Val = "Normal" });

            var ppr = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "240", After = "60" });
            style.Append(ppr);

            var rpr = new StyleRunProperties(
                new Bold(),
                new FontSize { Val = fontSize.ToString() });
            if (level <= 2)
                rpr.Append(new Color { Val = "2F5496" });
            style.Append(rpr);

            styles.Append(style);
        }

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }
}
