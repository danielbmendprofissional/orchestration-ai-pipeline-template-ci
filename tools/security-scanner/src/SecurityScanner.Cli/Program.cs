using System.CommandLine;
using SecurityScanner.Cli.Reporting;
using SecurityScanner.Cli.Scanners;
using SecurityScanner.Cli.Targets;

var repoRoot = Directory.GetCurrentDirectory();

// ============================================================
// = scan — roda as ferramentas via Docker (uso local)
// ============================================================

var repoUrlOption = new Option<string?>("--repo-url")
{
    Description = "URL de um repositório git para clonar e escanear",
};
var targetPathOption = new Option<string?>("--target-path")
{
    Description = "Caminho local para escanear",
};
var toolsOption = new Option<string>("--tools")
{
    Description = "Ferramentas a rodar, separadas por vírgula (trivy,opengrep,checkov,betterleaks,zap). " +
                  "zap escaneia uma aplicação em execução e requer --app-url.",
    DefaultValueFactory = _ => "trivy,opengrep,checkov,betterleaks",
};
var appUrlOption = new Option<string?>("--app-url")
{
    Description = "URL de uma aplicação em execução, para ferramentas DAST (ex: zap). " +
                  "Se o alvo roda no host (fora do Docker), use http://host.docker.internal:PORTA em vez de localhost.",
};
var exportOption = new Option<string>("--export")
{
    Description = "Formatos de exportação consolidada, separados por vírgula (json,pdf)",
    DefaultValueFactory = _ => "json",
};
var keepLastOption = new Option<int?>("--keep-last")
{
    Description = "Mantém só os N runs mais recentes em results/, apagando os mais antigos ao final deste scan.",
};

var scanCommand = new Command("scan", "Roda os scanners de segurança (via Docker) contra um alvo local ou remoto")
{
    repoUrlOption,
    targetPathOption,
    toolsOption,
    appUrlOption,
    exportOption,
    keepLastOption,
};

scanCommand.SetAction(async (parseResult, ct) =>
{
    var repoUrl = parseResult.GetValue(repoUrlOption);
    var targetPath = parseResult.GetValue(targetPathOption);
    var toolNames = SplitList(parseResult.GetValue(toolsOption));
    var appUrl = parseResult.GetValue(appUrlOption);
    var exportFormats = SplitList(parseResult.GetValue(exportOption));
    var keepLast = parseResult.GetValue(keepLastOption);

    var availableRunners = new Dictionary<string, IScanRunner>(StringComparer.OrdinalIgnoreCase)
    {
        ["trivy"] = new TrivyRunner(),
        ["opengrep"] = new OpengrepRunner(),
        ["checkov"] = new CheckovRunner(),
        ["betterleaks"] = new BetterLeaksRunner(),
    };
    var availableNetworkRunners = new Dictionary<string, INetworkScanRunner>(StringComparer.OrdinalIgnoreCase)
    {
        ["zap"] = new ZapRunner(),
    };

    var sourceToolNames = toolNames.Where(t => availableRunners.ContainsKey(t)).ToList();
    var networkToolNames = toolNames.Where(t => availableNetworkRunners.ContainsKey(t)).ToList();
    var unknownToolNames = toolNames.Except(sourceToolNames, StringComparer.OrdinalIgnoreCase)
        .Except(networkToolNames, StringComparer.OrdinalIgnoreCase).ToList();

    foreach (var unknown in unknownToolNames)
        Console.Error.WriteLine($"Ferramenta desconhecida: {unknown}. Opções: {string.Join(", ", availableRunners.Keys.Concat(availableNetworkRunners.Keys))}");

    if (sourceToolNames.Count > 0 && repoUrl is null && targetPath is null)
    {
        Console.Error.WriteLine($"As ferramentas {string.Join(", ", sourceToolNames)} precisam de --repo-url ou --target-path.");
        return 1;
    }

    if (repoUrl is not null && targetPath is not null)
    {
        Console.Error.WriteLine("Informe apenas um entre --repo-url e --target-path.");
        return 1;
    }

    if (networkToolNames.Count > 0 && appUrl is null)
    {
        Console.Error.WriteLine($"As ferramentas {string.Join(", ", networkToolNames)} precisam de --app-url (a aplicação precisa estar rodando).");
        return 1;
    }

    var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var resultsDir = Path.Combine(repoRoot, "results", runId);
    Directory.CreateDirectory(resultsDir);

    string? targetDir = null;
    if (sourceToolNames.Count > 0)
        targetDir = await TargetResolver.ResolveAsync(repoUrl, targetPath, Path.Combine(repoRoot, "targets"), ct);

    var outcomes = new List<ScanOutcome>();

    foreach (var toolName in sourceToolNames)
    {
        var runner = availableRunners[toolName];
        Console.WriteLine($"=== {runner.Name} ===");
        outcomes.Add(Report(runner.Name, await runner.RunAsync(targetDir!, resultsDir, ct)));
    }

    foreach (var toolName in networkToolNames)
    {
        var runner = availableNetworkRunners[toolName];
        Console.WriteLine($"=== {runner.Name} ===");
        outcomes.Add(Report(runner.Name, await runner.RunAsync(appUrl!, resultsDir, ct)));
    }

    var report = ConsolidatedReportBuilder.Build(runId, targetDir ?? appUrl ?? "(desconhecido)", outcomes);
    await ExportAsync(report, exportFormats, resultsDir, ct);
    PrintSummary(report);

    if (keepLast is > 0)
        ResultsCleaner.PruneOldRuns(Path.Combine(repoRoot, "results"), keepLast.Value);

    return 0;
});

