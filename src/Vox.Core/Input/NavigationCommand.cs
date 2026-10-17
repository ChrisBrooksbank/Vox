namespace Vox.Core.Input;

public enum NavigationCommand
{
    NextHeading,
    PrevHeading,
    NextLink,
    PrevLink,
    NextLandmark,
    PrevLandmark,
    NextFormField,
    PrevFormField,
    NextTable,
    PrevTable,
    NextFocusable,
    PrevFocusable,
    HeadingLevel1,
    HeadingLevel2,
    HeadingLevel3,
    HeadingLevel4,
    HeadingLevel5,
    HeadingLevel6,
    NextLine,
    PrevLine,
    NextWord,
    PrevWord,
    NextChar,
    PrevChar,
    ActivateElement,
    ToggleMode,
    SayAll,
    StopSpeech,
    ElementsList,
    ReadCurrentLine,
    ReadCurrentWord,
    PrevHeadingLevel1,
    PrevHeadingLevel2,
    PrevHeadingLevel3,
    PrevHeadingLevel4,
    PrevHeadingLevel5,
    PrevHeadingLevel6,
    ExitFocusMode,
    StartOfLine,
    EndOfLine,
    TopOfDocument,
    BottomOfDocument,
    NextParagraph,
    PrevParagraph,
    Quit,
    RunSetup,
    ReadCurrentChar,
    ReadSelection,
    ReadFormatting,
    SayTitle,
    SayFocus,
    SayStatusBar,
    SayTime,
    SayBattery,
    ReadWindow,
    CopySettingsToSecureScreens,
    ToggleSpeechViewer,
    DeveloperInfo,
    NavigatorParent,
    NavigatorFirstChild,
    NavigatorPrevious,
    NavigatorNext,
    ReportNavigator,
    NavigatorToFocus,
    FocusToNavigator,
    ActivateNavigator,
    ReviewPrevLine,
    ReviewCurrentLine,
    ReviewNextLine,
    ReviewPrevWord,
    ReviewCurrentWord,
    ReviewNextWord,
    ReviewPrevChar,
    ReviewCurrentChar,
    ReviewNextChar,
    ReviewTop,
    ReviewBottom,
    NextReviewMode,
    PrevReviewMode,
    ToggleMouseTracking,
    RouteMouseToNavigator,
    MouseLeftClick,
    MouseRightClick,
    ToggleLeftMouseLock,
    CopyNavigatorText,
    SettingsRingPrevious,
    SettingsRingNext,
    SettingsRingIncrease,
    SettingsRingDecrease,
    SpeechHistoryPrevious,
    SpeechHistoryNext,
    ToggleSleepMode,
    NextButton,
    PrevButton,
    NextEdit,
    PrevEdit,
    NextComboBox,
    PrevComboBox,
    NextCheckBox,
    PrevCheckBox,
    NextRadioButton,
    PrevRadioButton,
    NextList,
    PrevList,
    NextListItem,
    PrevListItem,
    NextGraphic,
    PrevGraphic,
    NextBlockQuote,
    PrevBlockQuote,
    NextFrame,
    PrevFrame,
    NextSeparator,
    PrevSeparator,
    NextEmbeddedObject,
    PrevEmbeddedObject,
    NextVisitedLink,
    PrevVisitedLink,
    NextUnvisitedLink,
    PrevUnvisitedLink,
    NextNonLinkText,
    PrevNonLinkText,
    NextTextParagraph,
    PrevTextParagraph,
    NextUnvisitedHeading,
    PrevUnvisitedHeading,
    EndOfContainer,
    StartOfContainer,
    TableNextColumn,
    TablePrevColumn,
    TableNextRow,
    TablePrevRow,
    TableFirstCell,
    TableLastCell,
    ReadTableRow,
    ReadTableColumn,
    SetColumnHeaders,
    SetRowHeaders,
    Find,
    FindNext,
    FindPrevious,
    SelectNextChar,
    SelectPrevChar,
    SelectNextWord,
    SelectPrevWord,
    SelectNextLine,
    SelectPrevLine,
    SelectToStartOfLine,
    SelectToEndOfLine,
    SelectToTop,
    SelectToBottom,
    SelectAll,
    CopySelection,
    MarkStart,
    SelectFromMark,
    /// <summary>Says the page's title, language and how many headings, links and so on it has.</summary>
    PageSummary,
    /// <summary>Says the address of the link at the browse cursor.</summary>
    SayLinkUrl,
    /// <summary>Presses the Reject/Close button of the page's cookie banner or modal dialog.</summary>
    DismissOverlay,
    /// <summary>Starts (or ends) exploring the expression at the browse cursor with the arrow keys.</summary>
    InteractWithMath,
    /// <summary>Turns input help on or off: keys say what they do instead of doing it.</summary>
    ToggleInputHelp,
    /// <summary>Opens the settings dialog.</summary>
    OpenSettings,
    /// <summary>Opens the input gestures dialog: the keys of every command, to add and remove.</summary>
    OpenInputGestures,
    /// <summary>Opens command search: type part of a command's name to hear its keys.</summary>
    OpenCommandSearch,
    /// <summary>Opens the Vox menu (settings, input gestures, speech viewer, help, exit).</summary>
    OpenVoxMenu,
    /// <summary>Stops all speech until pressed again.</summary>
    TogglePauseSpeech,
    /// <summary>Opens the user guide in the browser.</summary>
    OpenUserGuide,
    /// <summary>Exits Vox at once (from the Vox menu).</summary>
    ExitVox,
    /// <summary>Runs the practice lessons (web pages, editing text, reviewing the screen).</summary>
    RunTutorial,
    /// <summary>Copies Vox, with the user's settings, to a folder (a USB stick) to run from there.</summary>
    CreatePortableCopy,
    /// <summary>Checks for a newer Vox now, and offers to install it.</summary>
    CheckForUpdates,
    /// <summary>Shows the previous part of the braille line (the previous line at its start).</summary>
    BraillePanLeft,
    /// <summary>Shows the next part of the braille line (the next line at its end).</summary>
    BraillePanRight,
    /// <summary>Switches what braille follows: automatically, the focus, or the review cursor.</summary>
    CycleBrailleTether,
    /// <summary>Opens the configuration profiles dialog.</summary>
    OpenProfiles,

