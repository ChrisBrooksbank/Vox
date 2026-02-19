using System.Windows.Forms;
using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// Accessible WinForms dialog opened by Insert+F7.
///
/// Shows switchable tabs for Headings, Links, Landmarks, and FormFields.
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
    private readonly TextBox _filterBox;
    private readonly ListBox _listBox;
    private readonly Button _jumpButton;
    private readonly Button _cancelButton;

    // -------------------------------------------------------------------------
    // View model
    // -------------------------------------------------------------------------

    private readonly ElementsListViewModel _viewModel;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public ElementsListDialog(VBufferDocument document)
    {
        _viewModel = new ElementsListViewModel(document);

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
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8),
        };
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
        _tabControl.TabPages.AddRange(new[]
        {
            new TabPage("Headings")    { AccessibleName = "Headings tab" },
            new TabPage("Links")       { AccessibleName = "Links tab" },
            new TabPage("Landmarks")   { AccessibleName = "Landmarks tab" },
            new TabPage("Form Fields") { AccessibleName = "Form Fields tab" },
        });
        _tabControl.SelectedIndexChanged += OnTabChanged;
        mainPanel.Controls.Add(_tabControl, 0, 0);

        // ---- List box -------------------------------------------------------
        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Elements",
            AccessibleRole = AccessibleRole.List,
            IntegralHeight = false,
        };
        _listBox.KeyDown += OnListKeyDown;
        _listBox.DoubleClick += OnJump;
        mainPanel.Controls.Add(_listBox, 0, 1);

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
        mainPanel.Controls.Add(bottomPanel, 0, 2);
        Controls.Add(mainPanel);

        AcceptButton = _jumpButton;
        CancelButton = _cancelButton;

        // ---- Initial population ---------------------------------------------
        RefreshList();
    }

    // -------------------------------------------------------------------------
    // Public factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates and shows the dialog modally on the current thread.
    /// Returns the node the user jumped to, or null if cancelled.
    /// MUST be called on an STA thread (e.g. a dedicated WinForms thread).
    /// </summary>
    public static VBufferNode? ShowModal(VBufferDocument document)
    {
        Application.EnableVisualStyles();
        using var dlg = new ElementsListDialog(document);
        var result = dlg.ShowDialog();
        return result == DialogResult.OK ? dlg.SelectedNode : null;
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
            foreach (var node in _viewModel.GetFilteredItems())
            {
                _listBox.Items.Add(new NodeItem(node));
            }
            if (_listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
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

        public NodeItem(VBufferNode node)
        {
            Node = node;
        }

        public override string ToString() =>
            ElementsListViewModel.GetDisplayText(Node);
    }
}