// ============================================================
// = aggregate — junta relatórios brutos e aplica o gate (CI)
// ============================================================

var inputOption = new Option<string>("--input")
{
    Description = "Pasta com os relatórios brutos (trivy-fs.json, trivy-image-*.json, opengrep.json, checkov.json, betterleaks.json, zap.json)",
    DefaultValueFactory = _ => ".",
};
var outputOption = new Option<string>("--output")
{
    Description = "Pasta de saída do relatório consolidado",
    DefaultValueFactory = _ => "security-report",
};
var failOnOption = new Option<string>("--fail-on")
{
    Description = "Severidades que reprovam o gate, separadas por vírgula (CRITICAL,HIGH,MEDIUM,LOW)",
    DefaultValueFactory = _ => "CRITICAL,HIGH",
};
var warnOnlyOption = new Option<bool>("--warn-only")
{
    Description = "Gate reprovado vira aviso: sai com código 3 em vez de 2 (o job de CI trata 3 como allow_failure)",
};
var ignoreFileOption = new Option<string?>("--ignore-file")
{
    Description = "Arquivo de falsos positivos aceitos (uma regra por linha: ruleId ou ferramenta:ruleId)",
};
var targetOption = new Option<string>("--target")
{
    Description = "Rótulo do alvo no relatório (ex: grupo/projeto@v1.2.0-rc1)",
    DefaultValueFactory = _ => "(ci)",
};
var aggregateExportOption = new Option<string>("--export")
{
    Description = "Formatos de exportação consolidada, separados por vírgula (json,pdf)",
    DefaultValueFactory = _ => "json,pdf",
};

var aggregateCommand = new Command("aggregate", "Consolida relatórios já gerados (jobs de CI) e aplica o gate de severidade")
{
    inputOption,
    outputOption,
    failOnOption,
    warnOnlyOption,
    ignoreFileOption,
    targetOption,
    aggregateExportOption,
};

