using System.Windows.Forms;
using Vox.Core.Input;
using Vox.Core.Navigation;
using Vox.Core.Localization;

namespace Vox.Core.Configuration;

/// <summary>What the input gestures dialog needs from the rest of Vox.</summary>
/// <param name="CaptureKey">Hands the next key pressed to the callback instead of running it (any thread).</param>
/// <param name="CancelCapture">Stops waiting for a key.</param>
/// <param name="Say">Speaks a prompt ("Press the new keys").</param>
/// <param name="ScreenReaderKey">"Insert" or "Caps Lock", for naming keys.</param>
public sealed record GestureDialogServices(
    Action<Action<KeyModifiers, int, bool>> CaptureKey,
    Action CancelCapture,
    Action<string> Say,
    string ScreenReaderKey);

/// <summary>Shows the input gestures dialog; true when the user saved changes.</summary>
public interface IInputGesturesPresenter
{
    Task<bool> ShowAsync(GestureEditor editor, GestureDialogServices services);
}

/// <summary>Runs <see cref="InputGesturesDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class InputGesturesPresenter : IInputGesturesPresenter
{
    public Task<bool> ShowAsync(GestureEditor editor, GestureDialogServices services)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new InputGesturesDialog(editor, services);
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.OK && editor.IsChanged);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                services.CancelCapture();
            }
        })
        {
            IsBackground = true,
            Name = "Vox-InputGestures",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// The input gestures dialog: a tree of command categories, their commands, and each command's
/// keys. "Add key" waits for the next key pressed (Escape alone cancels) and binds it to the
/// selected command, asking first when it is another command's; "Remove key" takes the selected
/// key away. OK saves the changes as the user keymap.
/// </summary>
public sealed class InputGesturesDialog : VoxDialog
{
    private readonly GestureEditor _editor;
    private readonly GestureDialogServices _services;
    private readonly TreeView _tree;
    private readonly ComboBox _where;
    private readonly Button _add;
    private readonly Button _remove;

    private static (string Text, string Mode)[] Places =>
        [(VoxStrings.Get("Gesture.Place.Everywhere"), "Any"), (VoxStrings.Get("Gesture.Place.BrowseMode"), "Browse"), (VoxStrings.Get("Gesture.Place.FocusMode"), "Focus")];

    public InputGesturesDialog(GestureEditor editor, GestureDialogServices services)
    {
        _editor = editor;
        _services = services;

        Text = VoxStrings.Get("Command.OpenInputGestures.Name");
        AccessibleName = VoxStrings.Get("Command.OpenInputGestures.Name");
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _tree = new TreeView { AccessibleName = VoxStrings.Get("CommandSearch.Commands"), Width = 560, Height = 380, HideSelection = false };
        _tree.AfterSelect += (_, _) => UpdateButtons();

        _where = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = VoxStrings.Get("Gesture.NewKeyWorks"), Width = 160 };
        foreach (var (text, _) in Places)
            _where.Items.Add(text);
        _where.SelectedIndex = 0;

        _add = new Button { Text = VoxStrings.Get("Gesture.AddKey"), AutoSize = true };
        _add.Click += (_, _) => AddKey();
        _remove = new Button { Text = VoxStrings.Get("Gesture.RemoveKey"), AutoSize = true };
        _remove.Click += (_, _) => RemoveKey();
        var reset = new Button { Text = VoxStrings.Get("Gesture.ResetAll"), AutoSize = true };
        reset.Click += (_, _) => ResetAll();

        var ok = new Button { Text = VoxStrings.Get("Dialog.OK"), AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = VoxStrings.Get("Dialog.Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };

        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.Add(new Label { Text = VoxStrings.Get("Gesture.NewKeyWorksLabel"), AutoSize = true, Anchor = AnchorStyles.Left });
        actions.Controls.Add(_where);
        actions.Controls.Add(_add);
        actions.Controls.Add(_remove);
        actions.Controls.Add(reset);
        var closing = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        closing.Controls.Add(cancel);
        closing.Controls.Add(ok);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.Add(_tree);
        layout.Controls.Add(actions);
        layout.Controls.Add(closing);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;
        Fill();
    }

    private void Fill(NavigationCommand? select = null)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        TreeNode? selected = null;
        foreach (var category in CommandCatalog.All.GroupBy(c => c.Category))
        {
            var categoryNode = _tree.Nodes.Add(CommandCatalog.CategoryName(category.Key));
            foreach (var info in category)
            {
                var gestures = _editor.GesturesFor(info.Command);
                var keys = gestures.Count == 0 ? VoxStrings.Get("AddOns.Details.NoKey") : string.Join(", ", gestures.Select(g => g.Describe(_services.ScreenReaderKey)));
                var commandNode = categoryNode.Nodes.Add(VoxStrings.Format("Gesture.CommandKeys", info.Name, keys));
                commandNode.Tag = info.Command;
                commandNode.ToolTipText = info.Description;
                foreach (var gesture in gestures)
                    commandNode.Nodes.Add(new TreeNode(gesture.Describe(_services.ScreenReaderKey)) { Tag = gesture });
                if (select == info.Command)
                {
                    categoryNode.Expand();
                    selected = commandNode;
                }
            }
        }
        _tree.EndUpdate();
        if (selected is not null)
            _tree.SelectedNode = selected;
        UpdateButtons();
    }

    private NavigationCommand? SelectedCommand =>
        _tree.SelectedNode?.Tag as NavigationCommand? ?? _tree.SelectedNode?.Parent?.Tag as NavigationCommand?;

    private void UpdateButtons()
    {
        _add.Enabled = SelectedCommand is not null;
        _remove.Enabled = _tree.SelectedNode?.Tag is Gesture;
    }

    private void AddKey()
    {
        if (SelectedCommand is not { } command)
            return;
        var mode = Places[Math.Max(0, _where.SelectedIndex)].Mode;
        _add.Enabled = false;
        _services.Say(VoxStrings.Format("Gesture.PressNewKeys", CommandCatalog.Describe(command).Name));
        _services.CaptureKey((modifiers, vk, keypad) => BeginInvoke(() => KeyPressed(command, mode, modifiers, vk, keypad)));
    }

    private void KeyPressed(NavigationCommand command, string mode, KeyModifiers modifiers, int vk, bool keypad)
    {
        UpdateButtons();
        if (vk == 0x1B && modifiers == KeyModifiers.None)
        {
            _services.Say(VoxStrings.Get("Ai.Cancelled"));
            return;
        }
        if (Gesture.FromKey(modifiers, vk, keypad, mode) is not { } gesture)
        {
            _services.Say(VoxStrings.Get("Gesture.NumpadDigits"));
            return;
        }
        var conflicts = _editor.ConflictsWith(gesture, command);
        if (conflicts.Count > 0)
        {
            var names = string.Join(", ", conflicts.Select(c => CommandCatalog.Describe(c).Name));
            var answer = MessageBox.Show(this,
                VoxStrings.Format("Gesture.KeyInUseQuestion", gesture.Describe(_services.ScreenReaderKey), names, CommandCatalog.Describe(command).Name),
                VoxStrings.Get("Gesture.KeyInUse"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
        }
        _editor.Add(command, gesture);
        _services.Say(VoxStrings.Format("Gesture.KeyAdded", gesture.Describe(_services.ScreenReaderKey)));
        Fill(command);
    }

    private void RemoveKey()
    {
        if (_tree.SelectedNode?.Tag is not Gesture gesture || SelectedCommand is not { } command)
            return;
        _editor.Remove(command, gesture);
        _services.Say(VoxStrings.Format("Gesture.KeyRemoved", gesture.Describe(_services.ScreenReaderKey)));
        Fill(command);
    }

    private void ResetAll()
    {
        var answer = MessageBox.Show(this, VoxStrings.Get("Gesture.ResetAllQuestion"), VoxStrings.Get("Gesture.ResetKeys"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;
        _editor.ResetAll();
        Fill();
    }
}
