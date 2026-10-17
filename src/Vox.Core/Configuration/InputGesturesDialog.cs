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
/// <param name="AddOnCommands">The running add-ons' commands, listed under "Add-ons".</param>
public sealed record GestureDialogServices(
    Action<Action<KeyModifiers, int, bool>> CaptureKey,
    Action CancelCapture,
    Action<string> Say,
    string ScreenReaderKey,
    IReadOnlyList<GestureAddOnCommand>? AddOnCommands = null);

/// <summary>An add-on's command in the input gestures dialog.</summary>
/// <param name="AddOnName">The add-on's name (its node under "Add-ons").</param>
/// <param name="BindingName">The command's name in the user keymap (<see cref="AddOnCommandRef.BindingName"/>).</param>
/// <param name="Defaults">The keys the add-on gives it.</param>
public sealed record GestureAddOnCommand(string AddOnName, string BindingName, string Name, string Description, IReadOnlyList<KeyBinding> Defaults);

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

    /// <summary>A command node's tag: its name in the keymap (a <see cref="NavigationCommand"/> name or an add-on's) and its spoken name.</summary>
    private sealed record CommandTag(string Key, string Name);

    private void Fill(string? select = null)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        TreeNode? selected = null;

        TreeNode AddCommand(TreeNodeCollection parent, string key, string name, string description, TreeNode expand)
        {
            var gestures = _editor.GesturesFor(key);
            var keys = gestures.Count == 0 ? VoxStrings.Get("AddOns.Details.NoKey") : string.Join(", ", gestures.Select(g => g.Describe(_services.ScreenReaderKey)));
            var commandNode = parent.Add(VoxStrings.Format("Gesture.CommandKeys", name, keys));
            commandNode.Tag = new CommandTag(key, name);
            commandNode.ToolTipText = description;
            foreach (var gesture in gestures)
                commandNode.Nodes.Add(new TreeNode(gesture.Describe(_services.ScreenReaderKey)) { Tag = gesture });
            if (select == key)
            {
                for (var node = expand; node is not null; node = node.Parent)
                    node.Expand();
                selected = commandNode;
            }
            return commandNode;
        }

        foreach (var category in CommandCatalog.All.GroupBy(c => c.Category))
        {
            var categoryNode = _tree.Nodes.Add(CommandCatalog.CategoryName(category.Key));
            foreach (var info in category)
                AddCommand(categoryNode.Nodes, info.Command.ToString(), info.Name, info.Description, categoryNode);
        }
        if (_services.AddOnCommands is { Count: > 0 } addOnCommands)
        {
            var addOnsNode = _tree.Nodes.Add(VoxStrings.Get("Gesture.AddOnsCategory"));
            foreach (var addOn in addOnCommands.GroupBy(c => c.AddOnName))
            {
                var addOnNode = addOnsNode.Nodes.Add(addOn.Key);
                foreach (var command in addOn)
                    AddCommand(addOnNode.Nodes, command.BindingName, command.Name, command.Description, addOnNode);
            }
        }
        _tree.EndUpdate();
        if (selected is not null)
            _tree.SelectedNode = selected;
        UpdateButtons();
    }

    private CommandTag? SelectedCommand =>
        _tree.SelectedNode?.Tag as CommandTag ?? _tree.SelectedNode?.Parent?.Tag as CommandTag;

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
        _services.Say(VoxStrings.Format("Gesture.PressNewKeys", command.Name));
        _services.CaptureKey((modifiers, vk, keypad) => BeginInvoke(() => KeyPressed(command, mode, modifiers, vk, keypad)));
    }

    /// <summary>The spoken name of a command in the keymap (an add-on's, else Vox's).</summary>
    private string NameOf(string key) =>
        _services.AddOnCommands?.FirstOrDefault(c => c.BindingName == key)?.Name
        ?? (Enum.TryParse<NavigationCommand>(key, out var command) ? CommandCatalog.Describe(command).Name : key);

    private void KeyPressed(CommandTag command, string mode, KeyModifiers modifiers, int vk, bool keypad)
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
        var conflicts = _editor.ConflictNames(gesture, command.Key);
        if (conflicts.Count > 0)
        {
            var names = string.Join(", ", conflicts.Select(NameOf));
            var answer = MessageBox.Show(this,
                VoxStrings.Format("Gesture.KeyInUseQuestion", gesture.Describe(_services.ScreenReaderKey), names, command.Name),
                VoxStrings.Get("Gesture.KeyInUse"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
        }
        _editor.Add(command.Key, gesture);
        _services.Say(VoxStrings.Format("Gesture.KeyAdded", gesture.Describe(_services.ScreenReaderKey)));
        Fill(command.Key);
    }

    private void RemoveKey()
    {
        if (_tree.SelectedNode?.Tag is not Gesture gesture || SelectedCommand is not { } command)
            return;
        _editor.Remove(command.Key, gesture);
        _services.Say(VoxStrings.Format("Gesture.KeyRemoved", gesture.Describe(_services.ScreenReaderKey)));
        Fill(command.Key);
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
