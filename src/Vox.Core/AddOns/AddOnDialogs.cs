using System.Windows.Forms;
using Vox.Core.Localization;
using Vox.Core.Navigation;

namespace Vox.Core.AddOns;

/// <summary>Runs a Vox dialog on its own STA thread, as WinForms requires.</summary>
internal static class StaDialog
{
    public static Task Run(Func<VoxDialog> create, string threadName)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var dialog = create();
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
            Name = threadName,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

public sealed class AddOnsPresenter : IAddOnsPresenter
{
    public Task ShowAsync(IAddOnActions actions) => StaDialog.Run(() => new AddOnsDialog(actions), "Vox-AddOns");
}

public sealed class AddOnStorePresenter : IAddOnStorePresenter
{
    public Task ShowAsync(IAddOnActions actions) => StaDialog.Run(() => new AddOnStoreDialog(actions), "Vox-AddOnStore");
}

/// <summary>
/// The installed add-ons: a list (name, version, state), the selected one's details (author,
/// publisher, commands and their keys, any problem), and buttons to turn it on or off, remove it,
/// or install one from a .voxaddon file.
/// </summary>
public sealed class AddOnsDialog : VoxDialog
{
    private readonly IAddOnActions _actions;
    private readonly ListBox _list;
    private readonly TextBox _details;
    private readonly Button _toggle;
    private readonly Button _remove;
    private readonly Label _status;
    private IReadOnlyList<AddOnInfo> _items = [];
    private bool _busy;

    public AddOnsDialog(IAddOnActions actions)
    {
        _actions = actions;
        Text = VoxStrings.Get(actions.IsSafeMode ? "AddOns.Dialog.TitleSafeMode" : "AddOns.Dialog.Title");
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var listLabel = new Label { Text = VoxStrings.Get("AddOns.Dialog.List"), AutoSize = true };
        _list = new ListBox { AccessibleName = listLabel.Text.Replace("&", string.Empty), Width = 420, Height = 160 };
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        var detailsLabel = new Label { Text = VoxStrings.Get("AddOns.Dialog.Details"), AutoSize = true };
        _details = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 420, Height = 140,
            AccessibleName = detailsLabel.Text.Replace("&", string.Empty),
        };
        _toggle = new Button { Text = VoxStrings.Get("AddOns.Dialog.Disable"), AutoSize = true };
        _toggle.Click += async (_, _) => await RunAsync(ToggleAsync);
        _remove = new Button { Text = VoxStrings.Get("AddOns.Dialog.Remove"), AutoSize = true };
        _remove.Click += async (_, _) => await RunAsync(RemoveAsync);
        var install = new Button { Text = VoxStrings.Get("AddOns.Dialog.Install"), AutoSize = true };
        install.Click += async (_, _) => await RunAsync(InstallAsync);
        var close = new Button { Text = VoxStrings.Get("Dialog.Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
        _status = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(420, 0) };

        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([_toggle, _remove, install, close]);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.AddRange([listLabel, _list, detailsLabel, _details, buttons, _status]);
        Controls.Add(layout);
        CancelButton = close;
        ActiveControl = _list;
        Refill(null);
    }

    private AddOnInfo? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _items.Count ? _items[_list.SelectedIndex] : null;

    private void Refill(string? keepId)
    {
        _items = _actions.List();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in _items)
            _list.Items.Add(AddOnCommands.ListLine(item));
        if (_items.Count == 0)
            _list.Items.Add(VoxStrings.Get("AddOns.Dialog.None"));
        _list.EndUpdate();
        var index = keepId is null ? 0 : Math.Max(0, _items.ToList().FindIndex(i => i.Manifest.Id == keepId));
        _list.SelectedIndex = _list.Items.Count > 0 ? Math.Min(index, _list.Items.Count - 1) : -1;
        ShowSelected();
    }

