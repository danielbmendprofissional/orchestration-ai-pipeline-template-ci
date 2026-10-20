namespace SecurityScanner.Cli.Scanners;

/// <summary>
/// Scanners que atacam uma aplicação em execução (URL/host:porta) em vez de um
/// caminho de código-fonte — ex: ZAP (DAST).
/// </summary>
public interface INetworkScanRunner
{
    string Name { get; }

    Task<ScanOutcome> RunAsync(string targetUrl, string resultsDir, CancellationToken ct);
}
