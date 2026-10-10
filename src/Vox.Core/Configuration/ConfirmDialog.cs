using System.Windows.Forms;
using Vox.Core.Navigation;

namespace Vox.Core.Configuration;

/// <summary>Asks a yes/no question in an accessible dialog; true for Yes.</summary>
public interface IConfirmPresenter
{
    Task<bool> AskAsync(string title, string question);
}

/// <summary>Runs <see cref="ConfirmDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class ConfirmPresenter : IConfirmPresenter
{
    public Task<bool> AskAsync(string title, string question)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new ConfirmDialog(title, question);
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-Confirm",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>A question with Yes and No (Escape is No); the question is the dialog's description, so it is read on opening.</summary>
public sealed class ConfirmDialog : VoxDialog
{
    public ConfirmDialog(string title, string question)
    {
        Text = title;
        AccessibleName = title;
        AccessibleDescription = question;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var yes = new Button { Text = "&Yes", AutoSize = true, DialogResult = DialogResult.Yes };
        var no = new Button { Text = "&No", AutoSize = true, DialogResult = DialogResult.No };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(yes);
        buttons.Controls.Add(no);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        layout.Controls.Add(new Label { Text = question, AutoSize = true, MaximumSize = new System.Drawing.Size(420, 0) });
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = yes;
        CancelButton = no;
        ActiveControl = yes;
    }
}
