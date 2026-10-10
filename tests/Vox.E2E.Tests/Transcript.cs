using System.Runtime.CompilerServices;
using System.Text;
using Vox.Core.Tests.TestSupport;

namespace Vox.E2E.Tests;

/// <summary>
/// Approved-transcript snapshots: what Vox said in a scenario, one utterance per line with its
/// priority, compared with <c>Transcripts/&lt;name&gt;.approved.txt</c> beside the test. Any
/// difference fails the test and writes <c>&lt;name&gt;.received.txt</c>; if the new speech is
/// right, approve it with <c>tools/approve-transcripts.ps1</c> (which renames received to approved).
/// </summary>
public static class Transcript
{
    public const string ApprovedSuffix = ".approved.txt";
    public const string ReceivedSuffix = ".received.txt";

    /// <summary>The transcript text for these utterances.</summary>
    public static string Format(IEnumerable<SpokenUtterance> spoken)
    {
        var text = new StringBuilder();
        foreach (var utterance in spoken)
            text.Append('[').Append(utterance.Priority).Append("] ").Append(Normalize(utterance.Text)).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// The first difference between an approved and a received transcript ("line 3: expected ...,
    /// got ..."), or null when they match. Line endings don't matter.
    /// </summary>
    public static string? Difference(string approved, string received)
    {
        var expected = Lines(approved);
        var actual = Lines(received);
        for (int i = 0; i < Math.Max(expected.Length, actual.Length); i++)
        {
            var e = i < expected.Length ? expected[i] : "(nothing)";
            var a = i < actual.Length ? actual[i] : "(nothing)";
            if (e != a)
                return $"line {i + 1}: expected \"{e}\", got \"{a}\"";
        }
        return null;
    }

    /// <summary>
    /// Compares what <paramref name="speech"/> recorded with the approved transcript
    /// <paramref name="name"/>; on a difference writes the received one and throws.
    /// </summary>
    public static void Verify(RecordingSpeechEngine speech, string name, [CallerFilePath] string testFile = "") =>
        Verify(Format(speech.Spoken), name, Path.Combine(Path.GetDirectoryName(testFile)!, "Transcripts"));

    public static void Verify(string received, string name, string directory)
    {
        var approvedPath = Path.Combine(directory, name + ApprovedSuffix);
        var receivedPath = Path.Combine(directory, name + ReceivedSuffix);
        string? difference = File.Exists(approvedPath)
            ? Difference(File.ReadAllText(approvedPath), received)
            : "no approved transcript yet";
        if (difference is null)
        {
            File.Delete(receivedPath);
            return;
        }
        Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, received);
        throw new TranscriptMismatchException(
            $"Transcript {name} differs ({difference}). Check {receivedPath}; if it is right, run tools/approve-transcripts.ps1.");
    }

    private static string[] Lines(string text)
    {
        var trimmed = text.Replace("\r\n", "\n").TrimEnd('\n');
        return trimmed.Length == 0 ? [] : trimmed.Split('\n');
    }

    // One utterance per line: line breaks inside one are shown as spaces
    private static string Normalize(string text) => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
}

public sealed class TranscriptMismatchException(string message) : Exception(message);
