using System.Runtime.InteropServices;
using System.Windows.Forms;
using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// Accessible WinForms dialog opened by Insert+F7.
///
/// Shows switchable tabs for Headings, Links, Landmarks, Form Fields (with a field kind
/// choice: all, edits, combo boxes, check boxes or radio buttons), Buttons and Tables.
/// Type to filter the list. Press Enter to jump to the selected element.
/// Data comes from pre-built VBufferDocument indices (instant on large pages).
/// </summary>
public sealed class ElementsListDialog : Form
{
    // -------------------------------------------------------------------------
    // Public result
    // -------------------------------------------------------------------------

    /// <summary>
    /// The node the user chose to jump to. Null if the dialog was cancelled.
    /// Set before the dialog is closed with DialogResult.OK.
    /// </summary>
    public VBufferNode? SelectedNode { get; private set; }

    // -------------------------------------------------------------------------
    // Controls
    // -------------------------------------------------------------------------

    private readonly TabControl _tabControl;
    private readonly Panel _kindPanel;
    private readonly ComboBox _kindBox;
    private readonly TextBox _filterBox;
    private readonly ListBox _listBox;
    private readonly Button _jumpButton;
    private readonly Button _cancelButton;

    // -------------------------------------------------------------------------
    // View model
    // -------------------------------------------------------------------------

    private readonly ElementsListViewModel _viewModel;

