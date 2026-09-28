// Writing job requests from a JobPhraseBook (the original's random jobs keep their words that way).

using System.Text;
using System.Text.RegularExpressions;
using OpenGG.Core.Content;

namespace OpenGG.Core.Sim;

/// <summary>What a sentence is about: the region, how bad it is to be told (0 the mildest words … 1 the
/// strongest), how much of the region is damaged (0 … 1, with <see cref="Parts"/> parts) and the part it names,
/// if any.</summary>
public sealed record PhraseTopic(View Region, double Severity, double Share, int Parts, string? PartName = null);

/// <summary>Expands the %Variables of a phrase book's texts and puts the pieces together.</summary>
public sealed partial class PhraseWriter(JobPhraseBook book, Func<int, int> pick)
{
    [GeneratedRegex(@"%([A-Za-z][A-Za-z0-9]*)")]
    private static partial Regex Variable();

    /// <summary>The text with its variables replaced by words that fit the topic.</summary>
    public string Expand(string text, PhraseTopic topic, int depth = 0) =>
        Variable().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (name == "PartName") return topic.PartName ?? "part";
            if (!book.Variables.TryGetValue(name, out var v) || v.Words.Count == 0) return m.Value;
            var word = v.Words[Choose(v, topic)];
            return depth < 4 ? Expand(word, topic, depth + 1) : word;
        });

    int Choose(PhraseVariable v, PhraseTopic topic)
    {
        int n = v.Words.Count;
        return v.Choice switch
        {
            PhraseChoice.Condition => Band(topic.Severity, n),
            // By quarters of the region (the original said "one or two" of three damaged parts too).
            PhraseChoice.Share => Band(topic.Share, n),
            PhraseChoice.Region => Math.Clamp((int)topic.Region, 0, n - 1),
            _ => pick(n),
        };
    }

    static int Band(double x, int n) => Math.Clamp((int)Math.Floor(x * n), 0, n - 1);

    /// <summary>
    /// Pieces (the customer's opening, what is wrong, the closing) put together as the original does: one space
    /// between them, none after a piece that ends in one ("... but "), and each piece starts with a capital
    /// unless it runs on that way. Inside a piece, a sentence that starts in lower case after ". ", "! " or
    /// "? " gets a capital and a second space ("hey? are you there?" → "Hey?  Are you there?");
    /// one that starts with a capital already keeps its single space. Only first letters change.
    /// </summary>
    public static string Join(IEnumerable<string> pieces)
    {
        var sb = new StringBuilder();
        foreach (var p in pieces)
        {
            if (p.Length == 0) continue;
            bool runOn = sb.Length > 0 && char.IsWhiteSpace(sb[^1]);
            if (sb.Length > 0 && !runOn) sb.Append(' ');
            sb.Append(Piece(p, capital: !runOn));
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Job Update's words for a random job (measured): only what is wrong, without the customer's opening and
    /// closing, every sentence starting with a capital after a single space ("... loud. And my seat ...",
    /// where the Job Request says "... loud.  And my seat ...").
    /// </summary>
    public static string Hint(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool start = true;
        foreach (char c in text.Trim())
        {
            if (start && char.IsLetter(c))
            {
                sb.Append(char.ToUpperInvariant(c));
                start = false;
                continue;
            }
            if (char.IsLetterOrDigit(c)) start = false;
            else if (c is '.' or '!' or '?') start = true;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>One piece: its first letter a capital (if asked), and every lower-case sentence start inside
    /// it made a capital with two spaces before it.</summary>
    public static string Piece(string text, bool capital = true)
    {
        var sb = new StringBuilder(text.Length + 4);
        bool first = capital;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (first && char.IsLetter(c))
            {
                sb.Append(char.ToUpperInvariant(c));
                first = false;
                continue;
            }
            if (char.IsLetterOrDigit(c)) first = false;
            if (c is '.' or '!' or '?' && i + 2 < text.Length && text[i + 1] == ' ' && char.IsLower(text[i + 2]))
            {
                sb.Append(c).Append("  ").Append(char.ToUpperInvariant(text[i + 2]));
                i += 2;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
