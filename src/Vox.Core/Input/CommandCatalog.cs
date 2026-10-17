using Vox.Core.Localization;

namespace Vox.Core.Input;

/// <summary>Groups commands for the input gestures dialog, help and the user guide.</summary>
public enum CommandCategory
{
    QuickNavigation,
    Reading,
    BrowseMode,
    Tables,
    FindAndSelect,
    ObjectNavigation,
    Review,
    Mouse,
    Magnifier,
    Speech,
    Braille,
    /// <summary>Commands of one application's module (Word, Excel, ...), only while it is in the foreground.</summary>
    Applications,
    System,
}

/// <summary>What a command is called and does, for people (help mode, the gestures dialog, the guide).</summary>
public sealed record CommandInfo(NavigationCommand Command, string Name, string Description, CommandCategory Category);

/// <summary>
/// The one source of every command's name, description and category, used by input help,
/// the input gestures dialog and the generated keyboard reference. Names and descriptions are
/// the strings <c>Command.&lt;command&gt;.Name</c> and <c>.Description</c>, read when asked for,
/// so they follow the language setting.
/// </summary>
public static class CommandCatalog
{
    // Every command with an entry, by category, in the order the categories list them
    private static readonly Dictionary<NavigationCommand, CommandCategory> Categories = Build();

    /// <summary>Every command, in category order then the order listed here.</summary>
    public static IReadOnlyList<CommandInfo> All => Categories.OrderBy(c => c.Value).Select(c => Info(c.Key, c.Value)).ToList();

    public static CommandInfo Describe(NavigationCommand command) =>
        Categories.TryGetValue(command, out var category)
            ? Info(command, category)
            : new CommandInfo(command, command.ToString(), string.Empty, CommandCategory.System);

    /// <summary>True when <paramref name="command"/> has an entry (a test checks every command has).</summary>
    public static bool Has(NavigationCommand command) => Categories.ContainsKey(command);

    // Commands that report something on request: the speech they cause is kept in speech-on-demand
    // mode. Every other command (navigation, toggles, dialogs, ...) is dropped there.
    private static readonly HashSet<NavigationCommand> OnDemandReports =
    [
        NavigationCommand.SayTime, NavigationCommand.SayBattery, NavigationCommand.SayTitle,
        NavigationCommand.SayFocus, NavigationCommand.SayStatusBar, NavigationCommand.ReadWindow,
        NavigationCommand.ReadCurrentLine, NavigationCommand.ReadCurrentWord, NavigationCommand.ReadCurrentChar,
        NavigationCommand.ReadSelection, NavigationCommand.ReadFormatting, NavigationCommand.SayAll,
        NavigationCommand.SayLinkUrl, NavigationCommand.PageSummary, NavigationCommand.ReportNavigator,
        NavigationCommand.ReviewCurrentLine, NavigationCommand.ReviewCurrentWord, NavigationCommand.ReviewCurrentChar,
        NavigationCommand.TeamsCallState, NavigationCommand.ZoomCallState,
    ];

    /// <summary>True when <paramref name="command"/> reports something on request (kept in speech on demand).</summary>
    public static bool IsOnDemandReport(NavigationCommand command) => OnDemandReports.Contains(command);

    /// <summary>A category as people read it.</summary>
    public static string CategoryName(CommandCategory category) => VoxStrings.Get($"CommandCategory.{category}");

    private static CommandInfo Info(NavigationCommand command, CommandCategory category) =>
        new(command, VoxStrings.Get($"Command.{command}.Name"), VoxStrings.Get($"Command.{command}.Description"), category);

