using System.Security.Cryptography;
using System.Text;

namespace Vox.Core.Ai;

/// <summary>
/// Answers already given, by what was asked (the picture's hash, the question, the text sent, the
/// language and the describer), so the same image isn't described twice. Kept in memory only, for
/// this session; the least recently used answer goes first. Thread-safe.
/// </summary>
public sealed class DescriptionCache(int capacity = 64)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, string Text)>> _entries = new();
    private readonly LinkedList<(string Key, string Text)> _order = new();

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>The key for <paramref name="request"/> answered by <paramref name="describer"/>.</summary>
    public static string KeyFor(AiRequest request, string describer)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string? part)
        {
            var bytes = Encoding.UTF8.GetBytes(part ?? string.Empty);
            sha.AppendData(BitConverter.GetBytes(bytes.Length));
            sha.AppendData(bytes);
        }
        Add(request.Task.ToString());
        Add(request.Image?.Hash());
        Add(request.Question?.Trim());
        Add(request.Context);
        Add(request.Language);
        Add(describer);
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    public bool TryGet(string key, out string text)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                text = node.Value.Text;
                return true;
            }
        }
        text = string.Empty;
        return false;
    }

    public void Add(string key, string text)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var existing))
                _order.Remove(existing);
            var node = _order.AddFirst((key, text));
            _entries[key] = node;
            while (_entries.Count > Math.Max(1, capacity))
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _entries.Remove(last.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _order.Clear();
        }
    }
}
