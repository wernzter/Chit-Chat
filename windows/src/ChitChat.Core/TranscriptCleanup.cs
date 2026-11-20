using System.Text;
using System.Text.RegularExpressions;
using ChitChat.Dictionary;

namespace ChitChat.Core;

/// <summary>Local punctuation and formatting rules for recognized text.</summary>
public static partial class TranscriptCleanup
{
    /// <summary>Removes common speech fillers and applies spoken formatting commands.</summary>
    /// <param name="input">Text returned by the recognizer.</param>
    public static string Format(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = Fillers().Replace(input, string.Empty);
        text = SpokenFormatting().Replace(text, token => token.Value.ToUpperInvariant() switch
        {
            "NEW PARAGRAPH" => "\n\n",
            "NEW LINE" => "\n",
            "OPEN PAREN" => " (",
            _ => ") ",
        });
        text = HorizontalSpace().Replace(text, " ");
        text = AroundBreaks().Replace(text, "\n");
        text = BeforePunctuation().Replace(text, "$1");
        text = RepeatedBreaks().Replace(text, "\n\n").Trim();

        var sentenceStart = true;
        var output = new StringBuilder(text.Length + 1);
        foreach (var character in text)
        {
            output.Append(sentenceStart ? char.ToUpperInvariant(character) : character);
            if (char.IsLetter(character)) sentenceStart = false;
            if (character is '.' or '!' or '?' or '\n') sentenceStart = true;
        }
        if (text.Length != 0 && char.IsLetterOrDigit(text[^1])) output.Append('.');
        return output.ToString();
    }

    /// <summary>Finds isolated, unambiguous word substitutions in an edited transcript.</summary>
    /// <param name="edit">The original and revised text.</param>
    public static IReadOnlyList<DictionaryEntry> LearnFrom(TranscriptEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var before = Tokens(edit.Original);
        var after = Tokens(edit.Edited);
        if (before.Length is 0 or > 1000 || after.Length is 0 or > 1000) return [];

        // Use unchanged words as anchors. A longer replacement is usually a rewrite.
        var common = new ushort[before.Length + 1, after.Length + 1];
        for (var row = before.Length - 1; row >= 0; row--)
        for (var column = after.Length - 1; column >= 0; column--)
        {
            common[row, column] = SameWord(before[row], after[column])
                ? (ushort)(1 + common[row + 1, column + 1])
                : Math.Max(common[row + 1, column], common[row, column + 1]);
        }
        if (common[0, 0] == 0 && before.Length > 1) return [];

        var candidates = new List<(string Heard, string Written)>();
        var i = 0;
        var j = 0;
        var oldStart = 0;
        var newStart = 0;
        while (i < before.Length || j < after.Length)
        {
            if (i < before.Length && j < after.Length && SameWord(before[i], after[j]))
            {
                AddSubstitution(i, j);
                oldStart = ++i;
                newStart = ++j;
            }
            else if (i < before.Length && (j == after.Length || common[i + 1, j] >= common[i, j + 1])) i++;
            else j++;
        }
        AddSubstitution(i, j);

        return candidates.Distinct().GroupBy(pair => pair.Heard, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => DictionaryEntry.Correction(group.Key, group.Single().Written))
            .Where(entry => DictionaryWarning.Check(entry).Count == 0)
            .ToArray();

        void AddSubstitution(int oldEnd, int newEnd)
        {
            if (oldEnd - oldStart != 1 || newEnd - newStart != 1) return;
            var heard = before[oldStart];
            var written = after[newStart];
            if (heard.Length > 64 || written.Length > 64 || !heard.Any(char.IsLetter) || !written.Any(char.IsLetter)) return;
            if (!SameWord(WordCharacters().Replace(heard, ""), WordCharacters().Replace(written, "")))
                candidates.Add((heard, written));
        }
    }

    private static bool SameWord(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase);
    private static string[] Tokens(string text) => Words().Matches(text.Normalize()).Select(match => match.Value).ToArray();

    [GeneratedRegex("(?<![\\w'])(?:uhm|erm|hmm|mhm|um|uh)\\b,?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Fillers();
    [GeneratedRegex("\\b(?:new paragraph|new line|open paren|close paren)\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpokenFormatting();
    [GeneratedRegex("[ \\t]+")]
    private static partial Regex HorizontalSpace();
    [GeneratedRegex("[ \\t]*\\n[ \\t]*")]
    private static partial Regex AroundBreaks();
    [GeneratedRegex(" +([,.!?;:])")]
    private static partial Regex BeforePunctuation();
    [GeneratedRegex("\\n{3,}")]
    private static partial Regex RepeatedBreaks();
    [GeneratedRegex("[\\p{L}\\p{M}\\p{N}]+(?:['’\\-][\\p{L}\\p{M}\\p{N}]+)*")]
    private static partial Regex Words();
    [GeneratedRegex("[^\\p{L}\\p{M}\\p{N}]")]
    private static partial Regex WordCharacters();
}
