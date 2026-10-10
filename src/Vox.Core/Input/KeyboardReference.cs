using System.Net;
using System.Text;
using Vox.Core.Configuration;

namespace Vox.Core.Input;

/// <summary>
/// The user guide's keyboard reference: every command by category, with its keys in the desktop
/// and laptop layouts and what it does. Generated from the keymaps and <see cref="CommandCatalog"/>
/// when Vox is built (<c>Vox.App --keyboard-reference</c>), so it never falls out of date.
/// </summary>
public static class KeyboardReference
{
    public static string Html(IReadOnlyList<KeyBinding> desktop, IReadOnlyList<KeyBinding> laptop)
    {
        var desktopKeys = new GestureEditor(desktop);
        var laptopKeys = new GestureEditor(laptop);
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>Vox keyboard reference</title>
            <link rel="stylesheet" href="guide.css">
            </head>
            <body>
            <main>
            <h1>Vox keyboard reference</h1>
            <p>Every Vox command and its keys. "Insert" is the Vox key: Caps Lock instead if you chose it.
            Keys marked "browse mode" or "focus mode" work only on web pages, in that mode.
            The laptop layout has no numeric keypad keys. <a href="index.html">Back to the user guide</a>.</p>

            """);
        foreach (var category in CommandCatalog.All.GroupBy(c => c.Category))
        {
            var name = CommandCatalog.CategoryName(category.Key);
            html.Append($"<h2>{Encode(name)}</h2>\n<table>\n<caption>{Encode(name)}</caption>\n")
                .Append("<tr><th scope=\"col\">Command</th><th scope=\"col\">Desktop</th><th scope=\"col\">Laptop</th><th scope=\"col\">What it does</th></tr>\n");
            foreach (var info in category)
            {
                html.Append("<tr><th scope=\"row\">").Append(Encode(info.Name)).Append("</th>")
                    .Append("<td>").Append(Keys(desktopKeys, info.Command)).Append("</td>")
                    .Append("<td>").Append(Keys(laptopKeys, info.Command)).Append("</td>")
                    .Append("<td>").Append(Encode(info.Description)).Append("</td></tr>\n");
            }
            html.Append("</table>\n");
        }
        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>The reference for the keymaps in <paramref name="configDirectory"/> (assets/config).</summary>
    public static string Html(string configDirectory) =>
        Html(KeyMap.LayoutBindings(configDirectory, KeyboardLayout.Desktop), KeyMap.LayoutBindings(configDirectory, KeyboardLayout.Laptop));

    private static string Keys(GestureEditor keys, NavigationCommand command)
    {
        var gestures = keys.GesturesFor(command);
        return gestures.Count == 0
            ? "(none; see the Vox menu)"
            : string.Join("<br>", gestures.Select(g => $"<kbd>{Encode(g.Describe())}</kbd>"));
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
