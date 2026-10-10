using System.Windows.Forms;
using Vox.Core.Navigation;

namespace Vox.Core.Configuration;

/// <summary>Where to make a portable copy, and what to take.</summary>
public sealed record PortableCopyRequest(string Folder, bool CopySettings, bool CopyComponents);

/// <summary>Asks where to make a portable copy; null when cancelled.</summary>
public interface IPortableCopyPresenter
{
    Task<PortableCopyRequest?> ShowAsync();
}

/// <summary>Runs <see cref="PortableCopyDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class PortableCopyPresenter : IPortableCopyPresenter
{
    public Task<PortableCopyRequest?> ShowAsync()
    {
        var tcs = new TaskCompletionSource<PortableCopyRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new PortableCopyDialog();
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.OK ? dialog.Request : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-PortableCopy",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// "Create portable copy": a folder (typed, or chosen with Browse), whether to take the user's
/// settings and keys and the optional components, and Create / Cancel.
/// </summary>
public sealed class PortableCopyDialog : VoxDialog
{
    private readonly TextBox _folder;
    private readonly CheckBox _settings;
    private readonly CheckBox _components;

    public PortableCopyRequest? Request { get; private set; }

    public PortableCopyDialog()
    {
        Text = "Create portable copy";
        AccessibleName = "Create portable copy";
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _folder = new TextBox { AccessibleName = "Folder for the portable copy", Width = 360 };
        var browse = new Button { Text = "&Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Folder for the portable copy", UseDescriptionForTitle = true };
            if (picker.ShowDialog(this) == DialogResult.OK)
                _folder.Text = Path.Combine(picker.SelectedPath, "Vox");
        };
        _settings = new CheckBox { Text = "Copy my &settings, keys and dictionaries", AutoSize = true, Checked = true };
        _components = new CheckBox { Text = "Copy optional &components (eSpeak NG, MathCAT)", AutoSize = true, Checked = true };

        var create = new Button { Text = "C&reate", AutoSize = true };
        create.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_folder.Text))
            {
                MessageBox.Show(this, "Type or choose a folder first.", "Create portable copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _folder.Focus();
                return;
            }
            Request = new PortableCopyRequest(_folder.Text.Trim(), _settings.Checked, _components.Checked);
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        var row = new FlowLayoutPanel { AutoSize = true };
        row.Controls.Add(new Label { Text = "&Folder:", AutoSize = true, Anchor = AnchorStyles.Left });
        row.Controls.Add(_folder);
        row.Controls.Add(browse);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(create);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.Add(row);
        layout.Controls.Add(_settings);
        layout.Controls.Add(_components);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        AcceptButton = create;
        CancelButton = cancel;
        ActiveControl = _folder;
    }
}
