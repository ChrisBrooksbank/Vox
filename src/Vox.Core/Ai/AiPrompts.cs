using System.Globalization;

namespace Vox.Core.Ai;

/// <summary>
/// The instructions every describer gives its model, and the text sent with each request. The
/// answers are read aloud by a screen reader, so they are asked for as short plain text.
/// </summary>
public static class AiPrompts
{
    /// <summary>The most page text sent for a summary (characters); longer pages are cut, and the model is told.</summary>
    public const int MaxContextLength = 200_000;

    /// <summary>The question asked of a picture for an automatic label (<see cref="Labels.AutomaticLabeler"/>).</summary>
    public const string LabelQuestion =
        "This is a button, link or picture on a web page that has no text label. Give it a short label, as a " +
        "screen reader would read it: what it does if it is a control (\"Search\", \"Close\", \"Next slide\"), " +
        "else what it shows. A few words only, no full sentence, no full stop.";

    /// <summary>The system instructions for <paramref name="request"/>.</summary>
    public static string Instructions(AiRequest request)
    {
        var language = LanguageName(request.Language);
        var common =
            "You are helping a blind person who uses the Vox screen reader. Your answer will be read aloud by " +
            "speech and shown on a braille display, so write plain text only: no Markdown, headings, tables, " +
            "bullet symbols or emoji. Be accurate; if something can't be made out, say so rather than guess. " +
            $"Answer in {language}.";
        return request.Task switch
        {
            AiTask.DescribeImage =>
                common + " Describe the picture: start with a one-sentence overview, then the important details " +
                "(people, objects, setting, colours where they matter, any text shown, word for word). Keep it under " +
                "about 120 words unless the picture is mostly text. Don't start with \"The image shows\".",
            AiTask.ExplainScreen =>
                common + " You are given a screenshot of the window in front and the text of its controls. Answer " +
                "the person's question about it. When they ask how to do something, give keyboard steps where you " +
                "can (Tab, arrow keys, shortcuts), naming controls by the names in the control list. Be brief.",
            AiTask.SummarizePage =>
                common + " You are given the text of a web page. Summarise it in at most five short sentences: what " +
                "the page is, its main points, and anything the reader is asked to do. Then, on a new line, say " +
                "where the main content starts, quoting the first words of that part exactly.",
            AiTask.AskPage =>
                common + " You are given the text of a web page, one line per line, each starting with its number in " +
                "brackets, such as [12]. Answer the person's question from the page's text only; if the page doesn't " +
                "say, say so. Be brief, and don't mention the line numbers in your answer. When the answer is at one " +
                "place on the page the person may want to go to (a heading, link, button, field or passage), end " +
                "with one more line, exactly \"GO TO: [n]\", n being that line's number.",
            AiTask.GuideMe =>
                common + " You are given the text of a web page, one line per line, each starting with its number in " +
                "brackets, such as [12], and a task the person wants to do on it. Give the steps as a numbered list " +
                "(\"1. ...\"), one short step per line, at most ten. Name each element by its text and its role " +
                "(link, button, edit field, check box, heading), never by colour, position or appearance. Where the " +
                "person must press a key, say it: " + KeyAdvice(request) + " End a step that is at one place on the page " +
                "with \" GO TO: [n]\", n being that line's number. If the page doesn't seem to allow the task, or you " +
                "are not sure of a step, add a last line starting \"Not sure:\" saying what is uncertain.",
            _ => common,
        };
    }

    /// <summary>The text sent with the picture (the question, the window's or page's text).</summary>
    public static string UserText(AiRequest request)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            var context = request.Context.Length > MaxContextLength
                ? request.Context[..MaxContextLength] + "\n[The rest of the text was left out: it is too long.]"
                : request.Context;
            var heading = request.Task is AiTask.SummarizePage or AiTask.AskPage or AiTask.GuideMe ? "Page text:" : "Controls and text of the window:";
            parts.Add($"{heading}\n{context}");
        }
        if (!string.IsNullOrWhiteSpace(request.Question))
            parts.Add($"{(request.Task == AiTask.GuideMe ? "Task" : "Question")}: {request.Question.Trim()}");
        else if (request.Task == AiTask.DescribeImage)
            parts.Add("Describe this picture.");
        else if (request.Task == AiTask.SummarizePage)
            parts.Add("Summarise this page.");
        return string.Join("\n\n", parts);
    }

    // The browse-mode keys the steps may use, and the browser, as Vox knows them
    private static string KeyAdvice(AiRequest request) =>
        "in Vox's browse mode, H and Shift+H move by heading, K by link, B by button, E by edit field, F by form " +
        "field, T by table, Enter or Space activates, Insert+F7 lists the page's elements" +
        (string.IsNullOrWhiteSpace(request.Browser) ? "." : $"; the browser is {request.Browser}.");

    private static string LanguageName(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return "English";
        try { return CultureInfo.GetCultureInfo(tag).EnglishName; }
        catch (CultureNotFoundException) { return tag; }
    }
}
