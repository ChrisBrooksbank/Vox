using System.Windows.Forms;
using Vox.Core.Localization;
using Vox.Core.Navigation;

namespace Vox.Core.Labels;

/// <summary>Shows the custom labels dialog; the labels to keep, or null when cancelled or nothing changed.</summary>
public interface ICustomLabelsPresenter
{
    Task<IReadOnlyList<CustomLabel>?> EditAsync(CustomLabelsEditor editor);
}

/// <summary>Runs <see cref="CustomLabelsDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class CustomLabelsPresenter : ICustomLabelsPresenter
{
    public Task<IReadOnlyList<CustomLabel>?> EditAsync(CustomLabelsEditor editor)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CustomLabel>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new CustomLabelsDialog(editor);
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.OK && editor.Changed ? editor.Labels : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-Labels",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// The custom labels: a list (the current site's first), the selected one's text to change, and
/// Change label, Delete, Export (the selected label's site, as a label pack), Import, OK and
/// Cancel. The logic is <see cref="CustomLabelsEditor"/>'s and <see cref="LabelPack"/>'s.
/// </summary>
public sealed class CustomLabelsDialog : VoxDialog
{
    private readonly CustomLabelsEditor _editor;
    private readonly ListBox _list;
    private readonly TextBox _text;
    private readonly Label _status;

    public CustomLabelsDialog(CustomLabelsEditor editor)
    {
        _editor = editor;
        Text = VoxStrings.Get("Labels.Dialog.Title");
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        var listLabel = new Label { Text = VoxStrings.Get("Labels.Dialog.List"), AutoSize = true };
        _list = new ListBox { Width = 480, Height = 240, AccessibleName = listLabel.Text.Replace("&", string.Empty) };
        var textLabel = new Label { Text = VoxStrings.Get("Labels.Dialog.Label"), AutoSize = true };
        _text = new TextBox { Width = 480, AccessibleName = textLabel.Text.Replace("&", string.Empty) };
        _list.SelectedIndexChanged += (_, _) => _text.Text = _list.SelectedIndex >= 0 ? _editor.Labels[_list.SelectedIndex].Label : string.Empty;
        layout.Controls.Add(listLabel);
        layout.Controls.Add(_list);
        layout.Controls.Add(textLabel);
        layout.Controls.Add(_text);

        var change = new Button { Text = VoxStrings.Get("Labels.Dialog.Change"), AutoSize = true };
        change.Click += (_, _) =>
        {
            int index = _list.SelectedIndex;
            if (_editor.Rename(index, _text.Text))
                Refill(index);
        };
        var delete = new Button { Text = VoxStrings.Get("Labels.Dialog.Delete"), AutoSize = true };
        delete.Click += (_, _) =>
        {
            int index = _list.SelectedIndex;
            if (_editor.Delete(index))
                Refill(Math.Min(index, _editor.Labels.Count - 1));
        };
        var export = new Button { Text = VoxStrings.Get("Labels.Dialog.Export"), AutoSize = true };
        export.Click += (_, _) => Export();
        var import = new Button { Text = VoxStrings.Get("Labels.Dialog.Import"), AutoSize = true };
        import.Click += (_, _) => Import();
        var ok = new Button { Text = VoxStrings.Get("Dialog.OK"), AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = VoxStrings.Get("Dialog.Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([change, delete, export, import, ok, cancel]);
        layout.Controls.Add(buttons);
        // What the last export or import did
        _status = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(480, 0) };
        layout.Controls.Add(_status);
        Controls.Add(layout);
        CancelButton = cancel;

        Refill(0);
        ActiveControl = _list;
    }

    private void Export()
    {
        if (_editor.ExportSite(_list.SelectedIndex) is not { } site)
        {
            Status(VoxStrings.Get("Labels.Dialog.NothingToExport"));
            return;
        }
        var labels = _editor.LabelsOf(site);
        using var save = new SaveFileDialog
        {
            Filter = VoxStrings.Format("Labels.Dialog.Filter", LabelPack.Extension),
            DefaultExt = LabelPack.Extension,
            FileName = new Uri(site).Host + LabelPack.Extension,
        };
        if (save.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            LabelPack.Write(save.FileName, labels);
            Status(VoxStrings.Format("Labels.Dialog.Exported", labels.Count, new Uri(site).Host));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status(VoxStrings.Format("Labels.Dialog.Failed", ex.Message));
        }
    }

    private void Import()
    {
        using var open = new OpenFileDialog { Filter = VoxStrings.Format("Labels.Dialog.Filter", LabelPack.Extension) };
        if (open.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var labels = LabelPack.Read(open.FileName);
            int count = _editor.Merge(labels);
            Refill(0);
            Status(VoxStrings.Format("Labels.Dialog.Imported", count, string.Join(", ", LabelPack.SitesOf(labels).Select(s => new Uri(s).Host))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Status(VoxStrings.Format("Labels.Dialog.Failed", ex.Message));
        }
    }

    // Said in a message box, which screen readers read at once; kept below the buttons too
    private void Status(string text)
    {
        _status.Text = text;
        MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Refill(int selected)
    {
        _list.Items.Clear();
        for (int i = 0; i < _editor.Labels.Count; i++)
            _list.Items.Add(_editor.Describe(i));
        if (_list.Items.Count == 0)
            _text.Text = string.Empty;
        else
            _list.SelectedIndex = Math.Clamp(selected, 0, _list.Items.Count - 1);
    }
}
