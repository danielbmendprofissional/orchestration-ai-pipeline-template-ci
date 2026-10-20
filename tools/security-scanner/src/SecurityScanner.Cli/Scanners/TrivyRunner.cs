using System.Text.Json;
using SecurityScanner.Cli.Reporting;

namespace SecurityScanner.Cli.Scanners;

public sealed class TrivyRunner : IScanRunner
{
    private const string ToolName = "trivy";

    public string Name => ToolName;

    public async Task<ScanOutcome> RunAsync(string targetDir, string resultsDir, CancellationToken ct)
    {
        var outFile = Path.Combine(resultsDir, "trivy.json");
        var containerName = DockerCommandRunner.ContainerName(ToolName);
        await DockerCommandRunner.RemoveContainerIfExistsAsync(containerName, ct);

        var args = new List<string>
        {
            "run", "--rm", "--name", containerName,
            "-v", $"{targetDir}:/src:ro",
            "-v", $"{resultsDir}:/results",
            "aquasec/trivy:0.76.0",
            "fs", "--format", "json", "-o", "/results/trivy.json", "/src",
        };

        await DockerCommandRunner.RunAsync(args, ct);

        if (!File.Exists(outFile))
            return new ScanOutcome(ToolName, false, null, [], "Trivy não gerou arquivo de resultado.");

        return new ScanOutcome(ToolName, true, outFile, Parse(outFile), null);
    }

    internal static IReadOnlyList<Finding> Parse(string path, string toolName = ToolName)
    {
        var findings = new List<Finding>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        if (!doc.RootElement.TryGetProperty("Results", out var results))
            return findings;

        foreach (var result in results.EnumerateArray())
        {
            var target = result.TryGetProperty("Target", out var t) ? t.GetString() ?? "" : "";

            if (result.TryGetProperty("Vulnerabilities", out var vulns))
            {
                foreach (var v in vulns.EnumerateArray())
                {
                    var pkg = v.TryGetProperty("PkgName", out var p) ? p.GetString() : null;
                    var id = v.TryGetProperty("VulnerabilityID", out var vid) ? vid.GetString() : null;
                    var title = v.TryGetProperty("Title", out var ti) ? ti.GetString() : null;

                    findings.Add(new Finding(
                        Tool: toolName,
                        Severity: v.TryGetProperty("Severity", out var sev) ? sev.GetString() ?? "UNKNOWN" : "UNKNOWN",
                        Title: title ?? $"{id} em {pkg}",
                        RuleId: id,
                        FilePath: target,
                        Line: null,
                        Description: pkg is null ? null : $"pacote: {pkg}"));
                }
            }

            if (result.TryGetProperty("Secrets", out var secrets))
            {
                foreach (var s in secrets.EnumerateArray())
                {
                    findings.Add(new Finding(
                        Tool: toolName,
                        Severity: s.TryGetProperty("Severity", out var sev) ? sev.GetString() ?? "UNKNOWN" : "UNKNOWN",
                        Title: s.TryGetProperty("Title", out var ti) ? ti.GetString() ?? "secret detectado" : "secret detectado",
                        RuleId: s.TryGetProperty("RuleID", out var rid) ? rid.GetString() : null,
                        FilePath: target,
                        Line: s.TryGetProperty("StartLine", out var line) && line.ValueKind == JsonValueKind.Number ? line.GetInt32() : null,
                        Description: null));
                }
            }

            if (result.TryGetProperty("Misconfigurations", out var miscs))
            {
                foreach (var m in miscs.EnumerateArray())
                {
                    findings.Add(new Finding(
                        Tool: toolName,
                        Severity: m.TryGetProperty("Severity", out var sev) ? sev.GetString() ?? "UNKNOWN" : "UNKNOWN",
                        Title: m.TryGetProperty("Title", out var ti) ? ti.GetString() ?? "" : "",
                        RuleId: m.TryGetProperty("ID", out var id) ? id.GetString() : null,
                        FilePath: target,
                        Line: null,
                        Description: null));
                }
            }
        }

        return findings;
    }
}
