using System.Net;
using System.Text;
using Vox.Core.Configuration;
using Vox.Core.Localization;

namespace Vox.Core.Input;

/// <summary>
/// The user guide's keyboard reference: every command by category, with its keys in the desktop
/// and laptop layouts and what it does. Generated from the keymaps and <see cref="CommandCatalog"/>
/// when Vox is built (<c>Vox.App --keyboard-reference</c>), so it never falls out of date; the
/// JAWS layouts have a page of their own (<see cref="JawsFileName"/>, beside it).
/// </summary>
public static class KeyboardReference
{
    /// <summary>The JAWS layouts' reference page, written beside the main one.</summary>
    public const string JawsFileName = "keyboard-jaws.html";

    public static string Html(IReadOnlyList<KeyBinding> desktop, IReadOnlyList<KeyBinding> laptop) =>
        Html("KeyboardReference.Title", "KeyboardReference.Intro",
            [(VoxStrings.Get("KeyboardReference.Desktop"), desktop), (VoxStrings.Get("KeyboardReference.Laptop"), laptop)]);

    /// <summary>The reference for the keymaps in <paramref name="configDirectory"/> (assets/config).</summary>
    public static string Html(string configDirectory) =>
        Html(KeyMap.LayoutBindings(configDirectory, KeyboardLayout.Desktop), KeyMap.LayoutBindings(configDirectory, KeyboardLayout.Laptop));

    /// <summary>The reference for the JAWS desktop and laptop layouts in <paramref name="configDirectory"/>.</summary>
    public static string JawsHtml(string configDirectory) =>
        Html("KeyboardReference.JawsTitle", "KeyboardReference.JawsIntro",
            [(VoxStrings.Get("KeyboardReference.JawsDesktop"), KeyMap.LayoutBindings(configDirectory, KeyboardLayout.Jaws)),
             (VoxStrings.Get("KeyboardReference.JawsLaptop"), KeyMap.LayoutBindings(configDirectory, KeyboardLayout.JawsLaptop))]);

    private static string Html(string titleName, string introName, IReadOnlyList<(string Name, IReadOnlyList<KeyBinding> Bindings)> layouts)
    {
        var layoutKeys = layouts.Select(l => new GestureEditor(l.Bindings)).ToList();
        // App modules' keys are the same in every layout; modules can share a key, so one editor each
        var appKeys = AppModules.AppModuleKeyMaps.ModuleIds().Select(id => new GestureEditor(AppModules.AppModuleKeyMaps.Load(id))).ToList();
        var html = new StringBuilder();
        var title = Encode(VoxStrings.Get(titleName));
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
            <p>{VoxStrings.Get(introName)}</p>

            """);
        foreach (var category in CommandCatalog.All.GroupBy(c => c.Category))
        {
            var name = CommandCatalog.CategoryName(category.Key);
            html.Append($"<h2>{Encode(name)}</h2>\n<table>\n<caption>{Encode(name)}</caption>\n")
                .Append($"<tr><th scope=\"col\">{Encode(VoxStrings.Get("KeyboardReference.Command"))}</th>");
            foreach (var layout in layouts)
                html.Append($"<th scope=\"col\">{Encode(layout.Name)}</th>");
            html.Append($"<th scope=\"col\">{Encode(VoxStrings.Get("KeyboardReference.WhatItDoes"))}</th></tr>\n");
            foreach (var info in category)
            {
                html.Append("<tr><th scope=\"row\">").Append(Encode(info.Name)).Append("</th>");
                foreach (var keys in layoutKeys)
                    html.Append("<td>").Append(info.Category == CommandCategory.Applications ? Keys(appKeys, info.Command) : Keys([keys], info.Command)).Append("</td>");
                html.Append("<td>").Append(Encode(info.Description)).Append("</td></tr>\n");
            }
            html.Append("</table>\n");
        }
        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static string Keys(IReadOnlyList<GestureEditor> editors, NavigationCommand command)
    {
        var gestures = editors.SelectMany(keys => keys.GesturesFor(command)).ToList();
        return gestures.Count == 0
            ? Encode(VoxStrings.Get("KeyboardReference.NoKeys"))
            : string.Join("<br>", gestures.Select(g => $"<kbd>{Encode(g.Describe())}</kbd>"));
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
