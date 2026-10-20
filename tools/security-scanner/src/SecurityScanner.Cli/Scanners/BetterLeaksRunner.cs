using System.Text.Json;
using SecurityScanner.Cli.Reporting;

namespace SecurityScanner.Cli.Scanners;

public sealed class BetterLeaksRunner : IScanRunner
{
    private const string ToolName = "betterleaks";

    public string Name => ToolName;

    public async Task<ScanOutcome> RunAsync(string targetDir, string resultsDir, CancellationToken ct)
    {
        var outFile = Path.Combine(resultsDir, "betterleaks.json");
        var containerName = DockerCommandRunner.ContainerName(ToolName);
        await DockerCommandRunner.RemoveContainerIfExistsAsync(containerName, ct);

        var args = new List<string>
        {
            "run", "--rm", "--name", containerName,
            "-v", $"{targetDir}:/src:ro",
            "-v", $"{resultsDir}:/results",
            "ghcr.io/betterleaks/betterleaks:v1",
            "dir", "--report-format", "json", "--report-path", "/results/betterleaks.json", "/src",
        };

        // betterleaks sai com código != 0 quando encontra segredos (comportamento esperado, não é falha).
        await DockerCommandRunner.RunAsync(args, ct);

        if (!File.Exists(outFile))
            return new ScanOutcome(ToolName, false, null, [], "BetterLeaks não gerou arquivo de resultado.");

        return new ScanOutcome(ToolName, true, outFile, Parse(outFile), null);
    }

    internal static IReadOnlyList<Finding> Parse(string path)
    {
        var text = File.ReadAllText(path).Trim();
        if (text is "" or "null")
            return [];

        var findings = new List<Finding>();
        using var doc = JsonDocument.Parse(text);

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            findings.Add(new Finding(
                Tool: ToolName,
                // segredo exposto no código é sempre tratado como severidade alta,
                // independente de a ferramenta não expor um campo de severidade.
                Severity: "HIGH",
                Title: item.TryGetProperty("RuleID", out var rule) ? rule.GetString() ?? "secret detectado" : "secret detectado",
                RuleId: item.TryGetProperty("RuleID", out var ruleId) ? ruleId.GetString() : null,
                FilePath: item.TryGetProperty("File", out var file) ? file.GetString() : null,
                Line: item.TryGetProperty("StartLine", out var line) && line.ValueKind == JsonValueKind.Number ? line.GetInt32() : null,
                Description: item.TryGetProperty("Description", out var desc) ? desc.GetString() : null));
        }

        return findings;
    }
}
