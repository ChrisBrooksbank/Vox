using System.Windows.Forms;

namespace Vox.Core.Speech;

/// <summary>
/// A window showing what Vox says, for developers and sighted helpers. It never takes focus (so it
/// can't steal keystrokes from the application being used) and stays on top. It runs on its own
/// UI thread; toggled by the ToggleSpeechViewer command.
/// </summary>
public sealed class SpeechViewer : IDisposable
{
    private readonly SpeechHistory _history;
    private readonly object _lock = new();
    private ViewerForm? _form;
    private Thread? _thread;

    public SpeechViewer(SpeechHistory history)
    {
        _history = history;
    }

    public bool IsOpen
    {
        get { lock (_lock) return _form is not null; }
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        var ready = new ManualResetEventSlim();
        lock (_lock)
        {
            if (_form is not null || _thread is { IsAlive: true })
                return;
            _thread = new Thread(() =>
            {
                var form = new ViewerForm(_history);
                lock (_lock) _form = form;
                ready.Set();
                Application.Run(form);
                lock (_lock) _form = null;
            })
            { IsBackground = true, Name = "Vox-SpeechViewer" };
            if (OperatingSystem.IsWindows())
                _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Close()
    {
        ViewerForm? form;
        lock (_lock) form = _form;
        if (form is not null && form.IsHandleCreated)
            form.BeginInvoke(form.Close);
    }

    public void Dispose() => Close();

    private sealed class ViewerForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private readonly SpeechHistory _history;
        private readonly TextBox _text;

        public ViewerForm(SpeechHistory history)
        {
            _history = history;
            Text = "Vox speech viewer";
            TopMost = true;
            ShowInTaskbar = false;
            Width = 500;
            Height = 300;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(Screen.PrimaryScreen?.WorkingArea.Right - Width ?? 0, 0);
            _text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Segoe UI", 11),
                TabStop = false,
            };
            Controls.Add(_text);
            _text.Lines = history.Entries.ToArray();
            history.Added += OnAdded;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        private void OnAdded(object? sender, string text)
        {
            if (!IsHandleCreated)
                return;
            BeginInvoke(() =>
            {
                _text.AppendText((_text.TextLength > 0 ? Environment.NewLine : string.Empty) + text);
            });
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _history.Added -= OnAdded;
            base.OnFormClosed(e);
        }
    }
}
