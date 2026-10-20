using System.Diagnostics;

namespace SecurityScanner.Cli.Targets;

/// <summary>Resolve o diretório a escanear: pasta local existente ou clone raso de um repositório git.</summary>
public static class TargetResolver
{
    public static async Task<string> ResolveAsync(string? repoUrl, string? targetPath, string targetsRoot, CancellationToken ct)
    {
        if (targetPath is not null)
        {
            var fullPath = Path.GetFullPath(targetPath);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"--target-path não existe: {fullPath}");
            return fullPath;
        }

        if (repoUrl is null)
            throw new ArgumentException("Informe --repo-url ou --target-path.");

        var name = Path.GetFileNameWithoutExtension(repoUrl.TrimEnd('/'));
        var cloneDir = Path.Combine(targetsRoot, name);

        // Sempre um clone novo: um clone antigo deixado em targets/ escanearia código desatualizado.
        if (Directory.Exists(cloneDir))
            Directory.Delete(cloneDir, recursive: true);
        Directory.CreateDirectory(targetsRoot);

        var psi = new ProcessStartInfo("git") { UseShellExecute = false };
        foreach (var arg in new[] { "clone", "--depth", "1", repoUrl, cloneDir })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o git.");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git clone falhou para {repoUrl} (código {process.ExitCode}).");

        return cloneDir;
    }
}
