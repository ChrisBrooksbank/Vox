using Vox.Core.Enterprise;

namespace Vox.Core.Tests.TestSupport;

/// <summary>An in-memory registry: values set per hive and key.</summary>
public sealed class FakeRegistry : IRegistryReader
{
    private readonly Dictionary<(RegistryRoot, string), Dictionary<string, object>> _keys = new();

    public FakeRegistry Set(RegistryRoot root, string key, string name, object value)
    {
        if (!_keys.TryGetValue((root, key.ToLowerInvariant()), out var values))
            _keys[(root, key.ToLowerInvariant())] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        values[name] = value;
        return this;
    }

    /// <summary>A computer policy (HKLM\Software\Policies\Vox).</summary>
    public FakeRegistry Machine(string name, object value) => Set(RegistryRoot.LocalMachine, PolicySettings.PolicyKey, name, value);

    /// <summary>A user policy (HKCU\Software\Policies\Vox).</summary>
    public FakeRegistry User(string name, object value) => Set(RegistryRoot.CurrentUser, PolicySettings.PolicyKey, name, value);

    public IReadOnlyDictionary<string, object> ReadValues(RegistryRoot root, string key) =>
        _keys.TryGetValue((root, key.ToLowerInvariant()), out var values)
            ? new Dictionary<string, object>(values, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>();
}
