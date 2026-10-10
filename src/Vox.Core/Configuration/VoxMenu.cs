using System.Windows.Forms;
using Vox.Core.Input;
using Vox.Core.Navigation;

namespace Vox.Core.Configuration;

/// <summary>An entry of the Vox menu: what it says and the command it runs.</summary>
public sealed record VoxMenuItem(string Text, NavigationCommand Command);

/// <summary>The Vox menu (Insert+N, and the tray icon's menu): Vox's dialogs and switches in one place.</summary>
public static class VoxMenu
{
    public static readonly IReadOnlyList<VoxMenuItem> Items =
    [
        new("&Settings...", NavigationCommand.OpenSettings),
        new("&Input gestures...", NavigationCommand.OpenInputGestures),
        new("&Command search...", NavigationCommand.OpenCommandSearch),
        new("Speech &viewer", NavigationCommand.ToggleSpeechViewer),
        new("&Pause speech", NavigationCommand.TogglePauseSpeech),
        new("User &guide", NavigationCommand.OpenUserGuide),
        new("Practice &lessons...", NavigationCommand.RunTutorial),
        new("Run &welcome wizard...", NavigationCommand.RunSetup),
        new("E&xit Vox", NavigationCommand.ExitVox),
    ];
}

/// <summary>Shows the Vox menu; the command chosen, or null.</summary>
public interface IVoxMenuPresenter
{
    Task<NavigationCommand?> ShowAsync();
}

/// <summary>Runs <see cref="VoxMenuDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class VoxMenuPresenter : IVoxMenuPresenter
{
    public Task<NavigationCommand?> ShowAsync()
    {
        var tcs = new TaskCompletionSource<NavigationCommand?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new VoxMenuDialog();
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.OK ? dialog.Chosen : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-Menu",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// The Vox menu as a list: arrows (or an item's letter) pick an entry, Enter runs it, Escape
/// closes. A list in a small dialog rather than a pop-up menu, so it always gets the keyboard
/// when Insert+N opens it from another application.
/// </summary>
public sealed class VoxMenuDialog : VoxDialog
{
    private readonly ListBox _list;

    public NavigationCommand? Chosen { get; private set; }

    public VoxMenuDialog()
    {
        Text = "Vox menu";
        AccessibleName = "Vox menu";
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _list = new ListBox { AccessibleName = "Vox menu", Width = 260, Height = 170 };
        foreach (var item in VoxMenu.Items)
            _list.Items.Add(item.Text.Replace("&", string.Empty));
        _list.SelectedIndex = 0;
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Choose();
                e.Handled = true;
            }
        };
        _list.DoubleClick += (_, _) => Choose();

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.Add(_list);
        layout.Controls.Add(close);
        Controls.Add(layout);
        CancelButton = close;
        ActiveControl = _list;
    }

    private void Choose()
    {
        if (_list.SelectedIndex < 0)
            return;
        Chosen = VoxMenu.Items[_list.SelectedIndex].Command;
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// Vox's icon in the notification area, with the Vox menu on right-click (double-click opens
/// the settings). Runs its own STA thread with a message loop; commands chosen go to
/// <c>run</c> (which posts them to the pipeline).
/// </summary>
public sealed class VoxTrayIcon : IDisposable
{
    private readonly Thread _thread;
    private ApplicationContext? _context;
    private readonly ManualResetEventSlim _ready = new();

    public VoxTrayIcon(Action<NavigationCommand> run)
    {
        _thread = new Thread(() =>
        {
            using var menu = new ContextMenuStrip();
            foreach (var item in VoxMenu.Items)
                menu.Items.Add(item.Text, null, (_, _) => run(item.Command));
            using var icon = new NotifyIcon
            {
                Text = "Vox screen reader",
                Icon = System.Drawing.SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true,
            };
            icon.DoubleClick += (_, _) => run(NavigationCommand.OpenSettings);
            _context = new ApplicationContext();
            _ready.Set();
            Application.Run(_context);
            icon.Visible = false;
        })
        {
            IsBackground = true,
            Name = "Vox-Tray",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _context?.ExitThread();
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