    // Excel module
    /// <summary>Says the active cell's formula (Excel).</summary>
    ExcelReadFormula,
    /// <summary>Says the active cell's comment and note (Excel).</summary>
    ExcelReadComment,
    /// <summary>Makes the current row the sheet's column titles, or clears them (Excel).</summary>
    ExcelSetColumnTitles,
    /// <summary>Makes the current column the sheet's row titles, or clears them (Excel).</summary>
    ExcelSetRowTitles,
    /// <summary>Says the selected (or first) chart's title, type and series (Excel).</summary>
    ExcelChartSummary,

    // Word module
    /// <summary>Says the caret's page, section, line and column (Word).</summary>
    WordLocation,
    WordNextHeading,
    WordPrevHeading,
    WordNextTable,
    WordPrevTable,
    WordNextList,
    WordPrevList,
    WordNextComment,
    WordPrevComment,
    WordNextRevision,
    WordPrevRevision,
    WordNextSpellingError,
    WordPrevSpellingError,
    /// <summary>Moves to the table cell on the left (Word); and right, up, down below.</summary>
    WordTableCellLeft,
    WordTableCellRight,
    WordTableCellUp,
    WordTableCellDown,

    // Outlook module
    OutlookReadFrom,
    OutlookReadTo,
    OutlookReadCc,
    OutlookReadSubject,
    OutlookReadDate,
    OutlookReadAttachments,
    /// <summary>Says a meeting request's time, place and the user's answer so far (Outlook).</summary>
    OutlookMeetingStatus,
    OutlookAcceptMeeting,
    OutlookTentativeMeeting,
    OutlookDeclineMeeting,
    /// <summary>Says today's appointments (Outlook).</summary>
    OutlookToday,
    /// <summary>Says the appointments of the next 7 days (Outlook).</summary>
    OutlookThisWeek,

    // PowerPoint module
    PowerPointNextShape,
    PowerPointPrevShape,
    /// <summary>Says the current slide's speaker notes (PowerPoint).</summary>
    PowerPointNotes,
    /// <summary>Says the current slide's title and text (PowerPoint).</summary>
    PowerPointReadSlide,

    // Teams module
    /// <summary>Says the latest chat message (Teams); 2 to 5 below: the ones before it.</summary>
    TeamsReadMessage1,
    TeamsReadMessage2,
    TeamsReadMessage3,
    TeamsReadMessage4,
    TeamsReadMessage5,
    /// <summary>Says the call's state: in a call, microphone, camera, hand (Teams).</summary>
    TeamsCallState,
    /// <summary>Bound to Teams' own mute, camera and hand keys, which still reach it: says what changed.</summary>
    TeamsCallKey,

