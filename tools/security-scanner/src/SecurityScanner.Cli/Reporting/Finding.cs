namespace SecurityScanner.Cli.Reporting;

public sealed record Finding(
    string Tool,
    string Severity,
    string Title,
    string? RuleId,
    string? FilePath,
    int? Line,
    string? Description);
