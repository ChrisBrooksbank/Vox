using System.Windows.Forms;

namespace Vox.Core.Navigation;

/// <summary>
/// Shows the find prompt and returns what to look for (null if cancelled or empty).
/// </summary>
public interface IFindPrompt
{
    /// <param name="history">Earlier searches, most recent first; the box starts with the first.</param>
    Task<FindRequest?> ShowAsync(IReadOnlyList<string> history, bool matchCase);
}

/// <summary>
/// Runs <see cref="FindDialog"/> on its own STA thread, as WinForms requires.
/// </summary>
public sealed class FindPromptPresenter : IFindPrompt
{
    public Task<FindRequest?> ShowAsync(IReadOnlyList<string> history, bool matchCase)
    {
        var tcs = new TaskCompletionSource<FindRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(FindDialog.ShowModal(history, matchCase));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-Find",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// Accessible WinForms find prompt opened by Insert+Ctrl+F in browse mode: an editable
/// "Find what" combo box holding the earlier searches (Down arrow goes through them), a
/// "Match case" check box, and Find / Cancel buttons.
/// </summary>
public sealed class FindDialog : VoxDialog
{
    private readonly ComboBox _textBox;
    private readonly CheckBox _matchCaseBox;

    /// <summary>What to find; set when the dialog closes with DialogResult.OK.</summary>
    public FindRequest? Request { get; private set; }

    public FindDialog(IReadOnlyList<string> history, bool matchCase = false)
    {
        Text = "Find";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AccessibleName = "Find";
        AccessibleRole = AccessibleRole.Dialog;

        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(8),
        };

        panel.Controls.Add(new Label
        {
            Text = "&Find what:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        }, 0, 0);
        _textBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            AccessibleName = "Find what",
            Width = 300,
        };
        foreach (var text in history)
            _textBox.Items.Add(text);
        if (history.Count > 0)
            _textBox.Text = history[0];
        panel.Controls.Add(_textBox, 1, 0);

        _matchCaseBox = new CheckBox
        {
            Text = "Match &case",
            AutoSize = true,
            Checked = matchCase,
        };
        panel.Controls.Add(_matchCaseBox, 1, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Right,
        };
        var findButton = new Button { Text = "Find", AutoSize = true };
        findButton.Click += OnFind;
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(findButton);
        buttons.Controls.Add(cancelButton);
        panel.Controls.Add(buttons, 1, 2);
        Controls.Add(panel);

        AcceptButton = findButton;
        CancelButton = cancelButton;
        ActiveControl = _textBox;
        // The previous search is selected, so typing replaces it
        Shown += (_, _) => _textBox.SelectAll();
    }

    /// <summary>Shows the prompt modally and returns what to find, or null if cancelled or empty.</summary>
    public static FindRequest? ShowModal(IReadOnlyList<string> history, bool matchCase)
    {
        Application.EnableVisualStyles();
        using var dlg = new FindDialog(history, matchCase);
        return dlg.ShowInForeground() == DialogResult.OK ? dlg.Request : null;
    }

    private void OnFind(object? sender, EventArgs e)
    {
        // Nothing to find: stay in the box
        if (string.IsNullOrEmpty(_textBox.Text))
            return;
        Request = new FindRequest(_textBox.Text, _matchCaseBox.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }
}
