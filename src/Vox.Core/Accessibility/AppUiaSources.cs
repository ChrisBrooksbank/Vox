using Interop.UIAutomationClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vox.Core.Accessibility;

/// <summary>The names of the buttons in the foreground window (Teams' and Zoom's call controls).</summary>
public interface IWindowButtonSource
{
    /// <summary>The button names, or null when the window can't be read.</summary>
    Task<IReadOnlyList<string>?> ButtonNamesAsync();
}

/// <summary>One cell of a details-view row: its column's header and its value.</summary>
public sealed record DetailsCell(string Header, string Value);

/// <summary>The cells of the focused list item in a details view (File Explorer).</summary>
public interface IDetailsRowSource
{
    /// <summary>The focused item's cells, or null when it isn't a list item with cells.</summary>
    Task<IReadOnlyList<DetailsCell>?> FocusedRowAsync();
}

/// <summary><see cref="IWindowButtonSource"/> and <see cref="IDetailsRowSource"/> through UIA, on the UIA thread.</summary>
public sealed class UIAAppSources : IWindowButtonSource, IDetailsRowSource
{
    private const int NamePropertyId = 30005;
    private const int ControlTypePropertyId = 30003;
    private const int ButtonControlTypeId = 50000;
    private const int ListItemControlTypeId = 50007;
    private const int TableItemPatternId = 10013;
    private const int ValuePatternId = 10002;
    private const int MaxCells = 32;

    private readonly UIAThread _uiaThread;
    private readonly UIAProvider _uiaProvider;
    private readonly IForegroundWindow _foreground;
    private readonly ILogger _logger;

    public UIAAppSources(UIAThread uiaThread, UIAProvider uiaProvider, IForegroundWindow foreground, ILogger<UIAAppSources>? logger = null)
    {
        _uiaThread = uiaThread;
        _uiaProvider = uiaProvider;
        _foreground = foreground;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<IReadOnlyList<string>?> ButtonNamesAsync()
    {
        if (_foreground.Get() is not { Handle: var hwnd } || hwnd == IntPtr.Zero)
            return null;
        try
        {
            return await _uiaThread.RunAsync<IReadOnlyList<string>>(() =>
            {
                var automation = _uiaProvider.Automation;
                var window = automation.ElementFromHandle(hwnd);
                if (window is null)
                    return [];
                var cache = automation.CreateCacheRequest();
                cache.AddProperty(NamePropertyId);
                var buttons = _uiaProvider.WithDocumentCaptureTimeout(() => window.FindAllBuildCache(
                    TreeScope.TreeScope_Descendants,
                    automation.CreatePropertyCondition(ControlTypePropertyId, ButtonControlTypeId),
                    cache));
                var names = new List<string>();
                for (int i = 0; i < (buttons?.Length ?? 0); i++)
                {
                    var name = buttons!.GetElement(i).CachedName;
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
                return names;
            }, UIAThread.DocumentTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the window's buttons");
            return null;
        }
    }

    public async Task<IReadOnlyList<DetailsCell>?> FocusedRowAsync()
    {
        try
        {
            return await _uiaThread.RunAsync<IReadOnlyList<DetailsCell>?>(() =>
            {
                var automation = _uiaProvider.Automation;
                var focused = automation.GetFocusedElement();
                if (focused is null || focused.CurrentControlType != ListItemControlTypeId)
                    return null;
                var walker = automation.ControlViewWalker;
                var cells = new List<DetailsCell>();
                for (var child = walker.GetFirstChildElement(focused); child is not null && cells.Count < MaxCells;
                     child = walker.GetNextSiblingElement(child))
                {
                    string header = string.Empty;
                    if (child.GetCurrentPattern(TableItemPatternId) is IUIAutomationTableItemPattern tableItem)
                    {
                        var headers = tableItem.GetCurrentColumnHeaderItems();
                        if (headers is { Length: > 0 })
                            header = headers.GetElement(0).CurrentName ?? string.Empty;
                    }
                    string value = child.CurrentName ?? string.Empty;
                    if (value.Length == 0 && child.GetCurrentPattern(ValuePatternId) is IUIAutomationValuePattern valuePattern)
                        value = valuePattern.CurrentValue ?? string.Empty;
                    cells.Add(new DetailsCell(header, value));
                }
                return cells.Count == 0 ? null : cells;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the focused row");
            return null;
        }
    }
}