aggregateCommand.SetAction(async (parseResult, ct) =>
{
    var inputDir = Path.GetFullPath(parseResult.GetValue(inputOption)!);
    var outputDir = Path.GetFullPath(parseResult.GetValue(outputOption)!);
    var warnOnly = parseResult.GetValue(warnOnlyOption);
    var ignoreFile = parseResult.GetValue(ignoreFileOption);
    var exportFormats = SplitList(parseResult.GetValue(aggregateExportOption));

    var failOn = SplitList(parseResult.GetValue(failOnOption))
        .Select(ConsolidatedReportBuilder.NormalizeSeverity)
        .ToHashSet();
    if (failOn.Contains("UNKNOWN"))
    {
        Console.Error.WriteLine("--fail-on aceita só CRITICAL, HIGH, MEDIUM, LOW.");
        return 1;
    }

    if (!Directory.Exists(inputDir))
    {
        Console.Error.WriteLine($"--input não existe: {inputDir}");
        return 1;
    }

    IgnoreRules? ignoreRules = null;
    if (ignoreFile is not null)
    {
        if (!File.Exists(ignoreFile))
        {
            Console.Error.WriteLine($"--ignore-file não existe: {ignoreFile}");
            return 1;
        }
        ignoreRules = IgnoreRules.Load(ignoreFile);
        Console.WriteLine($"[gate] {ignoreRules.Count} regra(s) de falso positivo carregada(s) de {ignoreFile}");
    }

    var outcomes = RawReportCollector.Collect(inputDir);
    if (outcomes.Count == 0)
    {
        Console.Error.WriteLine($"[gate] Nenhum relatório reconhecido em {inputDir} — os jobs de scan rodaram e publicaram artifacts?");
        return 1;
    }

    var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var report = ConsolidatedReportBuilder.Build(runId, parseResult.GetValue(targetOption)!, outcomes, ignoreRules);

    Directory.CreateDirectory(outputDir);
    await ExportAsync(report, exportFormats, outputDir, ct);
    PrintSummary(report);

    var blocking = report.Findings
        .Where(f => failOn.Contains(ConsolidatedReportBuilder.NormalizeSeverity(f.Severity)))
        .OrderBy(ConsolidatedReportBuilder.SeverityRank)
        .ToList();
    var unreadable = report.ToolOutcomes.Where(o => !o.Success).ToList();

    Console.WriteLine();
    if (blocking.Count == 0 && unreadable.Count == 0)
    {
        Console.WriteLine($"[gate] APROVADO — nenhum achado {string.Join("/", failOn)}.");
        return 0;
    }

    foreach (var outcome in unreadable)
        Console.WriteLine($"[gate] relatório de {outcome.Tool} inválido: {outcome.ErrorMessage}");

    Console.WriteLine($"[gate] {blocking.Count} achado(s) {string.Join("/", failOn)}:");
    foreach (var f in blocking.Take(30))
    {
        var location = f.FilePath is null ? "" : f.Line is null ? $" — {f.FilePath}" : $" — {f.FilePath}:{f.Line}";
        Console.WriteLine($"  [{f.Tool}] {ConsolidatedReportBuilder.NormalizeSeverity(f.Severity)} {f.RuleId}: {f.Title}{location}");
    }
    if (blocking.Count > 30)
        Console.WriteLine($"  ... e mais {blocking.Count - 30} (ver relatório consolidado)");

    Console.WriteLine("[gate] Falso positivo? Adicione o ruleId em .security-ignore (com comentário do motivo).");

    if (warnOnly)
    {
        Console.WriteLine("[gate] REPROVADO em modo aviso — não bloqueia.");
        return 3;
    }

    Console.WriteLine("[gate] REPROVADO — deploy bloqueado.");
    return 2;
});

var rootCommand = new RootCommand("Security Scanner CLI — Trivy, Opengrep, Checkov, BetterLeaks e ZAP: scan local via Docker e gate de CI")
{
    scanCommand,
    aggregateCommand,
};

return await rootCommand.Parse(args).InvokeAsync();

static List<string> SplitList(string? value) =>
    (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

static ScanOutcome Report(string name, ScanOutcome outcome)
{
    Console.WriteLine(outcome.Success
        ? $"[{name}] {outcome.Findings.Count} findings"
        : $"[{name}] FALHOU: {outcome.ErrorMessage}");
    return outcome;
}

static async Task ExportAsync(ConsolidatedReport report, IEnumerable<string> formats, string outputDir, CancellationToken ct)
{
    foreach (var format in formats)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                var jsonPath = Path.Combine(outputDir, "consolidated.json");
                await JsonReportWriter.WriteAsync(report, jsonPath, ct);
                Console.WriteLine($"Relatório consolidado (JSON): {jsonPath}");
                break;

            case "pdf":
                var pdfPath = Path.Combine(outputDir, "consolidated.pdf");
                PdfReportWriter.Write(report, pdfPath);
                Console.WriteLine($"Relatório consolidado (PDF): {pdfPath}");
                break;

            default:
                Console.Error.WriteLine($"Formato de exportação desconhecido: {format}");
                break;
        }
    }
}

static void PrintSummary(ConsolidatedReport report)
{
    Console.WriteLine();
    Console.WriteLine("Resumo por severidade:");
    foreach (var (severity, count) in report.CountsBySeverity.OrderBy(kv => ConsolidatedReportBuilder.SeverityRank(kv.Key)))
        Console.WriteLine($"  {severity}: {count}");
    if (report.Suppressed.Count > 0)
        Console.WriteLine($"  (suprimidos por .security-ignore: {report.Suppressed.Count})");
}
