using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using M = MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace ToDoList.Core.Export;

public sealed class DocumentExporter(string fontFile)
{
    private static readonly object FontLock = new();

    public void WriteWord(ExportPlan plan, string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var body = new W.Body();
        main.Document = new W.Document(body);
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new W.Styles(new W.DocDefaults(
            new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                Fonts(), new W.FontSize { Val = "24" }, new W.Languages { Val = "zh-CN", EastAsia = "zh-CN" })),
            new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                new W.SpacingBetweenLines { After = "100", Line = "320", LineRule = W.LineSpacingRuleValues.Auto }))));
        EmbedFont(main);

        body.Append(new W.Paragraph(
            new W.ParagraphProperties(new W.KeepNext(), new W.SpacingBetweenLines { After = "360" },
                new W.Justification { Val = W.JustificationValues.Center }),
            new W.Run(new W.RunProperties(Fonts(), new W.Bold(), new W.FontSize { Val = "52" }),
                new W.Text(plan.Title) { Space = SpaceProcessingModeValues.Preserve })));

        foreach (var day in plan.Days)
        {
            body.Append(new W.Paragraph(
                new W.ParagraphProperties(new W.KeepNext(), new W.SpacingBetweenLines { Before = "220", After = "120" }),
                new W.Run(new W.RunProperties(Fonts(), new W.Bold(), new W.FontSize { Val = "28" }),
                    new W.Text(ExportPlanner.DateHeading(day.Date)))));

            var columns = new W.TableGrid();
            if (plan.ShowCheckboxes) columns.Append(new W.GridColumn { Width = "500" });
            columns.Append(new W.GridColumn { Width = plan.ShowCheckboxes ? "6938" : "7438" }, new W.GridColumn { Width = "2200" });
            var table = new W.Table(new W.TableProperties(
                new W.TableWidth { Width = "9638", Type = W.TableWidthUnitValues.Dxa },
                new W.TableBorders(new W.TopBorder { Val = W.BorderValues.Nil }, new W.LeftBorder { Val = W.BorderValues.Nil },
                    new W.BottomBorder { Val = W.BorderValues.Nil }, new W.RightBorder { Val = W.BorderValues.Nil },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Nil }, new W.InsideVerticalBorder { Val = W.BorderValues.Nil }),
                new W.TableLayout { Type = W.TableLayoutValues.Fixed }),
                columns);

            if (day.Items.Count == 0)
                for (var i = 0; i < 3; i++) AddWordRow(table, plan.ShowCheckboxes ? "□" : null, "", "");
            foreach (var item in day.Items)
            {
                var content = TextFlow.Chunks(item.Content);
                var duration = TextFlow.Chunks(item.Duration, 100, 5);
                for (var i = 0; i < Math.Max(content.Count, duration.Count); i++)
                    AddWordRow(table, plan.ShowCheckboxes ? (i == 0 ? "□" : "") : null,
                        i < content.Count ? content[i] : "", i < duration.Count ? duration[i] : "");
            }
            body.Append(table);
        }
        body.Append(new W.SectionProperties(
            new W.PageSize { Width = 11906U, Height = 16838U },
            new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134U, Right = 1134U, Header = 567U, Footer = 567U, Gutter = 0U }));
        main.Document.Save();
    }

    private static W.RunFonts Fonts() => new() { Ascii = ExportPlanner.FontFamily, HighAnsi = ExportPlanner.FontFamily, EastAsia = ExportPlanner.FontFamily };

    private static void AddWordRow(W.Table table, string? checkbox, string content, string duration)
    {
        var row = new W.TableRow(new W.TableRowProperties(new W.CantSplit()));
        if (checkbox is not null) row.Append(WordCell(checkbox, 500, false));
        row.Append(WordCell(content, checkbox is null ? 7438 : 6938, false), WordCell(duration, 2200, true));
        table.Append(row);
    }

    private static W.TableCell WordCell(string text, int width, bool alignRight)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
            new W.SpacingBetweenLines { After = "120", Line = "320", LineRule = W.LineSpacingRuleValues.Auto },
            new W.Justification { Val = alignRight ? W.JustificationValues.Right : W.JustificationValues.Left }));
        var run = new W.Run(new W.RunProperties(Fonts(), new W.FontSize { Val = "24" }));
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Append(new W.Break());
            run.Append(new W.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        paragraph.Append(run);
        return new W.TableCell(new W.TableCellProperties(
            new W.TableCellWidth { Width = width.ToString(System.Globalization.CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa },
            new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Top }), paragraph);
    }

    private void EmbedFont(MainDocumentPart main)
    {
        var table = main.AddNewPart<FontTablePart>();
        var fontPart = table.AddFontPart(FontPartType.FontOdttf);
        var bytes = File.ReadAllBytes(fontFile);
        var key = Guid.NewGuid();
        var keyBytes = Convert.FromHexString(key.ToString("N"));
        Array.Reverse(keyBytes);
        for (var i = 0; i < Math.Min(32, bytes.Length); i++) bytes[i] ^= keyBytes[i % 16];
        using (var stream = new MemoryStream(bytes)) fontPart.FeedData(stream);
        table.Fonts = new W.Fonts(new W.Font(
            new W.FontFamily { Val = W.FontFamilyValues.Swiss },
            new W.EmbedRegularFont { Id = table.GetIdOfPart(fontPart), FontKey = key.ToString("B").ToUpperInvariant() })
            { Name = ExportPlanner.FontFamily });
        table.Fonts.Save();
        var settings = main.AddNewPart<DocumentSettingsPart>();
        settings.Settings = new W.Settings(new W.EmbedTrueTypeFonts());
        settings.Settings.Save();
    }

    public void WritePdf(ExportPlan plan, string path)
    {
        lock (FontLock)
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new PortableFontResolver(fontFile);

        var document = new M.Document();
        document.Info.Title = plan.Title;
        document.Info.Author = "to-do-list";
        var normal = document.Styles[M.StyleNames.Normal]!;
        normal.Font.Name = ExportPlanner.FontFamily;
        normal.Font.Size = 12;
        normal.ParagraphFormat.SpaceAfter = M.Unit.FromPoint(6);
        normal.ParagraphFormat.LineSpacingRule = M.LineSpacingRule.AtLeast;
        normal.ParagraphFormat.LineSpacing = M.Unit.FromPoint(17);
        var section = document.AddSection();
        section.PageSetup.PageFormat = M.PageFormat.A4;
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = M.Unit.FromMillimeter(20);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = M.Unit.FromMillimeter(20);
        var title = section.AddParagraph(plan.Title);
        title.Format.Alignment = M.ParagraphAlignment.Center;
        title.Format.Font.Size = 26;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = M.Unit.FromPoint(18);
        title.Format.KeepWithNext = true;

        foreach (var day in plan.Days)
        {
            var heading = section.AddParagraph(ExportPlanner.DateHeading(day.Date));
            heading.Format.Font.Bold = true;
            heading.Format.Font.Size = 14;
            heading.Format.SpaceBefore = M.Unit.FromPoint(11);
            heading.Format.SpaceAfter = M.Unit.FromPoint(6);
            heading.Format.KeepWithNext = true;
            var table = section.AddTable();
            table.Borders.Visible = false;
            table.LeftPadding = M.Unit.FromPoint(0);
            table.RightPadding = M.Unit.FromPoint(0);
            table.TopPadding = M.Unit.FromPoint(0);
            table.BottomPadding = M.Unit.FromPoint(2);
            if (plan.ShowCheckboxes) table.AddColumn(M.Unit.FromMillimeter(8.82));
            table.AddColumn(M.Unit.FromMillimeter(plan.ShowCheckboxes ? 122.38 : 131.2));
            table.AddColumn(M.Unit.FromMillimeter(38.8));
            if (day.Items.Count == 0)
                for (var i = 0; i < 3; i++) AddPdfRow(table, plan.ShowCheckboxes ? "□" : null, "", "");
            foreach (var item in day.Items)
            {
                var content = TextFlow.Chunks(item.Content);
                var duration = TextFlow.Chunks(item.Duration, 100, 5);
                for (var i = 0; i < Math.Max(content.Count, duration.Count); i++)
                    AddPdfRow(table, plan.ShowCheckboxes ? (i == 0 ? "□" : "") : null,
                        i < content.Count ? content[i] : "", i < duration.Count ? duration[i] : "");
            }
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.Save(path);
    }

    private static void AddPdfRow(Table table, string? checkbox, string content, string duration)
    {
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Top;
        var column = 0;
        if (checkbox is not null) row.Cells[column++].AddParagraph(checkbox);
        row.Cells[column++].AddParagraph(content);
        row.Cells[column].AddParagraph(duration).Format.Alignment = M.ParagraphAlignment.Right;
    }
}
