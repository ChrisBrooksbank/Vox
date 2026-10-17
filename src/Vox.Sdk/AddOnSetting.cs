namespace Vox.Sdk;

/// <summary>
/// One setting an add-on offers, shown on the add-on's page of Vox's settings dialog and kept with
/// Vox's own settings (so configuration profiles and the organization's policy apply to it). Added
/// with <see cref="IAddOnHost.AddSettings"/>; read with <see cref="IAddOnHost.GetSetting"/>. Since API 1.1.
/// </summary>
public abstract record AddOnSetting(string Id, string Label);

/// <summary>An on/off setting. Its value reads as "true" or "false".</summary>
public sealed record AddOnToggleSetting(string Id, string Label) : AddOnSetting(Id, Label)
{
    public bool Default { get; init; }
}

/// <summary>A whole number in a range. Its value reads as digits.</summary>
public sealed record AddOnNumberSetting(string Id, string Label) : AddOnSetting(Id, Label)
{
    public int Min { get; init; }
    public int Max { get; init; } = 100;
    public int Step { get; init; } = 1;
    public int Default { get; init; }
}

/// <summary>One of a list of values. Its value reads as the chosen <see cref="AddOnChoice.Value"/>.</summary>
public sealed record AddOnChoiceSetting(string Id, string Label) : AddOnSetting(Id, Label)
{
    public IReadOnlyList<AddOnChoice> Choices { get; init; } = [];

    /// <summary>The <see cref="AddOnChoice.Value"/> used until the user picks one (the first choice when null).</summary>
    public string? Default { get; init; }
}

/// <summary>A choice: the <paramref name="Value"/> the add-on reads and the <paramref name="Text"/> people see.</summary>
public sealed record AddOnChoice(string Value, string Text);
