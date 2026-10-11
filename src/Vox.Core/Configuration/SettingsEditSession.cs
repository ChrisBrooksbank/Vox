namespace Vox.Core.Configuration;

/// <summary>
/// The settings dialog's changes, kept apart from its controls. Each change is made to the
/// settings as they are now (not as they were when the dialog opened), so a setting changed
/// meanwhile elsewhere (the settings ring, Insert+M, an edited settings file) isn't undone; Cancel
/// puts back only the fields the dialog changed.
/// </summary>
public sealed class SettingsEditSession
{
    private readonly Func<VoxSettings> _current;
    private readonly Action<VoxSettings> _apply;
    private readonly VoxSettings _opening;
    private readonly List<SettingField> _changed = [];

    /// <param name="current">The settings as they are now (<c>IOptionsMonitor.CurrentValue</c>).</param>
    /// <param name="apply">Applies new settings at once.</param>
    public SettingsEditSession(Func<VoxSettings> current, Action<VoxSettings> apply)
    {
        _current = current;
        _apply = apply;
        _opening = current();
    }

    /// <summary>The settings when the dialog opened (what its controls start from).</summary>
    public VoxSettings Opening => _opening;

    /// <summary>Applies <paramref name="change"/>, made by <paramref name="field"/>, to the current settings.</summary>
    public void Change(SettingField field, Func<VoxSettings, VoxSettings> change)
    {
        var now = _current();
        var updated = change(now);
        if (updated == now)
            return;
        if (!_changed.Any(f => ReferenceEquals(f, field)))
            _changed.Add(field);
        _apply(updated);
    }

    /// <summary>Puts the fields this dialog changed back as they were when it opened (Cancel).</summary>
    public void Revert()
    {
        var now = _current();
        var reverted = now;
        foreach (var field in _changed)
        {
            reverted = field switch
            {
                ToggleField toggle => toggle.Set(reverted, toggle.Get(_opening)),
                NumberField number => number.Set(reverted, number.Get(_opening)),
                ChoiceField choice => choice.Set(reverted, choice.Get(_opening)),
                _ => reverted,
            };
        }
        _changed.Clear();
        if (reverted != now)
            _apply(reverted);
    }
}
