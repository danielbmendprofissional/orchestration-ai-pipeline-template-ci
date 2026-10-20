using System.Text.Json;
using SecurityScanner.Cli.Reporting;

namespace SecurityScanner.Cli.Scanners;

public sealed class CheckovRunner : IScanRunner
{
    private const string ToolName = "checkov";
    private const string UnknownSeverity = "UNKNOWN";

    public string Name => ToolName;

    public async Task<ScanOutcome> RunAsync(string targetDir, string resultsDir, CancellationToken ct)
    {
        var rawOutFile = Path.Combine(resultsDir, "results_json.json");
        var containerName = DockerCommandRunner.ContainerName(ToolName);
        await DockerCommandRunner.RemoveContainerIfExistsAsync(containerName, ct);

        var args = new List<string>
        {
            "run", "--rm", "--name", containerName,
            "-v", $"{targetDir}:/src:ro",
            "-v", $"{resultsDir}:/results",
            "bridgecrew/checkov:3.3.29",
            "-d", "/src", "-o", "json", "--output-file-path", "/results", "--soft-fail",
        };

        await DockerCommandRunner.RunAsync(args, ct);

        if (!File.Exists(rawOutFile))
            return new ScanOutcome(ToolName, false, null, [], "Checkov não gerou arquivo de resultado.");

        var finalOutFile = Path.Combine(resultsDir, "checkov.json");
        File.Copy(rawOutFile, finalOutFile, overwrite: true);

        return new ScanOutcome(ToolName, true, finalOutFile, Parse(finalOutFile), null);
    }

    internal static IReadOnlyList<Finding> Parse(string path)
    {
        var findings = new List<Finding>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // Checkov emite um único objeto quando só há achados de um "check type"
        // (ex: dockerfile) ou um array de objetos (um por check type) quando há mais de um.
        IEnumerable<JsonElement> sections = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : [root];

        foreach (var section in sections)
        {
            if (!section.TryGetProperty("results", out var results))
                continue;
            if (!results.TryGetProperty("failed_checks", out var failedChecks) || failedChecks.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var check in failedChecks.EnumerateArray())
                findings.Add(ParseCheck(check));
        }

        return findings;
    }

    private static Finding ParseCheck(JsonElement check)
    {
        int? line = null;
        if (check.TryGetProperty("file_line_range", out var range) &&
            range.ValueKind == JsonValueKind.Array &&
            range.GetArrayLength() > 0)
        {
            line = range[0].GetInt32();
        }

        return new Finding(
            Tool: ToolName,
            Severity: check.TryGetProperty("severity", out var sev) && sev.ValueKind == JsonValueKind.String
                ? sev.GetString() ?? UnknownSeverity
                : UnknownSeverity,
            Title: check.TryGetProperty("check_name", out var name) ? name.GetString() ?? "" : "",
            RuleId: check.TryGetProperty("check_id", out var id) ? id.GetString() : null,
            FilePath: check.TryGetProperty("file_path", out var fp) ? fp.GetString() : null,
            Line: line,
            Description: check.TryGetProperty("resource", out var resource) ? resource.GetString() : null);
    }
}
