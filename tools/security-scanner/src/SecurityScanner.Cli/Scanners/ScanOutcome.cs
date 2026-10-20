using SecurityScanner.Cli.Reporting;

namespace SecurityScanner.Cli.Scanners;

public sealed record ScanOutcome(
    string Tool,
    bool Success,
    string? RawResultPath,
    IReadOnlyList<Finding> Findings,
    string? ErrorMessage);
