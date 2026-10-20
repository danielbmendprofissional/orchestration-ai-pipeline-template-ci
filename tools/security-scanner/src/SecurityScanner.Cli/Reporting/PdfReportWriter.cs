using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SecurityScanner.Cli.Reporting;

public static class PdfReportWriter
{
    public static void Write(ConsolidatedReport report, string path)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Security Scan Report — {report.Target}").FontSize(18).Bold();
                    col.Item().Text($"Run: {report.RunId}    Gerado em: {report.GeneratedAt:u}").FontSize(9);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Text("Resumo por severidade").Bold().FontSize(13);
                    foreach (var (severity, count) in report.CountsBySeverity.OrderBy(SeverityOrder))
                        col.Item().Text($"{severity}: {count}");

                    if (report.Suppressed.Count > 0)
                        col.Item().Text($"Suprimidos por .security-ignore: {report.Suppressed.Count}").FontColor(Colors.Grey.Darken1);

                    col.Item().PaddingTop(15).Text("Findings").Bold().FontSize(13);

                    foreach (var finding in report.Findings.OrderBy(ConsolidatedReportBuilder.SeverityRank))
                    {
                        col.Item().PaddingTop(8).BorderBottom(0.5f).PaddingBottom(6).Column(findingCol =>
                        {
                            var severity = ConsolidatedReportBuilder.NormalizeSeverity(finding.Severity);
                            findingCol.Item().Text($"[{finding.Tool}] {severity} — {finding.Title}").Bold();

                            if (!string.IsNullOrEmpty(finding.FilePath))
                            {
                                var location = finding.Line is not null ? $"{finding.FilePath}:{finding.Line}" : finding.FilePath;
                                findingCol.Item().Text(location).FontSize(9).FontColor(Colors.Grey.Darken1);
                            }

                            if (!string.IsNullOrEmpty(finding.RuleId))
                                findingCol.Item().Text($"regra: {finding.RuleId}").FontSize(9).FontColor(Colors.Grey.Darken1);

                            if (!string.IsNullOrEmpty(finding.Description))
                                findingCol.Item().Text(finding.Description!).FontSize(9);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(path);
    }

    private static int SeverityOrder(KeyValuePair<string, int> entry) => entry.Key switch
    {
        "CRITICAL" => 0,
        "HIGH" => 1,
        "MEDIUM" => 2,
        "LOW" => 3,
        _ => 4,
    };
}
