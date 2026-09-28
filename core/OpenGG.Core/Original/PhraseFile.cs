// The original's words for its random jobs: a text file of variables, sentences and customers
// (comments.txt, kept with the random jobs' portraits). Read into a JobPhraseBook.

using System.Text.RegularExpressions;
using OpenGG.Core.Content;

namespace OpenGG.Core.Original;

public static partial class PhraseFile
{
    [GeneratedRegex("""^%(\w+)\s*=\s*(\w+)\s*:(.*)$""")]
    private static partial Regex VariableLine();

    [GeneratedRegex("""^(\w+)\s*:\s*(?:(\w+)\s*:\s*)?"(.*)"\s*$""")]
    private static partial Regex TextLine();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex Quoted();

    /// <summary>
    /// Lines (";" starts a comment):
    /// <list type="bullet">
    /// <item><c>%Name = Random|CondRange|PercRange|RegionName: "word", "word", ...</c></item>
    /// <item><c>Symptom|Cause: Engine|Body|RGear|Complete: "text"</c></item>
    /// <item><c>RegionPerc|PartDam|PartGone: "text"</c></item>
    /// <item>customers: <c>Face: Happy|Sad: "file"</c>, <c>PreReq|PostReq|Thank: "text"</c>; a line whose
    /// field the current customer already has starts the next customer.</item>
    /// </list>
    /// Lines of other kinds are skipped and listed in <paramref name="problems"/>.
    /// </summary>
    /// <param name="faceId">The portrait asset id of a face file named in the text.</param>
    public static JobPhraseBook Parse(string text, Func<string, string> faceId, List<string>? problems = null)
    {
        var book = new JobPhraseBook();
        PhraseCustomer? who = null;
        int n = 0;
        foreach (var raw in text.Split('\n'))
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (VariableLine().Match(line) is { Success: true } v)
            {
                PhraseChoice? choice = v.Groups[2].Value switch
                {
                    "Random" => PhraseChoice.Random,
                    "CondRange" => PhraseChoice.Condition,
                    "PercRange" => PhraseChoice.Share,
                    "RegionName" => PhraseChoice.Region,
                    _ => null,
                };
                var words = Quoted().Matches(v.Groups[3].Value).Select(m => m.Groups[1].Value).ToList();
                if (choice is null || words.Count == 0) problems?.Add($"line {n}: {line}");
                else book.Variables[v.Groups[1].Value] = new PhraseVariable { Choice = choice.Value, Words = words };
                continue;
            }
            if (TextLine().Match(line) is not { Success: true } t)
            {
                problems?.Add($"line {n}: {line}");
                continue;
            }
            string kind = t.Groups[1].Value, sub = t.Groups[2].Value, value = t.Groups[3].Value;
            switch (kind)
            {
                case "Symptom" or "Cause":
                    View? region = sub switch
                    {
                        "Complete" => View.Complete,
                        "Engine" => View.Engine,
                        "Body" => View.Body,
                        "RGear" => View.RunningGear,
                        _ => null,
                    };
                    if (region is null) problems?.Add($"line {n}: {line}");
                    else book.Sentences.Add(new PhraseSentence { Kind = kind == "Symptom" ? PhraseKind.Symptom : PhraseKind.Cause, Region = region, Text = value });
                    break;
                case "RegionPerc":
                    book.Sentences.Add(new PhraseSentence { Kind = PhraseKind.RegionShare, Text = value });
                    break;
                case "PartDam":
                    book.Sentences.Add(new PhraseSentence { Kind = PhraseKind.PartDamaged, Text = value });
                    break;
                case "PartGone":
                    book.Sentences.Add(new PhraseSentence { Kind = PhraseKind.PartMissing, Text = value });
                    break;
                case "Face" when sub is "Happy" or "Sad":
                    bool happy = sub == "Happy";
                    if (who is null || (happy ? who.HappyFace : who.Face).Length > 0) book.Customers.Add(who = new PhraseCustomer());
                    if (happy) who.HappyFace = faceId(value);
                    else who.Face = faceId(value);
                    break;
                case "PreReq" or "PostReq" or "Thank":
                    string have = kind switch { "PreReq" => who?.Before, "PostReq" => who?.After, _ => who?.Thanks } ?? "";
                    if (who is null || have.Length > 0) book.Customers.Add(who = new PhraseCustomer());
                    if (kind == "PreReq") who.Before = value;
                    else if (kind == "PostReq") who.After = value;
                    else who.Thanks = value;
                    break;
                default:
                    problems?.Add($"line {n}: {line}");
                    break;
            }
        }
        foreach (var c in book.Customers)
        {
            if (c.Face.Length == 0) c.Face = c.HappyFace;
            if (c.HappyFace.Length == 0) c.HappyFace = c.Face;
        }
        return book;
    }
}