    // Zoom module
    ZoomCallState,
    ZoomCallKey,

    // Text recognition
    /// <summary>Recognises the text of the navigator object (while browsing: the graphic at the cursor, else the page) and reviews it.</summary>
    RecognizeNavigator,
    /// <summary>Recognises the text of the foreground window and reviews it.</summary>
    RecognizeWindow,
    /// <summary>Clicks the recognised word at the review cursor.</summary>
    ClickRecognizedWord,

    // AI
    /// <summary>Describes the navigator object (while browsing: the graphic at the cursor, else the page) and reviews the description.</summary>
    DescribeObject,
    /// <summary>Asks a question about the window in front and reviews the answer.</summary>
    AskAboutScreen,
    /// <summary>Summarises the page being browsed and reviews the summary.</summary>
    SummarizePage,
    /// <summary>Moves the browse cursor to where the page's main content starts (pressed again: the next suggestion).</summary>
    MoveToMainContent,
    /// <summary>Asks for the cloud AI provider's API key (Vox menu).</summary>
    SetAiKey,

    // Add-ons
    /// <summary>Opens the add-ons dialog: enable, disable, remove and install add-ons (Vox menu).</summary>
    ManageAddOns,
    /// <summary>Opens the add-on store: browse, search, install and update add-ons from Vox's signed catalogue (Vox menu).</summary>
    GetAddOns,
    /// <summary>Restarts Vox with add-ons turned off (safe mode), or with them on again when in safe mode (Vox menu).</summary>
    RestartWithoutAddOns,

    // Remote access
    /// <summary>Opens the remote access dialog: let this computer be controlled, or control another one (Vox menu).</summary>
    RemoteAccess,
    /// <summary>While connected to another computer: switches between sending keys to it and using this one.</summary>
    ToggleRemoteControl,
    /// <summary>Ends the remote session.</summary>
    DisconnectRemote,

    // Switching from other screen readers
    /// <summary>Brings over NVDA's or JAWS's settings, keys and dictionaries after showing what will change.</summary>
    ImportSettings,

    // Custom labels
    /// <summary>Gives the link, control or graphic at the browse cursor (or the focused one) a label of your own, kept for its site.</summary>
    LabelElement,
    /// <summary>Opens the custom labels dialog: change or delete labels.</summary>
    ManageLabels,

    // Per-site settings
    /// <summary>Opens the settings of the web site being browsed (start mode, verbosity, screen layout, cookie banners).</summary>
    OpenSiteSettings,

    // AI that fixes pages
    /// <summary>Asks a question about the page being browsed, answered from its text; offers to move to what the answer names.</summary>
    AskPage,

    // Safe trial
    /// <summary>Trial mode: quits Vox and starts the screen reader it stepped in for (NVDA, or Narrator) again.</summary>
    ReturnToScreenReader,

    // Magnifier
    /// <summary>Turns the magnifier on or off.</summary>
    ToggleMagnifier,
    /// <summary>Magnifies more (turns the magnifier on when it is off).</summary>
    MagnifierZoomIn,
    /// <summary>Magnifies less.</summary>
    MagnifierZoomOut,
    /// <summary>Switches to the next colour filter: inverted, greyscale, inverted greyscale, yellow on black, none.</summary>
    MagnifierNextFilter,
    /// <summary>Switches to the next view: full screen, docked, lens.</summary>
    MagnifierNextView,
    /// <summary>Moves the docked view to the next edge of the screen.</summary>
    MagnifierMoveView,
    /// <summary>Makes the docked view or the lens larger.</summary>
    MagnifierViewLarger,
    /// <summary>Makes the docked view or the lens smaller.</summary>
    MagnifierViewSmaller,
    /// <summary>Moves the magnified view left.</summary>
    MagnifierPanLeft,
    /// <summary>Moves the magnified view right.</summary>
    MagnifierPanRight,
    /// <summary>Moves the magnified view up.</summary>
    MagnifierPanUp,
    /// <summary>Moves the magnified view down.</summary>
    MagnifierPanDown,

    // Speech on demand
    /// <summary>Turns speech on demand on or off: only the answers to reporting commands are spoken.</summary>
    ToggleSpeechOnDemand,

    // Screen curtain
    /// <summary>Turns the screen curtain on or off: the display goes black while Vox keeps reading.</summary>
    ToggleScreenCurtain
}
