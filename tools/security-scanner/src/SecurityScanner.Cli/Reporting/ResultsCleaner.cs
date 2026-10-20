namespace SecurityScanner.Cli.Reporting;

internal static class ResultsCleaner
{
    /// <summary>Mantém só os `keepLast` runs mais recentes em results/, apagando os demais.</summary>
    public static void PruneOldRuns(string resultsRoot, int keepLast)
    {
        if (!Directory.Exists(resultsRoot))
            return;

        // Nomes de run são "yyyyMMdd-HHmmss", então ordenação alfabética == ordenação cronológica.
        var runDirs = Directory.GetDirectories(resultsRoot)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .ToList();

        foreach (var oldRun in runDirs.Skip(keepLast))
        {
            Console.WriteLine($"[cleanup] removendo run antigo: {oldRun}");
            Directory.Delete(oldRun, recursive: true);
        }
    }
}
