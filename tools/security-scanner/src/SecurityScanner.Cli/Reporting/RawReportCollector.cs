using SecurityScanner.Cli.Scanners;

namespace SecurityScanner.Cli.Reporting;

/// <summary>
/// Modo CI (comando aggregate): cada ferramenta já rodou como job próprio e deixou
/// o relatório bruto como artifact. Aqui só reconhecemos os arquivos pelo nome e
/// normalizamos — nenhuma ferramenta é executada.
/// </summary>
public static class RawReportCollector
{
    private static readonly (string Pattern, string Tool, Func<string, string, IReadOnlyList<Finding>> Parse)[] KnownReports =
    [
        ("trivy-fs.json", "trivy-fs", (path, tool) => TrivyRunner.Parse(path, tool)),
        ("trivy-image-*.json", "trivy-image", (path, tool) => TrivyRunner.Parse(path, tool)),
        ("opengrep.json", "opengrep", (path, _) => OpengrepRunner.Parse(path)),
        ("checkov.json", "checkov", (path, _) => CheckovRunner.Parse(path)),
        ("betterleaks.json", "betterleaks", (path, _) => BetterLeaksRunner.Parse(path)),
        ("zap.json", "zap", (path, _) => ZapRunner.Parse(path)),
    ];

    public static IReadOnlyList<ScanOutcome> Collect(string inputDir)
    {
        var outcomes = new List<ScanOutcome>();

        foreach (var (pattern, tool, parse) in KnownReports)
        {
            foreach (var path in Directory.GetFiles(inputDir, pattern, SearchOption.TopDirectoryOnly).Order())
            {
                try
                {
                    var findings = parse(path, tool);
                    outcomes.Add(new ScanOutcome(tool, true, path, findings, null));
                    Console.WriteLine($"[{tool}] {Path.GetFileName(path)}: {findings.Count} findings");
                }
                catch (Exception ex)
                {
                    // Relatório ilegível não pode virar "zero achados" em silêncio.
                    outcomes.Add(new ScanOutcome(tool, false, path, [], $"relatório ilegível: {ex.Message}"));
                    Console.Error.WriteLine($"[{tool}] FALHOU ao ler {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        return outcomes;
    }
}
