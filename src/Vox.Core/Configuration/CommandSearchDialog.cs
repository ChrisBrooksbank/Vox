using System.Windows.Forms;
using Vox.Core.Input;
using Vox.Core.Navigation;

namespace Vox.Core.Configuration;

/// <summary>Shows the command search; completes when it closes.</summary>
public interface ICommandSearchPresenter
{
    /// <param name="search">The commands (with their keys) matching what has been typed.</param>
    Task ShowAsync(Func<string, IReadOnlyList<CommandMatch>> search);
}

/// <summary>Runs <see cref="CommandSearchDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class CommandSearchPresenter : ICommandSearchPresenter
{
    public Task ShowAsync(Func<string, IReadOnlyList<CommandMatch>> search)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new CommandSearchDialog(search);
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
            Name = "Vox-CommandSearch",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// Command search: a "Search for" box and a list of the matching commands, each read as its
/// name and keys ("Next heading: H (browse mode)"). The list follows what is typed; Down arrow
/// moves from the box into it.
/// </summary>
public sealed class CommandSearchDialog : VoxDialog
{
    private readonly Func<string, IReadOnlyList<CommandMatch>> _search;
    private readonly TextBox _query;
    private readonly ListBox _results;

    public CommandSearchDialog(Func<string, IReadOnlyList<CommandMatch>> search)
    {
        _search = search;
        Text = "Command search";
        AccessibleName = "Command search";
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _query = new TextBox { AccessibleName = "Search for", Width = 420 };
        _results = new ListBox { AccessibleName = "Commands", Width = 420, Height = 300 };
        _query.TextChanged += (_, _) => Update();
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _results.Items.Count > 0)
            {
                _results.Focus();
                if (_results.SelectedIndex < 0)
                    _results.SelectedIndex = 0;
                e.Handled = true;
            }
        };

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.Add(new Label { Text = "&Search for:", AutoSize = true });
        layout.Controls.Add(_query);
        layout.Controls.Add(new Label { Text = "&Commands:", AutoSize = true });
        layout.Controls.Add(_results);
        layout.Controls.Add(close);
        Controls.Add(layout);
        CancelButton = close;
        ActiveControl = _query;
    }

    private void Update()
    {
        _results.BeginUpdate();
        _results.Items.Clear();
        foreach (var match in _search(_query.Text))
            _results.Items.Add(match.ToString());
        _results.EndUpdate();
        // Said by the screen reader as the list's description when focus moves to it
        _results.AccessibleDescription = $"{_results.Items.Count} found";
    }
}
