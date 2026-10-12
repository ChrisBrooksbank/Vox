using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Configuration;

/// <summary>Shows the configuration profiles dialog until it is closed.</summary>
public interface IProfilesPresenter
{
    /// <param name="profiles">Changed directly as the user works.</param>
    /// <param name="foregroundApp">The application in the foreground before the dialog (its process name), for "use in this application".</param>
    Task ShowAsync(ProfileManager profiles, string? foregroundApp);
}

/// <summary>Runs <see cref="ProfilesDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class ProfilesPresenter : IProfilesPresenter
{
    public Task ShowAsync(ProfileManager profiles, string? foregroundApp)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new ProfilesDialog(profiles, foregroundApp);
                dialog.ShowInForeground();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-Profiles",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// Opens the configuration profiles dialog (Insert+Ctrl+P). Browse keys are off while it is open;
/// it reports back with <see cref="VoxDialogClosedEvent"/>. Pipeline thread.
/// </summary>
public sealed class ProfileCommands(
    IProfilesPresenter presenter,
    ProfileManager profiles,
    Func<string?> foregroundApp,
    BrowseModeController browse,
    IEventSink pipeline,
    SpeechQueue speech,
    ILogger<ProfileCommands> logger)
{
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.OpenProfiles)
            return false;
        if (!profiles.IsAvailable)
            speech.Enqueue(new Utterance("Profiles aren't available here", SpeechPriority.Interrupt));
        else if (browse.BeginOwnDialog())
            _ = ShowAsync(foregroundApp());
        return true;
    }

    private async Task ShowAsync(string? app)
    {
        try
        {
            await presenter.ShowAsync(profiles, app).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Profiles dialog failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
    }

    /// <summary>What is said when the profile on top changes: its name, or "Normal configuration".</summary>
    public static string SwitchAnnouncement(string? profile) =>
        profile is null ? "Normal configuration" : $"Profile {profile}";
}

/// <summary>
/// The profiles: a list (the active ones marked), a name box, and buttons to create, rename,
/// delete, activate by hand, and check boxes to use the selected profile in the application that
/// was in the foreground and during Say All. Every change is made at once.
/// </summary>
public sealed class ProfilesDialog : VoxDialog
{
    private readonly ProfileManager _profiles;
    private readonly string? _app;
    private readonly ListBox _list;
    private readonly TextBox _name;
    private readonly CheckBox _forApp;
    private readonly CheckBox _sayAll;
    private readonly Button _activate;
    private bool _updating;

    public ProfilesDialog(ProfileManager profiles, string? foregroundApp)
    {
        _profiles = profiles;
        _app = foregroundApp;
        Text = "Configuration profiles";
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _list = new ListBox { AccessibleName = "Profiles", Width = 320, Height = 160 };
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _name = new TextBox { AccessibleName = "Profile name", Width = 320 };
        var create = new Button { Text = "&New", AutoSize = true };
        create.Click += (_, _) => Run(() => _profiles.Create(_name.Text) ? _name.Text.Trim() : Fail("Type a name no other profile has."));
        var rename = new Button { Text = "&Rename", AutoSize = true };
        rename.Click += (_, _) => Run(() => Selected is { } p && _profiles.Rename(p.Name, _name.Text) ? _name.Text.Trim() : Fail("Choose a profile and type a new name no other profile has."));
        var delete = new Button { Text = "&Delete", AutoSize = true };
        delete.Click += (_, _) => Run(() =>
        {
            if (Selected is { } p && MessageBox.Show(this, $"Delete the profile {p.Name}?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                _profiles.Delete(p.Name);
            return null;
        });
        _activate = new Button { Text = "&Activate", AutoSize = true };
        _activate.Click += (_, _) => Run(() =>
        {
            if (Selected is { } p)
                _profiles.ActivateManually(IsManual(p) ? null : p.Name);
            return Selected?.Name;
        });
        _forApp = new CheckBox { AutoSize = true, Enabled = _app is not null, Text = _app is null ? "Use in this application" : $"Use in {_app}" };
        _forApp.CheckedChanged += (_, _) =>
        {
            if (!_updating && Selected is { } p && _app is not null)
                Run(() => { _profiles.SetAppActivation(p.Name, _app, _forApp.Checked); return p.Name; });
        };
        _sayAll = new CheckBox { Text = "Use during &Say All", AutoSize = true };
        _sayAll.CheckedChanged += (_, _) =>
        {
            if (!_updating && Selected is { } p)
                Run(() => { _profiles.SetSayAllTrigger(p.Name, _sayAll.Checked); return p.Name; });
        };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };

        var nameRow = new FlowLayoutPanel { AutoSize = true };
        nameRow.Controls.Add(new Label { Text = "&Name:", AutoSize = true, Anchor = AnchorStyles.Left });
        nameRow.Controls.Add(_name);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([create, rename, delete, _activate, close]);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.Add(new Label { Text = "&Profiles:", AutoSize = true });
        layout.Controls.Add(_list);
        layout.Controls.Add(nameRow);
        layout.Controls.Add(_forApp);
        layout.Controls.Add(_sayAll);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        CancelButton = close;
        ActiveControl = _list;
        Fill(null);
    }

    private ConfigurationProfile? Selected =>
        _list.SelectedIndex > 0 ? _profiles.Profiles.ElementAtOrDefault(_list.SelectedIndex - 1) : null;

    private bool IsManual(ConfigurationProfile profile) =>
        string.Equals(_profiles.ManualProfile, profile.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lists the normal configuration and the profiles, each marked when active, and selects <paramref name="select"/>.</summary>
    private void Fill(string? select)
    {
        _updating = true;
        var active = _profiles.Active;
        _list.Items.Clear();
        _list.Items.Add(active.Count == 0 ? "Normal configuration, active" : "Normal configuration");
        int index = 0;
        foreach (var (profile, i) in _profiles.Profiles.Select((p, i) => (p, i + 1)))
        {
            var marks = new List<string>();
            if (active.Contains(profile))
                marks.Add("active");
            if (IsManual(profile))
                marks.Add("chosen by hand");
            _list.Items.Add(marks.Count == 0 ? profile.Name : $"{profile.Name}, {string.Join(", ", marks)}");
            if (select is not null && profile.Name.Equals(select, StringComparison.OrdinalIgnoreCase))
                index = i;
        }
        _list.SelectedIndex = index;
        _updating = false;
        ShowSelected();
    }

    private void ShowSelected()
    {
        _updating = true;
        var profile = Selected;
        _forApp.Enabled = profile is not null && _app is not null;
        _sayAll.Enabled = profile is not null;
        _activate.Enabled = profile is not null;
        _forApp.Checked = profile?.IsForApp(_app) == true;
        _sayAll.Checked = profile?.SayAll == true;
        _activate.Text = profile is not null && IsManual(profile) ? "De&activate" : "&Activate";
        if (profile is not null)
            _name.Text = profile.Name;
        _updating = false;
    }

    private void Run(Func<string?> change)
    {
        var select = change();
        Fill(select);
    }

    private string? Fail(string message)
    {
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        _name.Focus();
        return Selected?.Name;
    }
}