    private static Dictionary<NavigationCommand, CommandCategory> Build()
    {
        var commands = new Dictionary<NavigationCommand, CommandCategory>();
        void Add(CommandCategory category, params NavigationCommand[] inCategory)
        {
            foreach (var command in inCategory)
                commands.Add(command, category);
        }

        Add(CommandCategory.QuickNavigation,
            NavigationCommand.NextHeading, NavigationCommand.PrevHeading, NavigationCommand.NextLink,
            NavigationCommand.PrevLink, NavigationCommand.NextLandmark, NavigationCommand.PrevLandmark,
            NavigationCommand.NextFormField, NavigationCommand.PrevFormField, NavigationCommand.NextTable,
            NavigationCommand.PrevTable, NavigationCommand.NextFocusable, NavigationCommand.PrevFocusable,
            NavigationCommand.NextButton, NavigationCommand.PrevButton, NavigationCommand.NextEdit,
            NavigationCommand.PrevEdit, NavigationCommand.NextComboBox, NavigationCommand.PrevComboBox,
            NavigationCommand.NextCheckBox, NavigationCommand.PrevCheckBox, NavigationCommand.NextRadioButton,
            NavigationCommand.PrevRadioButton, NavigationCommand.NextList, NavigationCommand.PrevList,
            NavigationCommand.NextListItem, NavigationCommand.PrevListItem, NavigationCommand.NextGraphic,
            NavigationCommand.PrevGraphic, NavigationCommand.NextBlockQuote, NavigationCommand.PrevBlockQuote,
            NavigationCommand.NextFrame, NavigationCommand.PrevFrame, NavigationCommand.NextSeparator,
            NavigationCommand.PrevSeparator, NavigationCommand.NextEmbeddedObject, NavigationCommand.PrevEmbeddedObject,
            NavigationCommand.NextVisitedLink, NavigationCommand.PrevVisitedLink, NavigationCommand.NextUnvisitedLink,
            NavigationCommand.PrevUnvisitedLink, NavigationCommand.NextNonLinkText, NavigationCommand.PrevNonLinkText,
            NavigationCommand.NextTextParagraph, NavigationCommand.PrevTextParagraph,
            NavigationCommand.NextUnvisitedHeading, NavigationCommand.PrevUnvisitedHeading,
            NavigationCommand.HeadingLevel1, NavigationCommand.PrevHeadingLevel1, NavigationCommand.HeadingLevel2,
            NavigationCommand.PrevHeadingLevel2, NavigationCommand.HeadingLevel3, NavigationCommand.PrevHeadingLevel3,
            NavigationCommand.HeadingLevel4, NavigationCommand.PrevHeadingLevel4, NavigationCommand.HeadingLevel5,
            NavigationCommand.PrevHeadingLevel5, NavigationCommand.HeadingLevel6, NavigationCommand.PrevHeadingLevel6,
            NavigationCommand.EndOfContainer, NavigationCommand.StartOfContainer);
        Add(CommandCategory.Reading,
            NavigationCommand.NextLine, NavigationCommand.PrevLine, NavigationCommand.NextWord,
            NavigationCommand.PrevWord, NavigationCommand.NextChar, NavigationCommand.PrevChar,
            NavigationCommand.StartOfLine, NavigationCommand.EndOfLine, NavigationCommand.TopOfDocument,
            NavigationCommand.BottomOfDocument, NavigationCommand.NextParagraph, NavigationCommand.PrevParagraph,
            NavigationCommand.ReadCurrentLine, NavigationCommand.ReadCurrentWord, NavigationCommand.ReadCurrentChar,
            NavigationCommand.ReadSelection, NavigationCommand.ReadFormatting, NavigationCommand.SayAll,
            NavigationCommand.SayTitle, NavigationCommand.SayFocus, NavigationCommand.SayStatusBar,
            NavigationCommand.ReadWindow);
        Add(CommandCategory.BrowseMode,
            NavigationCommand.ActivateElement, NavigationCommand.ToggleMode, NavigationCommand.ExitFocusMode,
            NavigationCommand.ElementsList, NavigationCommand.PageSummary, NavigationCommand.SayLinkUrl,
            NavigationCommand.DismissOverlay, NavigationCommand.InteractWithMath, NavigationCommand.SummarizePage,
            NavigationCommand.MoveToMainContent, NavigationCommand.LabelElement, NavigationCommand.AskPage);
        Add(CommandCategory.Tables,
            NavigationCommand.TableNextColumn, NavigationCommand.TablePrevColumn, NavigationCommand.TableNextRow,
            NavigationCommand.TablePrevRow, NavigationCommand.TableFirstCell, NavigationCommand.TableLastCell,
            NavigationCommand.ReadTableRow, NavigationCommand.ReadTableColumn, NavigationCommand.SetColumnHeaders,
            NavigationCommand.SetRowHeaders);
        Add(CommandCategory.FindAndSelect,
            NavigationCommand.Find, NavigationCommand.FindNext, NavigationCommand.FindPrevious,
            NavigationCommand.SelectNextChar, NavigationCommand.SelectPrevChar, NavigationCommand.SelectNextWord,
            NavigationCommand.SelectPrevWord, NavigationCommand.SelectNextLine, NavigationCommand.SelectPrevLine,
            NavigationCommand.SelectToStartOfLine, NavigationCommand.SelectToEndOfLine, NavigationCommand.SelectToTop,
            NavigationCommand.SelectToBottom, NavigationCommand.SelectAll, NavigationCommand.CopySelection,
            NavigationCommand.MarkStart, NavigationCommand.SelectFromMark);
        Add(CommandCategory.ObjectNavigation,
            NavigationCommand.NavigatorParent, NavigationCommand.NavigatorFirstChild,
            NavigationCommand.NavigatorPrevious, NavigationCommand.NavigatorNext, NavigationCommand.ReportNavigator,
            NavigationCommand.NavigatorToFocus, NavigationCommand.FocusToNavigator, NavigationCommand.ActivateNavigator,
            NavigationCommand.CopyNavigatorText);
        Add(CommandCategory.Review,
            NavigationCommand.ReviewPrevLine, NavigationCommand.ReviewCurrentLine, NavigationCommand.ReviewNextLine,
            NavigationCommand.ReviewPrevWord, NavigationCommand.ReviewCurrentWord, NavigationCommand.ReviewNextWord,
            NavigationCommand.ReviewPrevChar, NavigationCommand.ReviewCurrentChar, NavigationCommand.ReviewNextChar,
            NavigationCommand.ReviewTop, NavigationCommand.ReviewBottom, NavigationCommand.NextReviewMode,
            NavigationCommand.PrevReviewMode, NavigationCommand.RecognizeNavigator, NavigationCommand.RecognizeWindow,
            NavigationCommand.ClickRecognizedWord, NavigationCommand.DescribeObject, NavigationCommand.AskAboutScreen);
        Add(CommandCategory.Mouse,
            NavigationCommand.ToggleMouseTracking, NavigationCommand.RouteMouseToNavigator,
            NavigationCommand.MouseLeftClick, NavigationCommand.MouseRightClick, NavigationCommand.ToggleLeftMouseLock);
        Add(CommandCategory.Magnifier,
            NavigationCommand.ToggleMagnifier, NavigationCommand.MagnifierZoomIn, NavigationCommand.MagnifierZoomOut,
            NavigationCommand.MagnifierNextFilter, NavigationCommand.MagnifierNextView, NavigationCommand.MagnifierMoveView,
            NavigationCommand.MagnifierViewLarger, NavigationCommand.MagnifierViewSmaller, NavigationCommand.MagnifierPanLeft,
            NavigationCommand.MagnifierPanRight, NavigationCommand.MagnifierPanUp, NavigationCommand.MagnifierPanDown);
        Add(CommandCategory.Speech,
            NavigationCommand.StopSpeech, NavigationCommand.SettingsRingPrevious, NavigationCommand.SettingsRingNext,
            NavigationCommand.SettingsRingIncrease, NavigationCommand.SettingsRingDecrease,
            NavigationCommand.SpeechHistoryPrevious, NavigationCommand.SpeechHistoryNext,
            NavigationCommand.ToggleSleepMode, NavigationCommand.ToggleSpeechViewer,
            NavigationCommand.TogglePauseSpeech);
        Add(CommandCategory.Braille,
            NavigationCommand.BraillePanLeft, NavigationCommand.BraillePanRight, NavigationCommand.CycleBrailleTether);
        Add(CommandCategory.System,
            NavigationCommand.SayTime, NavigationCommand.SayBattery, NavigationCommand.DeveloperInfo,
            NavigationCommand.CopySettingsToSecureScreens, NavigationCommand.OpenVoxMenu,
            NavigationCommand.OpenUserGuide, NavigationCommand.CheckForUpdates, NavigationCommand.CreatePortableCopy,
            NavigationCommand.RunTutorial, NavigationCommand.ExitVox, NavigationCommand.OpenCommandSearch,
            NavigationCommand.OpenInputGestures, NavigationCommand.OpenSettings, NavigationCommand.ToggleInputHelp,
            NavigationCommand.RunSetup, NavigationCommand.Quit, NavigationCommand.OpenProfiles, NavigationCommand.SetAiKey,
            NavigationCommand.ManageAddOns, NavigationCommand.GetAddOns, NavigationCommand.RestartWithoutAddOns,
            NavigationCommand.RemoteAccess, NavigationCommand.ToggleRemoteControl, NavigationCommand.DisconnectRemote,
            NavigationCommand.ImportSettings, NavigationCommand.ManageLabels, NavigationCommand.OpenSiteSettings,
            NavigationCommand.ReturnToScreenReader);
        // Application modules: only while their application is in the foreground
        Add(CommandCategory.Applications,
            NavigationCommand.ExcelReadFormula, NavigationCommand.ExcelReadComment, NavigationCommand.ExcelSetColumnTitles,
            NavigationCommand.ExcelSetRowTitles, NavigationCommand.ExcelChartSummary, NavigationCommand.WordLocation,
            NavigationCommand.WordNextHeading, NavigationCommand.WordPrevHeading, NavigationCommand.WordNextTable,
            NavigationCommand.WordPrevTable, NavigationCommand.WordNextList, NavigationCommand.WordPrevList,
            NavigationCommand.WordNextComment, NavigationCommand.WordPrevComment, NavigationCommand.WordNextRevision,
            NavigationCommand.WordPrevRevision, NavigationCommand.WordNextSpellingError, NavigationCommand.WordPrevSpellingError,
            NavigationCommand.WordTableCellLeft, NavigationCommand.WordTableCellRight, NavigationCommand.WordTableCellUp,
            NavigationCommand.WordTableCellDown, NavigationCommand.OutlookReadFrom, NavigationCommand.OutlookReadTo,
            NavigationCommand.OutlookReadCc, NavigationCommand.OutlookReadSubject, NavigationCommand.OutlookReadDate,
            NavigationCommand.OutlookReadAttachments, NavigationCommand.OutlookMeetingStatus, NavigationCommand.OutlookAcceptMeeting,
            NavigationCommand.OutlookTentativeMeeting, NavigationCommand.OutlookDeclineMeeting, NavigationCommand.OutlookToday,
            NavigationCommand.OutlookThisWeek, NavigationCommand.PowerPointNextShape, NavigationCommand.PowerPointPrevShape,
            NavigationCommand.PowerPointNotes, NavigationCommand.PowerPointReadSlide, NavigationCommand.TeamsReadMessage1,
            NavigationCommand.TeamsReadMessage2, NavigationCommand.TeamsReadMessage3, NavigationCommand.TeamsReadMessage4,
            NavigationCommand.TeamsReadMessage5, NavigationCommand.TeamsCallState, NavigationCommand.TeamsCallKey,
            NavigationCommand.ZoomCallState, NavigationCommand.ZoomCallKey);

        return commands;
    }
}
