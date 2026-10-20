namespace SecurityScanner.Cli.Scanners;

public interface IScanRunner
{
    string Name { get; }

    Task<ScanOutcome> RunAsync(string targetDir, string resultsDir, CancellationToken ct);
}
