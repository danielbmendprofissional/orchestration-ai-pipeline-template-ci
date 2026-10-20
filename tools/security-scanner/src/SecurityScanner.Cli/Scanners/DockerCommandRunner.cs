using System.Diagnostics;

namespace SecurityScanner.Cli.Scanners;

internal static class DockerCommandRunner
{
    /// <summary>Nome padrão dos containers lançados por este CLI (ex: "security-scanner-trivy").</summary>
    public static string ContainerName(string tool) => $"security-scanner-{tool}";

    /// <summary>Remove qualquer container remanescente com esse nome antes de rodar (ex: após um crash sem --rm).</summary>
    public static Task RemoveContainerIfExistsAsync(string name, CancellationToken ct) =>
        RunAsync(["rm", "-f", name], ct, silent: true);

    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct, bool silent = false)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        if (!silent)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        }

        process.Start();

        if (!silent)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
