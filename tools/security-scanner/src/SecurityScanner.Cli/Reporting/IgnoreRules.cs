namespace SecurityScanner.Cli.Reporting;

/// <summary>
/// Falsos positivos aceitos (.security-ignore). Uma regra por linha:
/// "ruleId" vale pra qualquer ferramenta, "ferramenta:ruleId" só pra ela.
/// "#" inicia comentário. Ferramenta com sufixo (trivy-fs, trivy-image) casa também por "trivy".
/// </summary>
public sealed class IgnoreRules
{
    private readonly HashSet<string> _anyTool = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Tool, string RuleId)> _perTool = [];

    public int Count => _anyTool.Count + _perTool.Count;

    public static IgnoreRules Load(string path)
    {
        var rules = new IgnoreRules();

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.Length == 0)
                continue;

            // ruleIds do Opengrep usam ponto, os do Trivy/Checkov hífen/underscore — ":" só separa a ferramenta.
            var separator = line.IndexOf(':');
            if (separator > 0)
                rules._perTool.Add((line[..separator].ToLowerInvariant(), line[(separator + 1)..].Trim()));
            else
                rules._anyTool.Add(line);
        }

        return rules;
    }

    public bool Matches(Finding finding)
    {
        if (finding.RuleId is null)
            return false;

        if (_anyTool.Contains(finding.RuleId))
            return true;

        var tool = finding.Tool.ToLowerInvariant();
        var family = tool.Split('-', 2)[0];
        return _perTool.Any(r =>
            (r.Tool == tool || r.Tool == family) &&
            string.Equals(r.RuleId, finding.RuleId, StringComparison.OrdinalIgnoreCase));
    }
}
