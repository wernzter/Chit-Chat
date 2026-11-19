using System.Text;
using System.Text.RegularExpressions;

namespace ChitChat.Dictionary;

/// <summary>Reports the first matched spelling and number of occurrences replaced by a rule.</summary>
/// <param name="From">Text from the recognition result.</param>
/// <param name="To">Spelling inserted by the rule.</param>
/// <param name="Count">Number of substitutions made.</param>
public sealed record AppliedCorrection(string From, string To, int Count);

/// <summary>Applies a snapshot of the user's correction rules to recognized speech.</summary>
public sealed class DictionaryCorrector
{
    private static readonly char[] Separators = [' ', '\t', '-'];
    private static readonly TimeSpan RuleBudget = TimeSpan.FromSeconds(1);

    private readonly (Regex Matcher, string Text)[] _rules;

    /// <summary>Prepares the correction rules in precedence order.</summary>
    /// <param name="entries">Current dictionary entries.</param>
    public DictionaryCorrector(IEnumerable<DictionaryEntry> entries)
    {
        var rules = new List<(Regex, string)>();
        foreach (var entry in entries.OrderByDescending(entry => entry.Hear.Length))
        {
            if (!entry.IsEnabled || entry.Kind != EntryKind.Correction) continue;

            var words = entry.Hear.Normalize().Trim().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            const string wordCharacter = @"[\p{L}\p{N}]";
            var phrase = string.Join(@"[-\s]*", words.Select(Regex.Escape));
            var matcher = new Regex("(?<!" + wordCharacter + ")" + phrase + "(?!" + wordCharacter + ")",
                                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RuleBudget);
            rules.Add((matcher, entry.Write));
        }

        _rules = [..rules];
    }

    /// <summary>Whether there is no rule available for replacement.</summary>
    public bool IsEmpty => _rules.Length == 0;

    /// <summary>Returns the corrected text and a log of rules that fired.</summary>
    /// <param name="text">The original utterance.</param>
    public (string Text, IReadOnlyList<AppliedCorrection> Applied) Apply(string text)
    {
        if (IsEmpty || text.Length == 0) return (text, []);

        var corrected = text.Normalize(NormalizationForm.FormC);
        var history = new List<AppliedCorrection>();
        foreach (var (matcher, spelling) in _rules)
        {
            string? observed = null;
            var occurrences = 0;
            corrected = matcher.Replace(corrected, match =>
            {
                observed ??= match.Value;
                occurrences++;
                return spelling;
            });
            if (observed is not null) history.Add(new AppliedCorrection(observed, spelling, occurrences));
        }

        return (corrected, history);
    }

    /// <summary>Maximum number of phrases sent to speech recognition as context.</summary>
    public const int BiasLimit = 40;

    /// <summary>Returns a bounded list of preferred spellings.</summary>
    /// <param name="entries">Current dictionary entries.</param>
    public static IReadOnlyList<string> BiasPhrases(IEnumerable<DictionaryEntry> entries) =>
        entries.Where(entry => entry.IsEnabled)
               .Select(entry => entry.Write.Trim())
               .Where(spelling => spelling.Length > 0)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .Take(BiasLimit)
               .ToArray();
}
