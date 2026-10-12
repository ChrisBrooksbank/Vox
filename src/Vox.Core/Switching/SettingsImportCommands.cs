using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Navigation;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Switching;

/// <summary>What the first-run wizard needs to offer an import: the sources found, importing one, and its report.</summary>
public interface ISettingsImportOffer
{
    /// <summary>The screen readers whose settings are on this computer.</summary>
    IReadOnlyList<ImportSource> FindSources();

    /// <summary>Brings <paramref name="source"/>'s settings over and saves them; the report, or null when it failed.</summary>
    ImportReport? Import(ImportSource source);

    /// <summary>Shows an import's report to read (it has been applied).</summary>
    void ShowReport(ImportReport report);
}

/// <summary>
/// Runs the Vox menu's "Import settings from NVDA or JAWS": finds their settings, prepares each
/// import, shows the reports to review (nothing is changed until the user chooses Import), then
/// saves the chosen one, with backups, and reloads the keys. Also the wizard's
/// <see cref="ISettingsImportOffer"/>. Never on secure screens (no <see cref="SettingsImport"/>).
/// </summary>
public sealed class SettingsImportCommands(
    SettingsImport? import,
    ISettingsImportPresenter presenter,
    IOptionsMonitor<VoxSettings> settings,
    Action<VoxSettings> saveSettings,
    Action reloadKeyMap,
    BrowseModeController browse,
    IEventSink pipeline,
    ILogger<SettingsImportCommands> logger,
    SpeechQueue? speechQueue = null) : ISettingsImportOffer
{
    public bool TryHandle(NavigationCommand command)
    {
        if (command != NavigationCommand.ImportSettings)
            return false;
        if (import is null)
        {
            Say(VoxStrings.Get("Import.NotHere"));
            return true;
        }
        var sources = FindSources();
        if (sources.Count == 0)
            Say(VoxStrings.Get("Import.NoneFound"));
        else if (browse.BeginOwnDialog())
            _ = ReviewAndImportAsync(sources);
        return true;
    }

    public IReadOnlyList<ImportSource> FindSources() => import?.FindSources() ?? [];

    /// <summary>Prepares an import from each source, shows the reports, and applies the one chosen.</summary>
    public async Task ReviewAndImportAsync(IReadOnlyList<ImportSource> sources)
    {
        SettingsImportResult? chosen = null;
        VoxSettings current = settings.CurrentValue;
        try
        {
            var prepared = await Task.Run(() => sources
                .Select(s => import!.Prepare(s, current))
                .OfType<SettingsImportResult>()
                .ToList()).ConfigureAwait(false);
            var previews = prepared.Select(p => new ImportPreview(p.Report.Source, ImportReportText.Format(p.Report, applied: false))).ToList();
            if (previews.Count > 0 && await presenter.ReviewAsync(previews).ConfigureAwait(false) is { } source)
                chosen = prepared.First(p => p.Report.Source == source);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Settings import dialog failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow, QuietFocusReturn: chosen is not null));
        if (chosen is not null)
            Apply(chosen, current, announce: true);
    }

    public ImportReport? Import(ImportSource source)
    {
        var current = settings.CurrentValue;
        try
        {
            if (import?.Prepare(source, current) is not { } result)
                return null;
            // The wizard says how it went itself
            return Apply(result, current, announce: false) ? result.Report : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Import of {Source} settings failed", source);
            return null;
        }
    }

    public void ShowReport(ImportReport report)
    {
        if (!browse.BeginOwnDialog())
            return;
        _ = ShowAsync(report);
    }

    private async Task ShowAsync(ImportReport report)
    {
        try
        {
            await presenter.ShowAsync(ImportReportText.Title(report.Source), ImportReportText.Format(report, applied: true)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Settings import report failed");
        }
        pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
    }

    private bool Apply(SettingsImportResult result, VoxSettings current, bool announce)
    {
        string name = ImportReportText.SourceName(result.Report.Source);
        try
        {
            import!.Apply(result, current, saveSettings);
            if (result.Files.Count > 0)
                reloadKeyMap();
            int changes = result.Report.Imported.Count(i => i.Changed);
            // Only what was imported is logged: the report has no typed text, values are settings
            logger.LogInformation("Imported {Changes} settings from {Source}; {Skipped} skipped; backups: {Backups}",
                changes, result.Report.Source, result.Report.Skipped.Count, result.Report.Backups.Count);
            if (announce)
                Say(changes == 0 ? VoxStrings.Format("Import.NothingChanged", name) : VoxStrings.Format("Import.Done", name, changes));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save the settings imported from {Source}", result.Report.Source);
            if (announce)
                Say(VoxStrings.Format("Import.Failed", ex.Message));
            return false;
        }
    }

    private void Say(string text) => speechQueue?.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
}
