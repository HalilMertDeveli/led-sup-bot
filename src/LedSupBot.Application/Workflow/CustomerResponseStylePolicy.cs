using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow;

public static partial class CustomerResponseStylePolicy
{
    public const string GenerationInstruction = """
        CUSTOMER RESPONSE STYLE (mandatory): Answer first, with no greeting or filler. For acknowledgement/wait use one short sentence; for a simple question use 1-2 short sentences; for a normal technical answer use 2-3 short sentences. Troubleshooting gives one action/check at a time. Only expand when the customer explicitly asks for detail. Never begin with Elbette, Tabii ki, Harika bir soru, Memnuniyetle, Anladığım kadarıyla, Öncelikle belirtmek isterim, or Size yardımcı olabilmem için. Never claim an action was started or completed unless backend state proves it.
        """;

    public static bool RequestsDetail(string? text) =>
        !string.IsNullOrWhiteSpace(text) && DetailRequestRegex().IsMatch(text);

    [GeneratedRegex(@"(?i)\b(detayl[ıi]|tüm özellik\w*|tum ozellik\w*|madde madde|ayrıntılı|ayrintili)\b")]
    private static partial Regex DetailRequestRegex();
}
