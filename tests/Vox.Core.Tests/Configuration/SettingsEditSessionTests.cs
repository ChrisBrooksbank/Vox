using Vox.Core.Configuration;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public class SettingsEditSessionTests
{
    private VoxSettings _settings = new() { SpeechRateWpm = 200, MouseTracking = false };

    private static readonly NumberField Rate =
        new("&Rate", 50, 500, 10, s => s.SpeechRateWpm, (s, v) => s with { SpeechRateWpm = v });

    private SettingsEditSession Open() => new(() => _settings, s => _settings = s);

    [Fact]
    public void Change_KeepsASettingChangedElsewhereWhileTheDialogIsOpen()
    {
        var session = Open();
        _settings = _settings with { MouseTracking = true }; // Insert+M while the dialog is open

        session.Change(Rate, s => Rate.Set(s, 300));

        Assert.Equal(300, _settings.SpeechRateWpm);
        Assert.True(_settings.MouseTracking);
    }

    [Fact]
    public void Revert_PutsBackOnlyWhatTheDialogChanged()
    {
        var session = Open();
        session.Change(Rate, s => Rate.Set(s, 300));
        session.Change(Rate, s => Rate.Set(s, 350));
        _settings = _settings with { MouseTracking = true };

        session.Revert();

        Assert.Equal(200, _settings.SpeechRateWpm);
        Assert.True(_settings.MouseTracking);
    }

    [Fact]
    public void Revert_WithoutChanges_AppliesNothing()
    {
        int applied = 0;
        var session = new SettingsEditSession(() => _settings, _ => applied++);

        session.Revert();

        Assert.Equal(0, applied);
    }
}
