using System.Windows.Forms;
using Vox.Core.Localization;
using Vox.Core.Navigation;

namespace Vox.Core.Remote;

/// <summary>Runs <see cref="RemoteDialog"/> on its own STA thread, as WinForms requires.</summary>
public sealed class RemotePresenter : IRemotePresenter
{
    public Task<RemoteRequest?> ShowAsync(RemoteDialogDefaults defaults)
    {
        var tcs = new TaskCompletionSource<RemoteRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var dialog = new RemoteDialog(defaults);
                tcs.SetResult(dialog.ShowInForeground() == DialogResult.OK ? dialog.Request : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Vox-Remote",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// Remote access: let this computer be controlled (it shows a new session code to read to the
/// other person) or control another computer (type the code it shows), directly or through a
/// relay server.
/// </summary>
public sealed class RemoteDialog : VoxDialog
{
    private readonly RadioButton _controlled;
    private readonly RadioButton _controlling;
    private readonly CheckBox _relay;
    private readonly Label _addressLabel;
    private readonly TextBox _address;
    private readonly Label _portLabel;
    private readonly NumericUpDown _port;
    private readonly TextBox _code;
    private readonly Button _newCode;
    private readonly Button _ok;
    private readonly Label _error;
    private readonly RemoteDialogDefaults _defaults;

    public RemoteRequest? Request { get; private set; }

    public RemoteDialog(RemoteDialogDefaults defaults)
    {
        _defaults = defaults;
        Text = VoxStrings.Get("Remote.Title");
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.Dialog;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _controlled = new RadioButton { Text = VoxStrings.Get("Remote.Dialog.Controlled"), AutoSize = true, Checked = true };
        _controlling = new RadioButton { Text = VoxStrings.Get("Remote.Dialog.Controlling"), AutoSize = true };
        _relay = new CheckBox { Text = VoxStrings.Get("Remote.Dialog.Relay"), AutoSize = true, Checked = defaults.UseRelay };
        _addressLabel = new Label { AutoSize = true };
        _address = new TextBox { Width = 300 };
        _portLabel = new Label { Text = VoxStrings.Get("Remote.Dialog.Port"), AutoSize = true };
        _port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = Math.Clamp(defaults.Port, 1024, 65535), Width = 100 };
        _port.AccessibleName = _portLabel.Text.Replace("&", string.Empty);
        var codeLabel = new Label { Text = VoxStrings.Get("Remote.Dialog.Code"), AutoSize = true };
        _code = new TextBox { Width = 200, AccessibleName = codeLabel.Text.Replace("&", string.Empty) };
        _newCode = new Button { Text = VoxStrings.Get("Remote.Dialog.NewCode"), AutoSize = true };
        _newCode.Click += (_, _) => _code.Text = RemoteCode.New();
        _ok = new Button { AutoSize = true };
        _ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = VoxStrings.Get("Dialog.Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        _error = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(360, 0) };

        _controlled.CheckedChanged += (_, _) => Arrange();
        _relay.CheckedChanged += (_, _) => Arrange();

        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([_ok, cancel]);
        var codeRow = new FlowLayoutPanel { AutoSize = true };
        codeRow.Controls.AddRange([_code, _newCode]);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        layout.Controls.AddRange([_controlled, _controlling, _relay, _addressLabel, _address, _portLabel, _port, codeLabel, codeRow, buttons, _error]);
        Controls.Add(layout);
        AcceptButton = _ok;
        CancelButton = cancel;
        ActiveControl = _controlled;
        _code.Text = defaults.NewCode;
        Arrange();
    }

    private void Arrange()
    {
        bool controlled = _controlled.Checked;
        bool relay = _relay.Checked;
        _addressLabel.Text = VoxStrings.Get(relay ? "Remote.Dialog.RelayAddress" : "Remote.Dialog.Address");
        _address.AccessibleName = _addressLabel.Text.Replace("&", string.Empty);
        _address.Text = relay ? _defaults.Relay ?? string.Empty : _defaults.Address ?? string.Empty;
        _address.Visible = _addressLabel.Visible = relay || !controlled;
        _port.Visible = _portLabel.Visible = controlled && !relay;
        _code.ReadOnly = controlled;
        _newCode.Visible = controlled;
        if (controlled && RemoteCode.Normalize(_code.Text) is null)
            _code.Text = RemoteCode.New();
        else if (!controlled && _code.Text == _defaults.NewCode)
            _code.Text = string.Empty;
        _ok.Text = VoxStrings.Get(controlled ? "Remote.Dialog.Start" : "Remote.Dialog.Connect");
    }

    private void Accept()
    {
        var code = RemoteCode.Normalize(_code.Text);
        if (code is null)
        {
            Refuse(VoxStrings.Get("Remote.Dialog.BadCode"), _code);
            return;
        }
        bool controlled = _controlled.Checked;
        bool relay = _relay.Checked;
        RemoteEndpoint endpoint;
        string? address = null;
        if (relay || !controlled)
        {
            if (RemoteEndpoint.ParseAddress(_address.Text, RemoteEndpoint.DefaultPort) is not { } parsed)
            {
                Refuse(VoxStrings.Get("Remote.Dialog.BadAddress"), _address);
                return;
            }
            address = _address.Text.Trim();
            endpoint = new RemoteEndpoint(relay, parsed.Host, parsed.Port);
        }
        else
        {
            endpoint = new RemoteEndpoint(false, null, (int)_port.Value);
        }
        Request = new RemoteRequest(controlled ? RemoteRole.Controlled : RemoteRole.Controlling, endpoint, code, address);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Refuse(string message, Control focus)
    {
        _error.Text = message;
        focus.AccessibleDescription = message;
        focus.Focus();
    }
}
