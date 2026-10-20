using System.Text.Json;
using System.Text.RegularExpressions;
using SecurityScanner.Cli.Reporting;

namespace SecurityScanner.Cli.Scanners;

/// <summary>
/// DAST via OWASP ZAP baseline scan (spider + passive scan, não ataca ativamente).
/// O alvo precisa ser uma aplicação já em execução — se ela roda no host (fora de
/// Docker), use http://host.docker.internal:PORTA em vez de localhost.
/// </summary>
public sealed class ZapRunner : INetworkScanRunner
{
    private const string ToolName = "zap";

    public string Name => ToolName;

    public async Task<ScanOutcome> RunAsync(string targetUrl, string resultsDir, CancellationToken ct)
    {
        var jsonOutFile = Path.Combine(resultsDir, "zap.json");
        var containerName = DockerCommandRunner.ContainerName(ToolName);
        await DockerCommandRunner.RemoveContainerIfExistsAsync(containerName, ct);

        var args = new List<string>
        {
            "run", "--rm", "--name", containerName,
            "-v", $"{resultsDir}:/zap/wrk/:rw",
            "zaproxy/zap-stable:2.17.0",
            "zap-baseline.py",
            "-t", targetUrl,
            "-J", "zap.json",
            "-I", // não falhar por causa de WARN — o resultado real vem do arquivo, não do exit code
        };

        // zap-baseline.py sai com código != 0 quando encontra alertas (comportamento esperado).
        await DockerCommandRunner.RunAsync(args, ct);

        if (!File.Exists(jsonOutFile))
            return new ScanOutcome(ToolName, false, null, [], "ZAP não gerou arquivo de resultado.");

        return new ScanOutcome(ToolName, true, jsonOutFile, Parse(jsonOutFile), null);
    }

    internal static IReadOnlyList<Finding> Parse(string path)
    {
        var findings = new List<Finding>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        if (!doc.RootElement.TryGetProperty("site", out var sites))
            return findings;

        foreach (var site in sites.EnumerateArray())
        {
            if (!site.TryGetProperty("alerts", out var alerts))
                continue;

            foreach (var alert in alerts.EnumerateArray())
            {
                var name = alert.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var ruleId = alert.TryGetProperty("pluginid", out var p) ? p.GetString() : null;
                var severity = MapRiskCode(alert.TryGetProperty("riskcode", out var rc) ? rc.GetString() : null);
                var description = alert.TryGetProperty("desc", out var d) ? StripHtml(d.GetString()) : null;

                if (alert.TryGetProperty("instances", out var instances) && instances.GetArrayLength() > 0)
                {
                    foreach (var instance in instances.EnumerateArray())
                    {
                        var uri = instance.TryGetProperty("uri", out var u) ? u.GetString() : null;
                        findings.Add(new Finding(ToolName, severity, name, ruleId, uri, null, description));
                    }
                }
                else
                {
                    findings.Add(new Finding(ToolName, severity, name, ruleId, null, null, description));
                }
            }
        }

        return findings;
    }

    private static string MapRiskCode(string? riskCode) => riskCode switch
    {
        "3" => "HIGH",
        "2" => "MEDIUM",
        "1" => "LOW",
        "0" => "INFO",
        _ => "UNKNOWN",
    };

    private static string? StripHtml(string? html) =>
        html is null ? null : Regex.Replace(html, "<.*?>", "").Trim();
}
