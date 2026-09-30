using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    private static void AddRegex(Dictionary<string, Entry> table)
    {
        Add(table, "regex", 2, 2, TerraformRegex.Regex);
        Add(table, "regexall", 2, 2, TerraformRegex.RegexAll);
    }
}