    // The virtual cursor's node when the dialog opened: each tab starts at the element there
    private readonly VBufferNode? _currentNode;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public ElementsListDialog(VBufferDocument document, VBufferNode? currentNode = null)
    {
        _viewModel = new ElementsListViewModel(document);
        _currentNode = currentNode;

        // ---- Form properties ------------------------------------------------
        Text = "Elements List";
        Size = new System.Drawing.Size(480, 420);
        MinimumSize = new System.Drawing.Size(360, 320);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        KeyPreview = true;
        AccessibleName = "Elements List";
        AccessibleRole = AccessibleRole.Dialog;

        // ---- Layout ---------------------------------------------------------
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8),
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // ---- Tabs -----------------------------------------------------------
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Element type tabs",
            AccessibleRole = AccessibleRole.PageTabList,
        };
        foreach (var name in ElementsListViewModel.TabNames)
            _tabControl.TabPages.Add(new TabPage(name) { AccessibleName = $"{name} tab" });
        _tabControl.SelectedIndexChanged += OnTabChanged;
        mainPanel.Controls.Add(_tabControl, 0, 0);

        // ---- Form field kind (Form Fields tab only) --------------------------
        _kindPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Visible = false,
        };
        _kindPanel.Controls.Add(new Label
        {
            Text = "Field &kind:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 4, 0),
        });
        _kindBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Field kind",
            Width = 160,
        };
        foreach (var name in ElementsListViewModel.FormFieldKindNames)
            _kindBox.Items.Add(name);
        _kindBox.SelectedIndex = 0;
        _kindBox.SelectedIndexChanged += OnKindChanged;
        _kindPanel.Controls.Add(_kindBox);
        mainPanel.Controls.Add(_kindPanel, 0, 1);

        // ---- List box -------------------------------------------------------
        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Elements",
            AccessibleRole = AccessibleRole.List,
            IntegralHeight = false,
        };
        _listBox.KeyDown += OnListKeyDown;
        _listBox.KeyPress += OnListKeyPress;
        _listBox.DoubleClick += OnJump;
        mainPanel.Controls.Add(_listBox, 0, 2);

        // ---- Filter + buttons row -------------------------------------------
        var bottomPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            AutoSize = true,
        };
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var filterLabel = new Label
        {
            Text = "Filter:",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        bottomPanel.Controls.Add(filterLabel, 0, 0);

        _filterBox = new TextBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Filter",
            AccessibleDescription = "Type to filter the list",
        };
        _filterBox.TextChanged += OnFilterChanged;
        _filterBox.KeyDown    += OnFilterKeyDown;
        bottomPanel.Controls.Add(_filterBox, 0, 1);
        bottomPanel.SetColumnSpan(_filterBox, 3);

        _jumpButton = new Button
        {
            Text = "Jump",
            AccessibleName = "Jump to element",
            DialogResult = DialogResult.None,
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
        };
        _jumpButton.Click += OnJump;
        bottomPanel.Controls.Add(_jumpButton, 1, 0);

        _cancelButton = new Button
        {
            Text = "Cancel",
            AccessibleName = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
        };
        bottomPanel.Controls.Add(_cancelButton, 2, 0);
        mainPanel.Controls.Add(bottomPanel, 0, 3);
        Controls.Add(mainPanel);

        AcceptButton = _jumpButton;
        CancelButton = _cancelButton;

        // ---- Initial population ---------------------------------------------
        RefreshList();

        // Start in the list (not the tab strip) so arrows browse items and typing filters
        ActiveControl = _listBox;
    }

    // -------------------------------------------------------------------------
    // Public factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates and shows the dialog modally on the current thread.
    /// Returns the node the user jumped to, or null if cancelled.
    /// MUST be called on an STA thread (e.g. a dedicated WinForms thread).
    /// </summary>
    public static VBufferNode? ShowModal(VBufferDocument document, VBufferNode? currentNode = null)
    {
        Application.EnableVisualStyles();
        using var dlg = new ElementsListDialog(document, currentNode);
        // Opened from a background thread in response to a global hotkey: make sure it
        // comes to the front and takes keyboard focus.
        dlg.StartPosition = FormStartPosition.CenterScreen;
        dlg.TopMost = true;
        dlg.Shown += (_, _) => dlg.BringToForeground();
        var result = dlg.ShowDialog();
        return result == DialogResult.OK ? dlg.SelectedNode : null;
    }

    // -------------------------------------------------------------------------
    // Foreground handling
    // -------------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    private const int ActivationTimeoutMs = 2000;
    private bool _activated;

    /// <summary>
    /// Takes the foreground. Vox isn't the foreground process when the hotkey fires (the browser
    /// is), so plain Activate() is blocked by the foreground lock and only flashes the taskbar.
    /// Briefly attaching to the foreground thread's input state lifts that restriction.
    /// If activation still hasn't happened after a moment, the dialog closes as cancelled so
    /// browse-mode keys (disabled while it is open) come back.
    /// </summary>
    private void BringToForeground()
    {
        try
        {
            var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), nint.Zero);
            var thisThread = GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != thisThread
                            && AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                BringWindowToTop(Handle);
                SetForegroundWindow(Handle);
                Activate();
            }
            finally
            {
                if (attached)
                    AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
        catch (Exception)
        {
            Activate();
        }

        var timer = new System.Windows.Forms.Timer { Interval = ActivationTimeoutMs };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            if (!_activated && Visible)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        timer.Start();
    }

    protected override void OnActivated(EventArgs e)
    {
        _activated = true;
        base.OnActivated(e);
    }

    /// <summary>
    /// Focus leaving the dialog (Alt+Tab, a click elsewhere) cancels it, so it can never sit
    /// unfocused in the background with browse-mode keys disabled.
    /// </summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Visible && DialogResult == DialogResult.None)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    private void OnTabChanged(object? sender, EventArgs e)
    {
        _viewModel.SelectedTabIndex = _tabControl.SelectedIndex;
        // Sync filter box to the (reset) viewmodel filter without re-triggering OnFilterChanged
        _filterBox.TextChanged -= OnFilterChanged;
        _filterBox.Text = string.Empty;
        _filterBox.TextChanged += OnFilterChanged;
        _kindPanel.Visible = _tabControl.SelectedIndex == ElementsListViewModel.FormFieldsTab;
        RefreshList();
    }

    private void OnKindChanged(object? sender, EventArgs e)
    {
        _viewModel.FormFieldKind = (ElementsFormFieldKind)Math.Max(0, _kindBox.SelectedIndex);
        RefreshList();
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        _viewModel.FilterText = _filterBox.Text;
        RefreshList();
    }

    private void OnJump(object? sender, EventArgs e) => TryJump();

    // -------------------------------------------------------------------------
    // Jump action
    // -------------------------------------------------------------------------

    private void TryJump()
    {
        if (_listBox.SelectedItem is NodeItem item)
        {
            SelectedNode = item.Node;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // -------------------------------------------------------------------------
    // List refresh
    // -------------------------------------------------------------------------

    private void RefreshList()
    {
        _listBox.BeginUpdate();
        try
        {
            _listBox.Items.Clear();
            var items = _viewModel.GetFilteredItems();
            foreach (var node in items)
            {
                _listBox.Items.Add(new NodeItem(node, _viewModel.DisplayText(node)));
            }
            if (_listBox.Items.Count > 0)
            {
                // Unfiltered, start at the element where the user is; filtered, at the best match
                _listBox.SelectedIndex = _viewModel.FilterText.Trim().Length == 0
                    ? Math.Max(0, ElementsListViewModel.InitialSelectionIndex(items, _currentNode))
                    : 0;
            }
        }
        finally
        {
            _listBox.EndUpdate();
        }

        _jumpButton.Enabled = _listBox.Items.Count > 0;
    }

    // -------------------------------------------------------------------------
    // Keyboard handling
    // -------------------------------------------------------------------------

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _listBox.Items.Count > 0)
        {
            _listBox.Focus();
            if (_listBox.SelectedIndex < 0)
                _listBox.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Return)
        {
            TryJump();
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up && _listBox.SelectedIndex == 0)
        {
            _filterBox.Focus();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Return)
        {
            TryJump();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Typing a character in the list moves it to the filter box, so "type to filter" works
    /// wherever focus is (ListBox's own first-letter search is replaced).
    /// </summary>
    private void OnListKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
            return;

        _filterBox.Focus();
        _filterBox.AppendText(e.KeyChar.ToString());
        e.Handled = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Tab))
        {
            int next = (_tabControl.SelectedIndex + 1) % _tabControl.TabCount;
            _tabControl.SelectedIndex = next;
            return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            int prev = (_tabControl.SelectedIndex - 1 + _tabControl.TabCount) % _tabControl.TabCount;
            _tabControl.SelectedIndex = prev;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // -------------------------------------------------------------------------
    // Helper types
    // -------------------------------------------------------------------------

    private sealed class NodeItem
    {
        public VBufferNode Node { get; }
        private readonly string _text;

        public NodeItem(VBufferNode node, string text)
        {
            Node = node;
            _text = text;
        }

        public override string ToString() => _text;
    }
}
