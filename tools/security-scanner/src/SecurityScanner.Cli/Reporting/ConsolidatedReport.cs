using SecurityScanner.Cli.Scanners;

namespace SecurityScanner.Cli.Reporting;

public sealed record ConsolidatedReport(
    string RunId,
    string Target,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ScanOutcome> ToolOutcomes,
    IReadOnlyList<Finding> Findings,
    IReadOnlyDictionary<string, int> CountsBySeverity,
    IReadOnlyList<Finding> Suppressed);
