namespace Vox.Core.Buffer;

/// <summary>
/// Reads the properties of a UIA AriaProperties string ("key=value;key2=value2", or with ':'
/// and ',') one at a time without allocating: the buffer reads several properties of every node
/// it builds. Segments without '=' or ':' are skipped; keys and values are trimmed.
/// </summary>
internal ref struct AriaPropertyReader
{
    private ReadOnlySpan<char> _rest;

    public AriaPropertyReader(string? ariaProps)
    {
        _rest = ariaProps;
    }

    /// <summary>The current property's name.</summary>
    public ReadOnlySpan<char> Key { get; private set; }

    /// <summary>The current property's value.</summary>
    public ReadOnlySpan<char> Value { get; private set; }

    /// <summary>Moves to the next property; false at the end.</summary>
    public bool MoveNext()
    {
        while (!_rest.IsEmpty)
        {
            int end = _rest.IndexOfAny(';', ',');
            var segment = end < 0 ? _rest : _rest[..end];
            _rest = end < 0 ? ReadOnlySpan<char>.Empty : _rest[(end + 1)..];

            int sep = segment.IndexOf('=');
            if (sep < 0) sep = segment.IndexOf(':');
            if (sep < 0) continue;

            Key = segment[..sep].Trim();
            Value = segment[(sep + 1)..].Trim();
            return true;
        }
        return false;
    }

    /// <summary>True when the current property is named <paramref name="key"/> (ignoring case).</summary>
    public readonly bool IsKey(string key) => Key.Equals(key, StringComparison.OrdinalIgnoreCase);
}
