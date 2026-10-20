using System.Text.Json;
using SecurityScanner.Cli.Reporting;

namespace SecurityScanner.Cli.Scanners;

public sealed class OpengrepRunner : IScanRunner
{
    private const string ToolName = "opengrep";
    private const string ImageTag = "local/opengrep:template-ci";
    private const string RulesRoot = "/opt/opengrep-rules";

    // Mesmo default do job de CI (security/opengrep.yml) — local e esteira acham as mesmas coisas.
    private static readonly string[] DefaultRulesets = ["csharp", "typescript", "javascript", "html", "dockerfile", "generic"];

    public string Name => ToolName;

    public async Task<ScanOutcome> RunAsync(string targetDir, string resultsDir, CancellationToken ct)
    {
        // Não existe imagem Docker oficial do Opengrep: usamos a mesma imagem que o
        // template-ci publica (tools/opengrep/Dockerfile), construída localmente na primeira execução.
        await EnsureImageAsync(ct);

        var outFile = Path.Combine(resultsDir, "opengrep.json");
        var containerName = DockerCommandRunner.ContainerName(ToolName);
        await DockerCommandRunner.RemoveContainerIfExistsAsync(containerName, ct);

        var args = new List<string>
        {
            "run", "--rm", "--name", containerName,
            "-v", $"{targetDir}:/src:ro",
            "-v", $"{resultsDir}:/results",
            ImageTag,
            "opengrep", "scan",
        };
        foreach (var ruleset in DefaultRulesets)
            args.AddRange(["--config", $"{RulesRoot}/{ruleset}"]);
        args.AddRange(["--json", "--output", "/results/opengrep.json", "/src"]);

        await DockerCommandRunner.RunAsync(args, ct);

        if (!File.Exists(outFile))
            return new ScanOutcome(ToolName, false, null, [], "Opengrep não gerou arquivo de resultado.");

        return new ScanOutcome(ToolName, true, outFile, Parse(outFile), null);
    }

    private static async Task EnsureImageAsync(CancellationToken ct)
    {
        var inspectExitCode = await DockerCommandRunner.RunAsync(["image", "inspect", ImageTag], ct, silent: true);
        if (inspectExitCode == 0)
            return;

        var contextDir = Path.Combine(Directory.GetCurrentDirectory(), "tools", "opengrep");
        Console.WriteLine($"[opengrep] imagem local não encontrada, construindo a partir de {contextDir}...");

        var buildExitCode = await DockerCommandRunner.RunAsync(["build", "-t", ImageTag, contextDir], ct);

        if (buildExitCode != 0)
            throw new InvalidOperationException(
                "Falha ao construir a imagem local do Opengrep. Rode o comando a partir da raiz do template-ci.");
    }

    internal static IReadOnlyList<Finding> Parse(string path)
    {
        var findings = new List<Finding>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return findings;

        foreach (var r in results.EnumerateArray())
        {
            var extra = r.GetProperty("extra");
            int? line = null;
            if (r.TryGetProperty("start", out var start) && start.TryGetProperty("line", out var lineEl))
                line = lineEl.GetInt32();

            findings.Add(new Finding(
                Tool: ToolName,
                Severity: extra.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "UNKNOWN" : "UNKNOWN",
                Title: extra.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                RuleId: r.TryGetProperty("check_id", out var id) ? id.GetString() : null,
                FilePath: r.TryGetProperty("path", out var p) ? p.GetString() : null,
                Line: line,
                Description: null));
        }

        return findings;
    }
}
