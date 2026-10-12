using System.Text;
using Vox.Core.Localization;

namespace Vox.Core.Switching;

/// <summary>
/// An <see cref="ImportReport"/> as a document to read line by line: a title, whether anything has
/// been changed yet, a summary, then what was brought over (with the value it replaced), what was
/// not and why, the files read and the backups kept. In Vox's language.
/// </summary>
public static class ImportReportText
{
    /// <summary>"NVDA" or "JAWS".</summary>
    public static string SourceName(ImportSource source) => VoxStrings.Get($"Import.Source.{source}");

    public static string Title(ImportSource source) => VoxStrings.Format("Import.Title", SourceName(source));

    /// <param name="applied">The import has been saved (else it is a preview to review first).</param>
    public static string Format(ImportReport report, bool applied)
    {
        var text = new StringBuilder();
        text.AppendLine(Title(report.Source));
        text.AppendLine(VoxStrings.Get(applied ? "Import.Applied" : "Import.Preview"));
        text.AppendLine(VoxStrings.Format("Import.Summary", report.Imported.Count, report.Skipped.Count));

        if (report.Imported.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(VoxStrings.Get("Import.ImportedHeading"));
            foreach (var item in report.Imported)
                text.AppendLine(Line(item));
        }
        if (report.Skipped.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(VoxStrings.Get("Import.SkippedHeading"));
            foreach (var item in report.Skipped)
                text.AppendLine(Line(item));
        }
        Section(text, "Import.FilesHeading", report.Files);
        Section(text, "Import.BackupsHeading", report.Backups);
        return text.ToString().TrimEnd();
    }

    private static string Value(string? value) => value ?? VoxStrings.Get("Import.NoValue");

    private static string Line(ImportedItem item)
    {
        if (!item.Changed)
            return VoxStrings.Format("Import.ImportedSame", item.Target, Value(item.Value), item.Source);
        return item.PreviousValue is null
            ? VoxStrings.Format("Import.ImportedNew", item.Target, Value(item.Value), item.Source)
            : VoxStrings.Format("Import.ImportedChanged", item.Target, Value(item.Value), item.PreviousValue, item.Source);
    }

    private static string Line(SkippedItem item)
    {
        string reason = VoxStrings.Get($"Import.Reason.{item.Reason}");
        return item.Detail is null
            ? VoxStrings.Format("Import.SkippedItem", item.Source, Value(item.Value), reason)
            : VoxStrings.Format("Import.SkippedItemDetail", item.Source, Value(item.Value), reason, item.Detail);
    }

    private static void Section(StringBuilder text, string heading, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return;
        text.AppendLine();
        text.AppendLine(VoxStrings.Get(heading));
        foreach (var line in lines)
            text.AppendLine(line);
    }
}
