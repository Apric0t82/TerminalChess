using System.Runtime.CompilerServices;

namespace Chess.Tests;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable("DisableRealDriverIO", "1");
}
