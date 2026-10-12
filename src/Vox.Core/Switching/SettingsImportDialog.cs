using System.Windows.Forms;
using Vox.Core.Localization;
using Vox.Core.Navigation;

namespace Vox.Core.Switching;

/// <summary>An import ready to review: where from, and its report as text.</summary>
public sealed record ImportPreview(ImportSource Source, string Report);

/// <summary>Shows import reports: to review before importing, or to read afterwards.</summary>
public interface ISettingsImportPresenter
{
    /// <summary>Shows the reports (one per source found); the source chosen to import, or null.</summary>
    Task<ImportSource?> ReviewAsync(IReadOnlyList<ImportPreview> previews);

    /// <summary>Shows an applied import's report until closed.</summary>
    Task ShowAsync(string title, string report);
}

/// <summary>Runs <see cref="SettingsImportDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class SettingsImportPresenter : ISettingsImportPresenter
{
    public Task<ImportSource?> ReviewAsync(IReadOnlyList<ImportPreview> previews) =>
        Run(() =>
        {
            using var dialog = new SettingsImportDialog(VoxStrings.Get("Import.Dialog.Title"), previews, review: true);
            return dialog.ShowInForeground() == DialogResult.OK ? dialog.Chosen : (ImportSource?)null;
        });

    public Task ShowAsync(string title, string report) =>
        Run(() =>
        {
            // The source doesn't matter when only reading
            using var dialog = new SettingsImportDialog(title, [new ImportPreview(ImportSource.Nvda, report)], review: false);
            dialog.ShowInForeground();
            return true;
        });

    private static Task<T> Run<T>(Func<T> show)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(show());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-Import",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// An import's report as a read-only document (arrows read it line by line), with, when reviewing,
/// the screen reader to import from (when both were found) and Import / Cancel; else Close.
/// </summary>
public sealed class SettingsImportDialog : VoxDialog
{
    private readonly IReadOnlyList<ImportPreview> _previews;
    private readonly ComboBox? _source;

    public ImportSource? Chosen { get; private set; }

    public SettingsImportDialog(string title, IReadOnlyList<ImportPreview> previews, bool review)
    {
        _previews = previews;
        Text = title;
        AccessibleName = title;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        var report = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Width = 560,
            Height = 360,
            AccessibleName = VoxStrings.Get(review ? "Import.Dialog.Report" : "Import.Dialog.ReportApplied"),
            Text = Lines(previews[0].Report),
        };

        if (review && previews.Count > 1)
        {
            var label = new Label { Text = VoxStrings.Get("Import.Dialog.Source"), AutoSize = true };
            _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, AccessibleName = label.Text.Replace("&", string.Empty) };
            foreach (var preview in previews)
                _source.Items.Add(ImportReportText.SourceName(preview.Source));
            _source.SelectedIndex = 0;
            _source.SelectedIndexChanged += (_, _) => report.Text = Lines(previews[_source.SelectedIndex].Report);
            layout.Controls.Add(label);
            layout.Controls.Add(_source);
        }
        layout.Controls.Add(report);

        var buttons = new FlowLayoutPanel { AutoSize = true };
        if (review)
        {
            var importButton = new Button { Text = VoxStrings.Get("Import.Dialog.Import"), AutoSize = true };
            importButton.Click += (_, _) =>
            {
                Chosen = _previews[_source?.SelectedIndex ?? 0].Source;
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = new Button { Text = VoxStrings.Get("Dialog.Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;
        }
        else
        {
            var close = new Button { Text = VoxStrings.Get("Dialog.Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(close);
            CancelButton = close;
        }
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        ActiveControl = (Control?)_source ?? report;
    }

    // A text box needs Windows line ends
    private static string Lines(string text) => text.ReplaceLineEndings("\r\n");
}