    private void ShowSelected()
    {
        var selected = Selected;
        _details.Text = selected is null ? string.Empty : _actions.Details(selected).Replace("\n", Environment.NewLine);
        _toggle.Enabled = _remove.Enabled = selected is not null;
        _toggle.Text = VoxStrings.Get(selected is { State: AddOnState.Disabled or AddOnState.TurnedOff } ? "AddOns.Dialog.Enable" : "AddOns.Dialog.Disable");
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var keep = Selected?.Manifest.Id;
            var message = await action();
            if (!IsDisposed)
            {
                if (message.Length > 0)
                    _status.Text = message;
                Refill(keep);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                _status.Text = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private Task<string> ToggleAsync() =>
        Selected is { } selected
            ? _actions.SetEnabledAsync(selected.Manifest.Id, selected.State is AddOnState.Disabled or AddOnState.TurnedOff)
            : Task.FromResult(string.Empty);

    private Task<string> RemoveAsync() =>
        Selected is { } selected ? _actions.RemoveAsync(selected.Manifest.Id) : Task.FromResult(string.Empty);

    private Task<string> InstallAsync()
    {
        using var open = new OpenFileDialog
        {
            Title = VoxStrings.Get("AddOns.Dialog.ChooseFile"),
            Filter = VoxStrings.Get("AddOns.Dialog.FileFilter") + $"|*{AddOnPackage.Extension}",
            CheckFileExists = true,
        };
        return open.ShowDialog(this) == DialogResult.OK ? _actions.InstallFromFileAsync(open.FileName) : Task.FromResult(string.Empty);
    }
}

/// <summary>
/// The add-on store: Vox's signed catalogue, searchable, with each add-on's details and whether it
/// is installed, has an update or doesn't work with this Vox; Install (or Update) and Update all.
/// </summary>
public sealed class AddOnStoreDialog : VoxDialog
{
    private readonly IAddOnActions _actions;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly TextBox _details;
    private readonly Button _install;
    private readonly Button _updateAll;
    private readonly Label _status;
    private IReadOnlyList<StoreItem> _all = [];
    private IReadOnlyList<StoreItem> _shown = [];
    private bool _busy;

    public AddOnStoreDialog(IAddOnActions actions)
    {
        _actions = actions;
        Text = VoxStrings.Get("Store.Dialog.Title");
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var searchLabel = new Label { Text = VoxStrings.Get("Store.Dialog.Search"), AutoSize = true };
        _search = new TextBox { Width = 420, AccessibleName = searchLabel.Text.Replace("&", string.Empty) };
        _search.TextChanged += (_, _) => Filter();
        var listLabel = new Label { Text = VoxStrings.Get("Store.Dialog.List"), AutoSize = true };
        _list = new ListBox { AccessibleName = listLabel.Text.Replace("&", string.Empty), Width = 420, Height = 160 };
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        var detailsLabel = new Label { Text = VoxStrings.Get("AddOns.Dialog.Details"), AutoSize = true };
        _details = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 420, Height = 120,
            AccessibleName = detailsLabel.Text.Replace("&", string.Empty),
        };
        _install = new Button { Text = VoxStrings.Get("Store.Dialog.Install"), AutoSize = true, Enabled = false };
        _install.Click += async (_, _) => await RunAsync(() => Selected is { } item ? _actions.InstallFromStoreAsync(item) : Task.FromResult(string.Empty));
        _updateAll = new Button { Text = VoxStrings.Get("Store.Dialog.UpdateAll"), AutoSize = true, Enabled = false };
        _updateAll.Click += async (_, _) => await RunAsync(() => _actions.UpdateAllAsync(_all));
        var close = new Button { Text = VoxStrings.Get("Dialog.Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
        _status = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(420, 0), Text = VoxStrings.Get("Store.Fetching") };

        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([_install, _updateAll, close]);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.AddRange([searchLabel, _search, listLabel, _list, detailsLabel, _details, buttons, _status]);
        Controls.Add(layout);
        CancelButton = close;
        ActiveControl = _search;
        Shown += async (_, _) => await LoadAsync();
    }

    private StoreItem? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count ? _shown[_list.SelectedIndex] : null;

    private async Task LoadAsync()
    {
        try
        {
            _all = await _actions.BrowseStoreAsync();
            if (IsDisposed)
                return;
            _status.Text = VoxStrings.Format("Store.Dialog.Count", _all.Count);
            Filter();
        }
        catch (AddOnStoreException ex)
        {
            if (!IsDisposed)
                _status.Text = _details.Text = ex.Message;
        }
    }

    private async Task ReloadAsync()
    {
        var keep = Selected?.Entry.Id;
        try { _all = await _actions.BrowseStoreAsync(); }
        catch (AddOnStoreException) { return; }
        if (IsDisposed)
            return;
        Filter();
        var index = _shown.ToList().FindIndex(i => i.Entry.Id == keep);
        if (index >= 0)
            _list.SelectedIndex = index;
    }

    private void Filter()
    {
        _shown = AddOnCatalogue.Search(_all, _search.Text);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in _shown)
            _list.Items.Add(Line(item));
        _list.EndUpdate();
        if (_shown.Count > 0)
            _list.SelectedIndex = 0;
        _updateAll.Enabled = _all.Any(i => i.UpdateAvailable);
        ShowSelected();
    }

    private static string Line(StoreItem item)
    {
        var state = item.Incompatible is not null ? VoxStrings.Get("Store.State.Incompatible")
            : item.UpdateAvailable ? VoxStrings.Format("Store.State.Update", item.Installed!)
            : item.Installed is not null ? VoxStrings.Get("Store.State.Installed")
            : string.Empty;
        return VoxStrings.Format("Store.ListLine", item.Entry.Name, item.Entry.Version, item.Entry.Author, state).TrimEnd(' ', ',');
    }

    private void ShowSelected()
    {
        var item = Selected;
        if (item is null)
        {
            _details.Text = string.Empty;
            _install.Enabled = false;
            return;
        }
        var lines = new List<string> { VoxStrings.Format("AddOns.Details.Title", item.Entry.Name, item.Entry.Version), VoxStrings.Format("AddOns.Details.Author", item.Entry.Author) };
        if (item.Entry.IsLabelPack)
            lines.Add(VoxStrings.Get("Store.Details.LabelPack"));
        if (item.Installed is { } installed)
            lines.Add(VoxStrings.Format("Store.Details.Installed", installed));
        if (item.Incompatible is { } reason)
            lines.Add(reason);
        if (!string.IsNullOrWhiteSpace(item.Entry.Description))
            lines.Add(item.Entry.Description);
        _details.Text = string.Join(Environment.NewLine, lines);
        _install.Text = VoxStrings.Get(item.UpdateAvailable ? "Store.Dialog.Update" : "Store.Dialog.Install");
        _install.Enabled = item.Incompatible is null && (item.Installed is null || item.UpdateAvailable);
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var message = await action();
            if (IsDisposed)
                return;
            if (message.Length > 0)
                _status.Text = message;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                _status.Text = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
