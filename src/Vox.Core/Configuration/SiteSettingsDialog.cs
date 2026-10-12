using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Configuration;

/// <summary>Shows the site settings dialog; the site's settings chosen, or null when cancelled.</summary>
public interface ISiteSettingsPresenter
{
    Task<JsonObject?> EditAsync(SiteSettingsEditor editor);
}

/// <summary>Runs <see cref="SiteSettingsDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class SiteSettingsPresenter : ISiteSettingsPresenter
{
    public Task<JsonObject?> EditAsync(SiteSettingsEditor editor)
    {
        var tcs = new TaskCompletionSource<JsonObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new SiteSettingsDialog(editor);
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.OK ? editor.ToJson() : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-SiteSettings",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>A combo box per site setting (the first choice: as usual), with OK and Cancel.</summary>
public sealed class SiteSettingsDialog : VoxDialog
{
    public SiteSettingsDialog(SiteSettingsEditor editor)
    {
        Text = VoxStrings.Format("SiteSettings.Title", editor.SiteName);
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        Control? first = null;
        foreach (var field in editor.Fields)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, AccessibleName = field.Label };
            foreach (var choice in field.Choices)
                box.Items.Add(choice.Text);
            box.SelectedIndex = field.Selected;
            box.SelectedIndexChanged += (_, _) => field.Selected = Math.Max(0, box.SelectedIndex);
            layout.Controls.Add(new Label { Text = field.Label, AutoSize = true });
            layout.Controls.Add(box);
            first ??= box;
        }
        var ok = new Button { Text = VoxStrings.Get("Dialog.OK"), AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = VoxStrings.Get("Dialog.Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = first;
    }
}

/// <summary>
/// Opens the settings of the web site being browsed (Vox menu) and keeps what is chosen, laid
/// over the application's profile while the site's pages are browsed. None on secure screens.
/// Pipeline thread.
/// </summary>
public sealed class SiteSettingsCommands(
    ProfileManager profiles,
    ISiteSettingsPresenter presenter,
    IOptionsMonitor<VoxSettings> settings,
    BrowseModeController browse,
    IEventSink pipeline,
    ILogger<SiteSettingsCommands> logger,
    SpeechQueue? speechQueue = null)
{
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.OpenSiteSettings)
            return false;
        if (!profiles.SitesAvailable)
            Say(VoxStrings.Get("SiteSettings.NotHere"));
        else if (Labels.CustomLabelSites.SiteOf(browse.CurrentAddress) is not { } site)
            Say(VoxStrings.Get("SiteSettings.NoSite"));
        else if (browse.BeginOwnDialog())
            _ = EditAsync(new SiteSettingsEditor(site, profiles.SiteSettingsFor(site), UsualSettings(site)));
        return true;
    }

    /// <summary>Runs the dialog over <paramref name="editor"/> and keeps the site's settings chosen.</summary>
    public async Task EditAsync(SiteSettingsEditor editor)
    {
        JsonObject? chosen = null;
        try
        {
            chosen = await presenter.EditAsync(editor).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Site settings dialog failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow, QuietFocusReturn: chosen is not null));
        if (chosen is null)
            return;
        if (!profiles.SetSiteSettings(editor.Site, chosen))
            Say(VoxStrings.Get("SiteSettings.NotSaved"));
        else
            Say(VoxStrings.Format(chosen.Count == 0 ? "SiteSettings.Cleared" : "SiteSettings.Saved", editor.SiteName));
    }

    // What the site's settings are laid over: the settings without the site's own
    private VoxSettings UsualSettings(string site) =>
        string.Equals(profiles.Site, site, StringComparison.OrdinalIgnoreCase) && profiles.HasSiteSettings && settings is SettingsMonitor monitor
            ? monitor.Policy.Apply(profiles.Apply(monitor.NormalValue, includeSite: false))
            : settings.CurrentValue;

    private void Say(string text) => speechQueue?.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
