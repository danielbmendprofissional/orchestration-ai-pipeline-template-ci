using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecurityScanner.Cli.Reporting;

public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteAsync(ConsolidatedReport report, string path, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, Options, ct);
    }
}
