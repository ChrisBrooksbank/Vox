using Vox.Core.Configuration;

namespace Vox.Core.Remote;

/// <summary>Presses and releases keys on this computer as if typed (SendInput, through Vox's own hook).</summary>
public interface IRemoteKeyInjector
{
    void SendRaw(int vkCode, int scanCode, bool down, bool extended);
}

/// <summary>
/// Plays the keys the controlling computer sends on this one. Its screen reader key is pressed
/// as this computer's own (Insert or Caps Lock, whatever the controlling side uses), so Vox
/// commands work as they do here. Every key it pressed is released when control stops or the
/// session ends, so nothing is left held down.
/// </summary>
public sealed class RemoteKeyPlayer(IRemoteKeyInjector injector, Func<ModifierKey> screenReaderKey)
{
    private readonly HashSet<int> _down = [];
    private readonly object _lock = new();

    /// <summary>Plays one key message; keys that aren't keys are ignored.</summary>
    public void Play(RemoteMessage key)
    {
        int vk = key.VoxKey ? (screenReaderKey() == ModifierKey.CapsLock ? 0x14 : 0x2D) : key.Vk;
        if (vk is <= 0 or >= 0xFF)
            return;
        lock (_lock)
        {
            if (key.Down)
                _down.Add(vk);
            else if (!_down.Remove(vk))
                return; // a key-up of a key this side never pressed (it was down when control started)
        }
        var extended = key.VoxKey ? vk == 0x2D : !key.Keypad && IsExtended(vk, key.Extended);
        injector.SendRaw(vk, key.VoxKey ? 0 : key.Scan & 0xFFFF, key.Down, extended);
    }

    /// <summary>Releases every key still held down.</summary>
    public void ReleaseAll()
    {
        int[] held;
        lock (_lock)
        {
            held = [.. _down];
            _down.Clear();
        }
        foreach (var vk in held)
            injector.SendRaw(vk, 0, down: false, IsExtended(vk, false));
    }

    /// <summary>Keys held down now (tests).</summary>
    public int HeldCount
    {
        get { lock (_lock) return _down.Count; }
    }

    private static bool IsExtended(int vk, bool reported) =>
        reported || vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5 or 0x6F or 0x90;
}
