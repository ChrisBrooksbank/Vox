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
    Speech,
    System,
}

/// <summary>What a command is called and does, for people (help mode, the gestures dialog, the guide).</summary>
public sealed record CommandInfo(NavigationCommand Command, string Name, string Description, CommandCategory Category);

/// <summary>
/// The one source of every command's name, description and category, used by input help,
/// the input gestures dialog and the generated keyboard reference.
/// </summary>
public static class CommandCatalog
{
    private static readonly Dictionary<NavigationCommand, CommandInfo> Commands = Build();

    /// <summary>Every command, in category order then the order listed here.</summary>
    public static IReadOnlyList<CommandInfo> All { get; } = Commands.Values.OrderBy(c => c.Category).ToList();

    public static CommandInfo Describe(NavigationCommand command) =>
        Commands.TryGetValue(command, out var info)
            ? info
            : new CommandInfo(command, command.ToString(), string.Empty, CommandCategory.System);

    /// <summary>True when <paramref name="command"/> has an entry (a test checks every command has).</summary>
    public static bool Has(NavigationCommand command) => Commands.ContainsKey(command);

    /// <summary>A category as people read it.</summary>
    public static string CategoryName(CommandCategory category) => category switch
    {
        CommandCategory.QuickNavigation => "Quick navigation",
        CommandCategory.Reading => "Reading",
        CommandCategory.BrowseMode => "Browse mode",
        CommandCategory.Tables => "Tables",
        CommandCategory.FindAndSelect => "Find, select and copy",
        CommandCategory.ObjectNavigation => "Object navigation",
        CommandCategory.Review => "Review cursor",
        CommandCategory.Mouse => "Mouse",
        CommandCategory.Speech => "Speech",
        _ => "System",
    };

