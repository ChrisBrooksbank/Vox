using System.Windows.Forms;
using Vox.Core.Localization;
using Vox.Core.Navigation;

namespace Vox.Core.Configuration;

/// <summary>Asks for a line of text in an accessible dialog; null when cancelled or left empty.</summary>
public interface ITextPrompt
{
    /// <param name="secret">Typed characters are hidden (a key or password); nothing is offered from history.</param>
    /// <param name="history">Earlier answers, most recent first (Down arrow goes through them).</param>
    Task<string?> AskAsync(string title, string label, bool secret = false, IReadOnlyList<string>? history = null);
}

/// <summary>Runs <see cref="TextPromptDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class TextPromptPresenter : ITextPrompt
{
    public Task<string?> AskAsync(string title, string label, bool secret = false, IReadOnlyList<string>? history = null)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var dialog = new TextPromptDialog(title, label, secret, history ?? []);
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.OK ? dialog.Answer : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-TextPrompt",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// A labelled text box (a combo box with earlier answers, or a password box for a secret) with OK
/// and Cancel; Enter is OK, Escape is Cancel.
/// </summary>
public sealed class TextPromptDialog : VoxDialog
{
    private readonly Control _box;

    /// <summary>What was typed; set when the dialog closes with OK.</summary>
    public string? Answer { get; private set; }

    public TextPromptDialog(string title, string label, bool secret, IReadOnlyList<string> history)
    {
        Text = title;
        AccessibleName = title;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        layout.Controls.Add(new Label { Text = label, AutoSize = true, MaximumSize = new System.Drawing.Size(420, 0) });
        var accessibleLabel = label.Replace("&", string.Empty);
        if (secret)
        {
            _box = new TextBox { UseSystemPasswordChar = true, Width = 400, AccessibleName = accessibleLabel };
        }
        else
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 400, AccessibleName = accessibleLabel };
            foreach (var earlier in history)
                combo.Items.Add(earlier);
            _box = combo;
        }
        layout.Controls.Add(_box);

        var ok = new Button { Text = VoxStrings.Get("Dialog.OK"), AutoSize = true };
        ok.Click += OnOk;
        var cancel = new Button { Text = VoxStrings.Get("Dialog.Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = _box;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        // Nothing typed: stay in the box
        if (string.IsNullOrWhiteSpace(_box.Text))
            return;
        Answer = _box.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
