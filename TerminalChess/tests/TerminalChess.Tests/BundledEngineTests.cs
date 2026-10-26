using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;
using TerminalChess.Infrastructure;
using TerminalChess.Tui;

namespace TerminalChess.Tests;

public sealed class BundledEngineTests
{
    [Theory]
    [InlineData(true, "stockfish.exe")]
    [InlineData(false, "stockfish")]
    public void BundledEngineIsResolvedRelativeToApplicationDirectory(bool windows, string filename)
    {
        var application = Path.Combine(Path.GetTempPath(), "package with spaces");
        var expected = Path.Combine(application, "stockfish", filename);
        string? probed = null;
        var resolved = StockfishExecutable.Resolve(null, null, application, windows, path =>
        {
            probed = path;
            return path == expected;
        });
        Assert.Equal(expected, resolved);
        Assert.Equal(expected, probed);
    }

    [Fact]
    public void ExplicitEngineOverridesEnvironmentAndBundle()
    {
        var resolved = StockfishExecutable.Resolve("custom engine", "environment engine", "package", true,
            _ => throw new InvalidOperationException("Bundle must not be probed."));
        Assert.Equal("custom engine", resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    public void EnvironmentOverridesBundledEngineDuringAutomaticDiscovery(string? requested)
    {
        var resolved = StockfishExecutable.Resolve(requested, "environment engine", "package", true,
            _ => throw new InvalidOperationException("Bundle must not be probed."));
        Assert.Equal("environment engine", resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingBundleFallsBackToPathAndIgnoresEmptyEnvironment(string? configured)
    {
        Assert.Equal("stockfish", StockfishExecutable.Resolve("auto", configured, "package", false, _ => false));
    }

    [Fact]
    public void NoArgumentsStartsHumanWhiteAgainstStockfish()
    {
        var options = StartupOptions.Parse([]);
        Assert.Equal(PieceSide.White, options.Human);
        Assert.Equal(PieceSide.Black, options.EngineSide);
        Assert.False(options.Local);
        Assert.Null(options.Stockfish);
    }

    [Fact]
    public void HumanCanPlayBlackWithoutSupplyingAnEnginePath()
    {
        var options = StartupOptions.Parse(["--human", "black", "--ascii", "--save", "game with spaces.json", "--load"]);
        Assert.Equal(PieceSide.White, options.EngineSide);
        Assert.Equal("game with spaces.json", options.SavePath);
        Assert.True(options.Ascii);
        Assert.True(options.Load);
    }

    [Fact]
    public void LocalFlagKeepsTwoPlayerModeAvailable()
    {
        var options = StartupOptions.Parse(["--local"]);
        Assert.True(options.Local);
        Assert.Null(options.EngineSide);
    }

    [Theory]
    [InlineData("--stockfish")]
    [InlineData("--save")]
    [InlineData("--human")]
    public void MissingOptionValueIsRejected(string flag) =>
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse([flag]));

    [Theory]
    [InlineData("--stockfish", " ")]
    [InlineData("--human", "purple")]
    [InlineData("--stockfish", "--local")]
    public void InvalidOptionValuesAreRejected(string flag, string value) =>
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse([flag, value]));

    [Fact]
    public void LocalModeRejectsConflictingEngineAndHumanOptions()
    {
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--local", "--human", "black"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--stockfish", "auto", "--local"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--unknown"]));
    }

    [Fact]
    public void LoadedModeIsPreservedUnlessLocalOrHumanSideWasExplicitlyRequested()
    {
        var saved = new SavedGame(1, "unused by options", PieceSide.White, []);
        Assert.Same(saved, StartupOptions.Parse([]).ConfigureLoadedGame(saved));
        Assert.Same(saved, StartupOptions.Parse(["--stockfish", "other engine"]).ConfigureLoadedGame(saved));
        Assert.Null(StartupOptions.Parse(["--local"]).ConfigureLoadedGame(saved).EngineSide);
        Assert.Equal(PieceSide.Black, StartupOptions.Parse(["--human", "white"]).ConfigureLoadedGame(saved).EngineSide);
        Assert.Null(StartupOptions.Parse([]).ConfigureLoadedGame(saved with { EngineSide = null }).EngineSide);
    }
}
