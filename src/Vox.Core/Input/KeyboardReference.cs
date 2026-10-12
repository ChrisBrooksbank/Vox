using System.Net;
using System.Text;
using Vox.Core.Configuration;
using Vox.Core.Localization;

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
        // App modules' keys are the same in both layouts; modules can share a key, so one editor each
        var appKeys = AppModules.AppModuleKeyMaps.ModuleIds().Select(id => new GestureEditor(AppModules.AppModuleKeyMaps.Load(id))).ToList();
        var html = new StringBuilder();
        var title = Encode(VoxStrings.Get("KeyboardReference.Title"));
        // The introduction is HTML (it links back to the guide)
        html.Append($"""
            <!doctype html>
            <html lang="{Encode(VoxStrings.Get("KeyboardReference.Lang"))}"{(VoxStrings.IsRightToLeft ? " dir=\"rtl\"" : string.Empty)}>
            <head>
            <meta charset="utf-8">
            <title>{title}</title>
            <link rel="stylesheet" href="guide.css">
            </head>
            <body>
            <main>
            <h1>{title}</h1>
            <p>{VoxStrings.Get("KeyboardReference.Intro")}</p>

            """);
        foreach (var category in CommandCatalog.All.GroupBy(c => c.Category))
        {
            var name = CommandCatalog.CategoryName(category.Key);
            html.Append($"<h2>{Encode(name)}</h2>\n<table>\n<caption>{Encode(name)}</caption>\n")
                .Append($"<tr><th scope=\"col\">{Encode(VoxStrings.Get("KeyboardReference.Command"))}</th><th scope=\"col\">{Encode(VoxStrings.Get("KeyboardReference.Desktop"))}</th>")
                .Append($"<th scope=\"col\">{Encode(VoxStrings.Get("KeyboardReference.Laptop"))}</th><th scope=\"col\">{Encode(VoxStrings.Get("KeyboardReference.WhatItDoes"))}</th></tr>\n");
            foreach (var info in category)
            {
                html.Append("<tr><th scope=\"row\">").Append(Encode(info.Name)).Append("</th>")
                    .Append("<td>").Append(info.Category == CommandCategory.Applications ? Keys(appKeys, info.Command) : Keys([desktopKeys], info.Command)).Append("</td>")
                    .Append("<td>").Append(info.Category == CommandCategory.Applications ? Keys(appKeys, info.Command) : Keys([laptopKeys], info.Command)).Append("</td>")
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

    private static string Keys(IReadOnlyList<GestureEditor> editors, NavigationCommand command)
    {
        var gestures = editors.SelectMany(keys => keys.GesturesFor(command)).ToList();
        return gestures.Count == 0
            ? Encode(VoxStrings.Get("KeyboardReference.NoKeys"))
            : string.Join("<br>", gestures.Select(g => $"<kbd>{Encode(g.Describe())}</kbd>"));
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
