using SecurityScanner.Cli.Scanners;

namespace SecurityScanner.Cli.Reporting;

public static class ConsolidatedReportBuilder
{
    public static ConsolidatedReport Build(
        string runId,
        string target,
        IReadOnlyList<ScanOutcome> outcomes,
        IgnoreRules? ignoreRules = null)
    {
        var all = outcomes.SelectMany(o => o.Findings).ToList();
        // Where, não Except: Finding é record e Except deduplicaria achados idênticos.
        var suppressed = ignoreRules is null ? [] : all.Where(ignoreRules.Matches).ToList();
        var findings = ignoreRules is null ? all : all.Where(f => !ignoreRules.Matches(f)).ToList();

        var bySeverity = findings
            .GroupBy(f => NormalizeSeverity(f.Severity))
            .ToDictionary(g => g.Key, g => g.Count());

        return new ConsolidatedReport(runId, target, DateTimeOffset.UtcNow, outcomes, findings, bySeverity, suppressed);
    }

    // ERROR é a severidade mais alta das regras no formato Semgrep/Opengrep, mas no
    // pacote de regras comunitário ela marca muita coisa de alto ruído — HIGH, não CRITICAL.
    public static string NormalizeSeverity(string severity) => severity.ToUpperInvariant() switch
    {
        "CRITICAL" => "CRITICAL",
        "HIGH" or "ERROR" => "HIGH",
        "MEDIUM" or "WARNING" => "MEDIUM",
        "LOW" or "INFO" => "LOW",
        _ => "UNKNOWN",
    };

    public static int SeverityRank(Finding finding) => SeverityRank(NormalizeSeverity(finding.Severity));

    public static int SeverityRank(string normalizedSeverity) => normalizedSeverity switch
    {
        "CRITICAL" => 0,
        "HIGH" => 1,
        "MEDIUM" => 2,
        "LOW" => 3,
        _ => 4,
    };
}
