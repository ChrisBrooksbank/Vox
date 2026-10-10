using System.Windows.Forms;
using Vox.Core.Navigation;

namespace Vox.Core.Configuration;

/// <summary>Shows the settings dialog; completes when it closes.</summary>
public interface ISettingsDialogPresenter
{
    /// <param name="settings">The settings when the dialog opens.</param>
    /// <param name="pages">What it shows (<see cref="SettingsPages"/>).</param>
    /// <param name="apply">Called on the dialog's thread with every change, so it takes effect at once.</param>
    Task ShowAsync(VoxSettings settings, IReadOnlyList<SettingsPage> pages, Action<VoxSettings> apply);
}

/// <summary>Runs <see cref="SettingsDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class SettingsDialogPresenter : ISettingsDialogPresenter
{
    public Task ShowAsync(VoxSettings settings, IReadOnlyList<SettingsPage> pages, Action<VoxSettings> apply)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new SettingsDialog(settings, pages, apply);
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
            Name = "Vox-Settings",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// The settings dialog: a tab per <see cref="SettingsPage"/>, a labelled control per field
/// (check box, spin box or combo box). Every change applies at once; OK keeps the changes,
/// Cancel (or Escape) puts back the settings the dialog opened with.
/// </summary>
public sealed class SettingsDialog : VoxDialog
{
    private readonly VoxSettings _original;
    private readonly Action<VoxSettings> _apply;
    private VoxSettings _current;

    public SettingsDialog(VoxSettings settings, IReadOnlyList<SettingsPage> pages, Action<VoxSettings> apply)
    {
        _original = settings;
        _current = settings;
        _apply = apply;

        Text = "Vox settings";
        AccessibleName = "Vox settings";
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var tabs = new TabControl { AccessibleName = "Categories", Width = 520, Height = 380 };
        foreach (var page in pages)
            tabs.TabPages.Add(BuildPage(page));

        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;
        FormClosed += (_, _) =>
        {
            if (DialogResult != DialogResult.OK && _current != _original)
                _apply(_original);
        };
    }

    private TabPage BuildPage(SettingsPage page)
    {
        var tab = new TabPage(page.Title) { AccessibleName = page.Title, AutoScroll = true };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        int row = 0;
        foreach (var field in page.Fields)
        {
            var control = BuildControl(field);
            if (field is ToggleField)
            {
                grid.Controls.Add(control, 1, row);
            }
            else
            {
                // The label's mnemonic moves to the control after it (tab order follows)
                grid.Controls.Add(new Label { Text = field.Label + ":", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
                grid.Controls.Add(control, 1, row);
            }
            row++;
        }
        tab.Controls.Add(grid);
        return tab;
    }

    private Control BuildControl(SettingField field)
    {
        var name = field.Label.Replace("&", string.Empty);
        switch (field)
        {
            case ToggleField toggle:
            {
                var box = new CheckBox { Text = toggle.Label, AutoSize = true, Checked = toggle.Get(_current), AccessibleName = name };
                box.CheckedChanged += (_, _) => Change(s => toggle.Set(s, box.Checked));
                return box;
            }
            case NumberField number:
            {
                var spin = new NumericUpDown
                {
                    Minimum = number.Min, Maximum = number.Max, Increment = number.Step,
                    Value = Math.Clamp(number.Get(_current), number.Min, number.Max), AccessibleName = name, Width = 120,
                };
                spin.ValueChanged += (_, _) => Change(s => number.Set(s, (int)spin.Value));
                return spin;
            }
            case ChoiceField choice:
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name, Width = 260 };
                foreach (var option in choice.Choices)
                    combo.Items.Add(option.Text);
                combo.SelectedIndex = choice.IndexOf(_current);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    if (combo.SelectedIndex >= 0)
                        Change(s => choice.Set(s, choice.Choices[combo.SelectedIndex].Value));
                };
                return combo;
            }
            default:
                throw new ArgumentException($"No control for {field.GetType().Name}", nameof(field));
        }
    }

    private void Change(Func<VoxSettings, VoxSettings> change)
    {
        var updated = change(_current);
        if (updated == _current)
            return;
        _current = updated;
        _apply(updated);
    }
}
