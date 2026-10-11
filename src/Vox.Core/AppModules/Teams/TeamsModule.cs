using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Vox.Core.Accessibility;
using Vox.Core.AppModules.Meetings;
using Vox.Core.Buffer;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.AppModules.Teams;

/// <summary>Which of Teams' new-message announcements are said.</summary>
public enum TeamsMessageAnnouncements
{
    All,
    MentionsOnly,
    Off,
}

/// <summary>Recognizes Teams' new-message announcements, and finds a chat's messages in its page.</summary>
public static partial class TeamsMessages
{
    [GeneratedRegex(@"^(new message|new reply)\b|\b(sent a message|sent you a message|replied|posted|mentioned you)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MessagePattern();

    /// <summary>Whether an announcement is about a new message.</summary>
    public static bool IsMessage(string? text) => !string.IsNullOrWhiteSpace(text) && MessagePattern().IsMatch(text);

    /// <summary>Whether a message announcement mentions the user.</summary>
    public static bool IsMention(string text) =>
        text.Contains("mentioned you", StringComparison.OrdinalIgnoreCase) || text.Contains('@');

    /// <summary>Whether an announcement is said with <paramref name="setting"/>; anything but a new message always is.</summary>
    public static bool ShouldAnnounce(string? text, TeamsMessageAnnouncements setting) =>
        !IsMessage(text) || setting switch
        {
            TeamsMessageAnnouncements.All => true,
            TeamsMessageAnnouncements.MentionsOnly => IsMention(text!),
            _ => false,
        };

    private const int MaxMessageLength = 500;

    /// <summary>
    /// The chat's messages, oldest first: the items of the longest list named like a chat ("Chat
    /// content", "Messages", "Conversation"), else of the longest list.
    /// </summary>
    public static IReadOnlyList<string> Messages(VBufferDocument? document)
    {
        if (document is null || document.Lists.Count == 0)
            return [];
        var named = document.Lists.Where(l =>
            l.Name.Contains("chat", StringComparison.OrdinalIgnoreCase)
            || l.Name.Contains("message", StringComparison.OrdinalIgnoreCase)
            || l.Name.Contains("conversation", StringComparison.OrdinalIgnoreCase)).ToList();
        // The chat list beside the conversation is named like it too: the longer list is the conversation
        var list = (named.Count > 0 ? named : document.Lists).MaxBy(l => ItemsOf(document, l).Count())!;
        return ItemsOf(document, list).Select(item => Text(document, item)).Where(t => t.Length > 0).ToList();
    }

    private static IEnumerable<VBufferNode> ItemsOf(VBufferDocument document, VBufferNode list) =>
        document.ListItems.Where(item => NearestList(item) == list);

    private static VBufferNode? NearestList(VBufferNode item)
    {
        for (var node = item.Parent; node is not null; node = node.Parent)
        {
            if (PageElements.IsList(node) || string.Equals(node.AriaRole, "feed", StringComparison.OrdinalIgnoreCase))
                return node;
        }
        return null;
    }

    /// <summary>The text of a node's subtree (only nodes that emit text have a range of their own).</summary>
    private static string Text(VBufferDocument document, VBufferNode node)
    {
        int start = int.MaxValue, end = -1;
        var pending = new Stack<VBufferNode>([node]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.TextRange.End > current.TextRange.Start)
            {
                start = Math.Min(start, current.TextRange.Start);
                end = Math.Max(end, current.TextRange.End);
            }
            foreach (var child in current.Children)
                pending.Push(child);
        }
        if (end <= start || end > document.FlatText.Length)
            return node.Name.Trim();
        var text = string.Join(' ', document.FlatText[start..end].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return text.Length > MaxMessageLength ? text[..MaxMessageLength] : text;
    }
}

/// <summary>
/// Microsoft Teams (new Teams, a WebView2 application read in browse mode, where I and Shift+I move
/// between messages). Adds reading the chat's last messages, the call's state (and what Teams'
/// own mute, camera and hand keys changed), and filters Teams' new-message announcements.
/// </summary>
public sealed class TeamsModule : MeetingAppModule
{
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Func<VBufferDocument?> _document;

    public TeamsModule(IWindowButtonSource buttons, SpeechQueue speech, IEventSink events, RecentSpeech recentSpeech,
        IOptionsMonitor<VoxSettings> settings, Func<VBufferDocument?> document, TimeSpan? stateCheckDelay = null)
        : base(buttons, speech, events, recentSpeech, stateCheckDelay)
    {
        _settings = settings;
        _document = document;
    }

    public override string Id => "teams";
    public override string Name => "Microsoft Teams";
    public override IReadOnlyCollection<string> ProcessNames { get; } = ["ms-teams", "Teams"];

    protected override NavigationCommand StateCommand => NavigationCommand.TeamsCallState;
    protected override NavigationCommand CallKeyCommand => NavigationCommand.TeamsCallKey;

    private static readonly NavigationCommand[] ReadMessageCommands =
    [
        NavigationCommand.TeamsReadMessage1, NavigationCommand.TeamsReadMessage2, NavigationCommand.TeamsReadMessage3,
        NavigationCommand.TeamsReadMessage4, NavigationCommand.TeamsReadMessage5,
    ];

    public override bool TryHandle(NavigationCommand command)
    {
        int index = Array.IndexOf(ReadMessageCommands, command);
        if (index < 0)
            return base.TryHandle(command);
        // 1 is the latest message
        var messages = TeamsMessages.Messages(_document());
        var text = messages.Count == 0 ? "No messages here"
            : index < messages.Count ? messages[messages.Count - 1 - index]
            : $"Only {messages.Count} {(messages.Count == 1 ? "message" : "messages")}";
        Speech.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
        return true;
    }

    public override bool ShouldProcess(ScreenReaderEvent evt)
    {
        var setting = _settings.CurrentValue.TeamsMessageAnnouncements;
        var text = evt switch
        {
            LiveRegionChangedEvent live => live.Text,
            NotificationEvent notification => notification.NotificationText,
            _ => null,
        };
        if (text is not null && !TeamsMessages.ShouldAnnounce(text, setting))
            return false;
        return base.ShouldProcess(evt);
    }
}
