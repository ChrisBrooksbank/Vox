using Xunit;

namespace Vox.Core.Tests.Configuration;

/// <summary>
/// Groups settings-related tests into a single collection to prevent parallel execution.
/// Settings tests share the global %APPDATA%/Vox/settings.json file, so they must run sequentially.
/// </summary>
[CollectionDefinition("SettingsTests", DisableParallelization = true)]
public class SettingsTestsCollection { }
