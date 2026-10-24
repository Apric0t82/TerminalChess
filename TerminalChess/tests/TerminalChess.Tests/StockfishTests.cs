using System.Diagnostics;
using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;
using TerminalChess.Infrastructure;
using TerminalChess.Rules;

namespace TerminalChess.Tests;

public sealed class StockfishTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminalchess-uci-" + Guid.NewGuid().ToString("N"));
    private StockfishOpponent Engine(string mode, out string log, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(_directory);
        log = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".log");
        var runtime = Path.ChangeExtension(typeof(StockfishTests).Assembly.Location, ".runtimeconfig.json");
        return new StockfishOpponent("dotnet", timeout: timeout, arguments:
            ["exec", "--runtimeconfig", runtime, typeof(FakeUciEngine.Marker).Assembly.Location, mode, log]);
    }
    private static OpponentPosition Position() => new(new GeraGame().Snapshot, new GeraGame().Snapshot.Fen, []);
    private static bool Running(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static void AssertExited(string log) => Assert.False(Running(int.Parse(File.ReadAllText(log + ".pid"))));

    [Fact]
    public async Task HandshakeSendsPositionHistoryAndParsesBestmoveThenQuits()
    {
        var engine = Engine("normal", out var log);
        var initial = new GeraGame();
        var fen = initial.Snapshot.Fen;
        var moves = new[] { UciMove.Parse("g1f3"), UciMove.Parse("g8f6") };
        foreach (var move in moves) Assert.Equal(MoveResult.Applied, initial.TryMove(move));
        var moveResult = await engine.GetMoveAsync(new(initial.Snapshot, fen, moves), default);
        Assert.Equal(new MoveIntent(new(4, 1), new(4, 3)), moveResult);
        Assert.Equal(new[]
        {
            "uci", "setoption name Threads value 1", "setoption name Hash value 16", "ucinewgame", "isready",
            $"position fen {fen} moves g1f3 g8f6", "go movetime 500", "stop", "quit"
        }, await File.ReadAllLinesAsync(log));
        AssertExited(log);
    }

    [Fact]
    public async Task EngineOutputAndLargeStderrAreDrainedWithoutBlocking()
    {
        var engine = Engine("stderr", out var log);
        var move = await engine.GetMoveAsync(Position(), default);
        Assert.Equal(UciMove.Parse("e2e4"), move);
        AssertExited(log);
    }

    [Fact]
    public async Task EnginePromotionIsAdaptedToApplicationMove()
    {
        var engine = Engine("promotion", out var log);
        var move = await engine.GetMoveAsync(Position(), default);
        Assert.Equal(new MoveIntent(new(0, 6), new(0, 7), PieceKind.Knight), move);
        AssertExited(log);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("none")]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task InvalidOrMissingMoveIsRejectedAndProcessIsCleanedUp(string mode)
    {
        var engine = Engine(mode, out var log);
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.GetMoveAsync(Position(), default));
        AssertExited(log);
    }

    [Fact]
    public async Task UnexpectedEngineExitReturnsUsefulFailure()
    {
        var engine = Engine("exit", out var log);
        var error = await Assert.ThrowsAsync<IOException>(() => engine.GetMoveAsync(Position(), default));
        Assert.Contains("exited", error.Message);
        AssertExited(log);
    }

    [Theory]
    [InlineData("handshake-timeout")]
    [InlineData("ready-timeout")]
    [InlineData("search-timeout")]
    public async Task UnresponsiveProtocolHasBoundedDeadlineAndCleanup(string mode)
    {
        var engine = Engine(mode, out var log, TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<TimeoutException>(() => engine.GetMoveAsync(Position(), default));
        AssertExited(log);
    }

    [Fact]
    public async Task CancellationKillsEngineThatIgnoresStopAndQuit()
    {
        var engine = Engine("ignore-quit", out var log);
        using var cancellation = new CancellationTokenSource();
        var search = engine.GetMoveAsync(Position(), cancellation.Token);
        using var readyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (!File.Exists(log) || !(await ReadLiveTranscript()).Contains("go movetime"))
                await Task.Delay(20, readyDeadline.Token);
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        Assert.Contains("stop\nquit\n", (await File.ReadAllTextAsync(log)).Replace("\r", ""));
        AssertExited(log);

        async Task<string> ReadLiveTranscript()
        {
            // Both handles allow concurrent reads/writes; Windows enforces sharing on existing handles too.
            await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
    }

    [Fact]
    public async Task MissingBinaryReportsStartupFailure()
    {
        var engine = new StockfishOpponent(Path.Combine(_directory, "missing-stockfish"));
        var error = await Assert.ThrowsAsync<IOException>(() => engine.GetMoveAsync(Position(), default));
        Assert.Contains("Cannot start Stockfish", error.Message);
    }

    [Theory]
    [InlineData("a7a8q", PieceKind.Queen)]
    [InlineData("a7a8r", PieceKind.Rook)]
    [InlineData("a7a8b", PieceKind.Bishop)]
    [InlineData("a7a8n", PieceKind.Knight)]
    public void UciPromotionMappingIsExplicit(string text, PieceKind expected)
    {
        Assert.Equal(new MoveIntent(new(0, 6), new(0, 7), expected), UciMove.Parse(text));
        Assert.Equal(text, UciMove.Format(new(new(0, 6), new(0, 7), expected)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("e9e4")]
    [InlineData("i2e4")]
    [InlineData("e2e4k")]
    [InlineData("E2e4")]
    [InlineData("e2e4qq")]
    public void InvalidUciCoordinatesAndPromotionsAreRejected(string text) =>
        Assert.Throws<InvalidDataException>(() => UciMove.Parse(text));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