    private static Dictionary<NavigationCommand, CommandInfo> Build()
    {
        var commands = new Dictionary<NavigationCommand, CommandInfo>();
        void Add(NavigationCommand command, string name, string description, CommandCategory category) =>
            commands.Add(command, new CommandInfo(command, name, description, category));

        // Quick navigation: next and previous of each kind of element
        void Pair(NavigationCommand next, NavigationCommand previous, string singular, string what)
        {
            Add(next, $"Next {singular}", $"Moves to the next {what}.", CommandCategory.QuickNavigation);
            Add(previous, $"Previous {singular}", $"Moves to the previous {what}.", CommandCategory.QuickNavigation);
        }
        Pair(NavigationCommand.NextHeading, NavigationCommand.PrevHeading, "heading", "heading");
        Pair(NavigationCommand.NextLink, NavigationCommand.PrevLink, "link", "link");
        Pair(NavigationCommand.NextLandmark, NavigationCommand.PrevLandmark, "landmark", "landmark or region (navigation, main, search...)");
        Pair(NavigationCommand.NextFormField, NavigationCommand.PrevFormField, "form field", "form field of any kind");
        Pair(NavigationCommand.NextTable, NavigationCommand.PrevTable, "table", "table");
        Pair(NavigationCommand.NextFocusable, NavigationCommand.PrevFocusable, "focusable element", "element that can take focus");
        Pair(NavigationCommand.NextButton, NavigationCommand.PrevButton, "button", "button");
        Pair(NavigationCommand.NextEdit, NavigationCommand.PrevEdit, "edit field", "text box");
        Pair(NavigationCommand.NextComboBox, NavigationCommand.PrevComboBox, "combo box", "combo box");
        Pair(NavigationCommand.NextCheckBox, NavigationCommand.PrevCheckBox, "check box", "check box");
        Pair(NavigationCommand.NextRadioButton, NavigationCommand.PrevRadioButton, "radio button", "radio button");
        Pair(NavigationCommand.NextList, NavigationCommand.PrevList, "list", "list");
        Pair(NavigationCommand.NextListItem, NavigationCommand.PrevListItem, "list item", "list item");
        Pair(NavigationCommand.NextGraphic, NavigationCommand.PrevGraphic, "graphic", "image or graphic");
        Pair(NavigationCommand.NextBlockQuote, NavigationCommand.PrevBlockQuote, "block quote", "block quote");
        Pair(NavigationCommand.NextFrame, NavigationCommand.PrevFrame, "frame", "frame or embedded page");
        Pair(NavigationCommand.NextSeparator, NavigationCommand.PrevSeparator, "separator", "separator");
        Pair(NavigationCommand.NextEmbeddedObject, NavigationCommand.PrevEmbeddedObject, "embedded object", "embedded object (video, audio, plug-in)");
        Pair(NavigationCommand.NextVisitedLink, NavigationCommand.PrevVisitedLink, "visited link", "link you have visited");
        Pair(NavigationCommand.NextUnvisitedLink, NavigationCommand.PrevUnvisitedLink, "unvisited link", "link you haven't visited");
        Pair(NavigationCommand.NextNonLinkText, NavigationCommand.PrevNonLinkText, "text after links", "text after a block of links");
        Pair(NavigationCommand.NextTextParagraph, NavigationCommand.PrevTextParagraph, "text paragraph", "paragraph of plain text");
        Pair(NavigationCommand.NextUnvisitedHeading, NavigationCommand.PrevUnvisitedHeading, "unread heading", "heading you haven't been on in this page");
        for (int level = 1; level <= 6; level++)
        {
            Add(NavigationCommand.HeadingLevel1 + level - 1, $"Next heading level {level}", $"Moves to the next heading at level {level}.", CommandCategory.QuickNavigation);
            Add(NavigationCommand.PrevHeadingLevel1 + level - 1, $"Previous heading level {level}", $"Moves to the previous heading at level {level}.", CommandCategory.QuickNavigation);
        }
        Add(NavigationCommand.EndOfContainer, "End of container", "Moves to the end of the list, table, landmark, block quote or frame the cursor is in.", CommandCategory.QuickNavigation);
        Add(NavigationCommand.StartOfContainer, "Start of container", "Moves to the start of the list, table, landmark, block quote or frame the cursor is in.", CommandCategory.QuickNavigation);

        // Reading
        Add(NavigationCommand.NextLine, "Next line", "Moves to the next line and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.PrevLine, "Previous line", "Moves to the previous line and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.NextWord, "Next word", "Moves to the next word and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.PrevWord, "Previous word", "Moves to the previous word and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.NextChar, "Next character", "Moves to the next character and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.PrevChar, "Previous character", "Moves to the previous character and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.StartOfLine, "Start of line", "Moves to the start of the line.", CommandCategory.Reading);
        Add(NavigationCommand.EndOfLine, "End of line", "Moves to the end of the line.", CommandCategory.Reading);
        Add(NavigationCommand.TopOfDocument, "Top of page", "Moves to the top of the page.", CommandCategory.Reading);
        Add(NavigationCommand.BottomOfDocument, "Bottom of page", "Moves to the bottom of the page.", CommandCategory.Reading);
        Add(NavigationCommand.NextParagraph, "Next paragraph", "Moves to the next paragraph and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.PrevParagraph, "Previous paragraph", "Moves to the previous paragraph and reads it.", CommandCategory.Reading);
        Add(NavigationCommand.ReadCurrentLine, "Read current line", "Reads the current line; pressed twice spells it, three times spells it phonetically.", CommandCategory.Reading);
        Add(NavigationCommand.ReadCurrentWord, "Read current word", "Reads the current word; pressed twice spells it, three times spells it phonetically.", CommandCategory.Reading);
        Add(NavigationCommand.ReadCurrentChar, "Read current character", "Reads the current character; pressed twice says its phonetic name.", CommandCategory.Reading);
        Add(NavigationCommand.ReadSelection, "Read selection", "Reads the selected text.", CommandCategory.Reading);
        Add(NavigationCommand.ReadFormatting, "Read formatting", "Says the font and formatting at the caret.", CommandCategory.Reading);
        Add(NavigationCommand.SayAll, "Say all", "Reads from the cursor to the end; any key stops it.", CommandCategory.Reading);
        Add(NavigationCommand.SayTitle, "Say window title", "Says the title of the window in front.", CommandCategory.Reading);
        Add(NavigationCommand.SayFocus, "Say focus", "Says the element that has focus.", CommandCategory.Reading);
        Add(NavigationCommand.SayStatusBar, "Say status bar", "Reads the status bar of the window in front.", CommandCategory.Reading);
        Add(NavigationCommand.ReadWindow, "Read window", "Reads all the controls in the window in front, or the dialog's text.", CommandCategory.Reading);

        // Browse mode
        Add(NavigationCommand.ActivateElement, "Activate", "Presses the link or button at the cursor, or moves into the form field there.", CommandCategory.BrowseMode);
        Add(NavigationCommand.ToggleMode, "Toggle browse and focus mode", "Switches between browse mode (single-letter navigation) and focus mode (keys go to the page).", CommandCategory.BrowseMode);
        Add(NavigationCommand.ExitFocusMode, "Leave focus mode", "Goes back to browse mode from a form field.", CommandCategory.BrowseMode);
        Add(NavigationCommand.ElementsList, "Elements list", "Lists the page's headings, links, landmarks, form fields, buttons and tables to choose from.", CommandCategory.BrowseMode);
        Add(NavigationCommand.PageSummary, "Page summary", "Says the page's title, language and how many headings, links, landmarks, form fields and tables it has.", CommandCategory.BrowseMode);
        Add(NavigationCommand.SayLinkUrl, "Say link address", "Says the address of the link at the cursor.", CommandCategory.BrowseMode);
        Add(NavigationCommand.DismissOverlay, "Dismiss cookie banner or dialog", "Presses the Reject or Close button of the page's cookie banner or modal dialog.", CommandCategory.BrowseMode);
        Add(NavigationCommand.InteractWithMath, "Explore math", "Starts or ends exploring the expression at the cursor with the arrow keys (needs MathCAT).", CommandCategory.BrowseMode);

        // Tables
        Add(NavigationCommand.TableNextColumn, "Next column", "Moves to the next cell in the row.", CommandCategory.Tables);
        Add(NavigationCommand.TablePrevColumn, "Previous column", "Moves to the previous cell in the row.", CommandCategory.Tables);
        Add(NavigationCommand.TableNextRow, "Next row", "Moves to the next cell in the column.", CommandCategory.Tables);
        Add(NavigationCommand.TablePrevRow, "Previous row", "Moves to the previous cell in the column.", CommandCategory.Tables);
        Add(NavigationCommand.TableFirstCell, "First cell", "Moves to the first cell of the table.", CommandCategory.Tables);
        Add(NavigationCommand.TableLastCell, "Last cell", "Moves to the last cell of the table.", CommandCategory.Tables);
        Add(NavigationCommand.ReadTableRow, "Read row", "Reads the whole row the cursor is in.", CommandCategory.Tables);
        Add(NavigationCommand.ReadTableColumn, "Read column", "Reads the whole column the cursor is in.", CommandCategory.Tables);
        Add(NavigationCommand.SetColumnHeaders, "Set column headers", "Makes the current row the table's column headers (remembered for the page), or clears them when it already is.", CommandCategory.Tables);
        Add(NavigationCommand.SetRowHeaders, "Set row headers", "Makes the current column the table's row headers (remembered for the page), or clears them when it already is.", CommandCategory.Tables);

        // Find, select and copy
        Add(NavigationCommand.Find, "Find", "Opens the find box to search the page.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.FindNext, "Find next", "Finds the next match of the last search.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.FindPrevious, "Find previous", "Finds the previous match of the last search.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectNextChar, "Select next character", "Extends the selection by a character to the right.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectPrevChar, "Select previous character", "Extends the selection by a character to the left.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectNextWord, "Select next word", "Extends the selection to the end of the word.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectPrevWord, "Select previous word", "Extends the selection to the start of the word.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectNextLine, "Select next line", "Extends the selection by a line down.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectPrevLine, "Select previous line", "Extends the selection by a line up.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectToStartOfLine, "Select to start of line", "Extends the selection to the start of the line.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectToEndOfLine, "Select to end of line", "Extends the selection to the end of the line.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectToTop, "Select to top", "Extends the selection to the top of the page.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectToBottom, "Select to bottom", "Extends the selection to the bottom of the page.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectAll, "Select all", "Selects the whole page.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.CopySelection, "Copy", "Copies the selected text to the clipboard.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.MarkStart, "Mark start", "Marks the cursor position as the start of a selection.", CommandCategory.FindAndSelect);
        Add(NavigationCommand.SelectFromMark, "Select from mark", "Selects from the mark to the cursor; pressed twice copies it.", CommandCategory.FindAndSelect);

        // Object navigation
        Add(NavigationCommand.NavigatorParent, "Navigator to parent", "Moves the navigator object to the object containing it.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.NavigatorFirstChild, "Navigator to first child", "Moves the navigator object to the first object inside it.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.NavigatorPrevious, "Navigator to previous", "Moves the navigator object to the previous object.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.NavigatorNext, "Navigator to next", "Moves the navigator object to the next object.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.ReportNavigator, "Report navigator object", "Says the navigator object.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.NavigatorToFocus, "Navigator to focus", "Moves the navigator object to the focused object.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.FocusToNavigator, "Focus navigator object", "Moves focus to the navigator object.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.ActivateNavigator, "Activate navigator object", "Presses or toggles the navigator object.", CommandCategory.ObjectNavigation);
        Add(NavigationCommand.CopyNavigatorText, "Copy navigator text", "Copies the navigator object's text to the clipboard.", CommandCategory.ObjectNavigation);

        // Review cursor
        Add(NavigationCommand.ReviewPrevLine, "Review previous line", "Moves the review cursor to the previous line and reads it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewCurrentLine, "Review current line", "Reads the review cursor's line; pressed twice spells it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewNextLine, "Review next line", "Moves the review cursor to the next line and reads it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewPrevWord, "Review previous word", "Moves the review cursor to the previous word and reads it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewCurrentWord, "Review current word", "Reads the review cursor's word; pressed twice spells it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewNextWord, "Review next word", "Moves the review cursor to the next word and reads it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewPrevChar, "Review previous character", "Moves the review cursor to the previous character and reads it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewCurrentChar, "Review current character", "Reads the review cursor's character; pressed twice says its phonetic name.", CommandCategory.Review);
        Add(NavigationCommand.ReviewNextChar, "Review next character", "Moves the review cursor to the next character and reads it.", CommandCategory.Review);
        Add(NavigationCommand.ReviewTop, "Review top", "Moves the review cursor to the top.", CommandCategory.Review);
        Add(NavigationCommand.ReviewBottom, "Review bottom", "Moves the review cursor to the bottom.", CommandCategory.Review);
        Add(NavigationCommand.NextReviewMode, "Next review mode", "Switches review to the next mode (object, document, screen).", CommandCategory.Review);
        Add(NavigationCommand.PrevReviewMode, "Previous review mode", "Switches review to the previous mode (object, document, screen).", CommandCategory.Review);

        // Mouse
        Add(NavigationCommand.ToggleMouseTracking, "Toggle mouse tracking", "Turns reading what is under the mouse pointer on or off.", CommandCategory.Mouse);
        Add(NavigationCommand.RouteMouseToNavigator, "Move mouse to navigator", "Moves the mouse pointer to the navigator object.", CommandCategory.Mouse);
        Add(NavigationCommand.MouseLeftClick, "Left click", "Clicks the left mouse button where the pointer is.", CommandCategory.Mouse);
        Add(NavigationCommand.MouseRightClick, "Right click", "Clicks the right mouse button where the pointer is.", CommandCategory.Mouse);
        Add(NavigationCommand.ToggleLeftMouseLock, "Lock left mouse button", "Holds the left mouse button down, or lets it go (for dragging).", CommandCategory.Mouse);

        // Speech
        Add(NavigationCommand.StopSpeech, "Stop speech", "Stops speaking.", CommandCategory.Speech);
        Add(NavigationCommand.SettingsRingPrevious, "Previous voice setting", "Picks the previous setting in the voice settings ring (voice, rate, pitch, volume, punctuation, synthesizer).", CommandCategory.Speech);
        Add(NavigationCommand.SettingsRingNext, "Next voice setting", "Picks the next setting in the voice settings ring.", CommandCategory.Speech);
        Add(NavigationCommand.SettingsRingIncrease, "Increase voice setting", "Raises the voice setting picked in the ring.", CommandCategory.Speech);
        Add(NavigationCommand.SettingsRingDecrease, "Decrease voice setting", "Lowers the voice setting picked in the ring.", CommandCategory.Speech);
        Add(NavigationCommand.SpeechHistoryPrevious, "Previous spoken text", "Says the previous thing Vox said; pressed twice copies it.", CommandCategory.Speech);
        Add(NavigationCommand.SpeechHistoryNext, "Next spoken text", "Says the next thing Vox said; pressed twice copies it.", CommandCategory.Speech);
        Add(NavigationCommand.ToggleSleepMode, "Sleep mode", "Turns sleep mode on or off for this application: Vox stops speaking and passes every key to it.", CommandCategory.Speech);
        Add(NavigationCommand.ToggleSpeechViewer, "Speech viewer", "Shows or hides a window with everything Vox says.", CommandCategory.Speech);

        // System
        Add(NavigationCommand.SayTime, "Say time", "Says the time; pressed twice says the date.", CommandCategory.System);
        Add(NavigationCommand.SayBattery, "Say battery", "Says the battery level and whether it is charging.", CommandCategory.System);
        Add(NavigationCommand.DeveloperInfo, "Developer information", "Says the focused element's role, class, framework and process, and copies them to the clipboard.", CommandCategory.System);
        Add(NavigationCommand.CopySettingsToSecureScreens, "Use settings on sign-in screens", "Copies your settings to the sign-in, lock and UAC screens.", CommandCategory.System);
        Add(NavigationCommand.RunSetup, "Run setup", "Runs the welcome and setup wizard again.", CommandCategory.System);
        Add(NavigationCommand.Quit, "Quit Vox", "Exits Vox (pressed twice to confirm).", CommandCategory.System);

        return commands;
    }
}
