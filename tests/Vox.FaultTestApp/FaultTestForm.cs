namespace Vox.FaultTestApp;

/// <summary>
/// "Vox fault test": Before, Faulty button, After and a Notes text box, in tab order. Only the
/// faulty button's provider misbehaves; the rest is ordinary WinForms.
/// </summary>
public sealed class FaultTestForm : Form
{
    private readonly TextBox _notes;

    public FaultTestForm()
    {
        Text = "Vox fault test";
        Width = 420;
        Height = 200;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        var before = new Button { Text = "Before", AutoSize = true, TabIndex = 0 };
        var faulty = new FaultyButton { Text = "Faulty button", AutoSize = true, TabIndex = 1 };
        var after = new Button { Text = "After", AutoSize = true, TabIndex = 2 };
        _notes = new TextBox { AccessibleName = "Notes", Width = 360, TabIndex = 3 };
        panel.Controls.AddRange([before, faulty, after, _notes]);
        Controls.Add(panel);
        ActiveControl = before;
    }

    /// <summary>The text typed into Notes (UI thread).</summary>
    public string NotesText => _notes.Text;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Console.WriteLine("ready");
    }
}

/// <summary>A button whose accessible object injects <see cref="Faults"/> into every property UIA reads.</summary>
public sealed class FaultyButton : Button
{
    protected override AccessibleObject CreateAccessibilityInstance() => new FaultyAccessibleObject(this);

    private sealed class FaultyAccessibleObject(FaultyButton owner) : ControlAccessibleObject(owner)
    {
        public override string? Name
        {
            get
            {
                Faults.Inject(nameof(Name));
                return base.Name;
            }
            set => base.Name = value;
        }

        public override AccessibleRole Role
        {
            get
            {
                Faults.Inject(nameof(Role));
                return AccessibleRole.PushButton;
            }
        }

        public override AccessibleStates State
        {
            get
            {
                Faults.Inject(nameof(State));
                return base.State;
            }
        }

        public override string? Description
        {
            get
            {
                Faults.Inject(nameof(Description));
                return base.Description;
            }
        }

        public override string DefaultAction => "Press";

        public override void DoDefaultAction() => owner.PerformClick();
    }
}
