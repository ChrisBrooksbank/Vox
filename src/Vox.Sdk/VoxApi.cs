namespace Vox.Sdk;

/// <summary>
/// The version of the add-on API this Vox offers. A minor version only adds (an add-on built for
/// 1.0 runs on 1.3); a major version may change or remove (an add-on built for 1.x is refused by
/// 2.0 unless its manifest says it was tested with 2.0).
/// </summary>
public static class VoxApi
{
    /// <summary>The API version of the running Vox (major.minor).</summary>
    public static Version Current { get; } = new(1, 1);
}

/// <summary>
/// An add-on's entry point: the one public class in its entry assembly that implements this, with
/// a public parameterless constructor. Vox creates it when the add-on is loaded and calls
/// <see cref="Initialize"/> once, on the pipeline thread; everything the add-on adds to Vox it adds
/// there, through the host it is given. When the add-on is disabled, removed or updated Vox
/// takes everything it added back out and, if the class is <see cref="IDisposable"/>, disposes it.
/// </summary>
public interface IAddOn
{
    void Initialize(IAddOnHost host);
}
