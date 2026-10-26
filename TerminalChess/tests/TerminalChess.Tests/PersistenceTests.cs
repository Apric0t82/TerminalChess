using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;
using TerminalChess.Infrastructure;
using TerminalChess.Rules;

namespace TerminalChess.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminalchess-tests-" + Guid.NewGuid().ToString("N"));
    private sealed class NoOpponent : IChessOpponent
    {
        public Task<MoveIntent> GetMoveAsync(OpponentPosition position, CancellationToken token) => throw new InvalidOperationException("Unexpected engine search.");
    }
    private static GameSession Session(string? fen = null) => new(fen is null ? new GeraGame() : new GeraGame(fen),
        new NoOpponent(), action => { action(); return Task.CompletedTask; }, f => new GeraGame(f));
    private static void Play(GameSession session, params string[] moves)
    {
        foreach (var move in moves) Assert.Equal(MoveResult.Applied, session.TryMove(UciMove.Parse(move)));
    }
    private JsonGameStore Store() => new(Path.Combine(_directory, "games", "saved game.json"));

    [Fact]
    public async Task JsonRoundTripPreservesRepetitionAndClaimedOutcome()
    {
        await using var session = Session();
        for (var i = 0; i < 2; i++) Play(session, "g1f3", "g8f6", "f3g1", "f6g8");
        Assert.True(session.CanClaimDraw);
        var store = Store();
        await store.SaveAsync(session.Export());
        var json = await File.ReadAllTextAsync(store.Path);
        Assert.Contains("\"Version\": 2", json);
        Assert.Contains("\"Move\": \"g1f3\"", json);
        await using var restored = Session();
        restored.Load(await store.LoadAsync());
        Assert.True(restored.CanClaimDraw);
        Assert.Equal(session.Snapshot.Fen, restored.Snapshot.Fen);
        Assert.Equal(8, restored.Export().Actions.Count);
        Assert.True(restored.ClaimDraw());
        await store.SaveAsync(restored.Export());
        session.Load(await store.LoadAsync());
        Assert.Equal(new GameOutcome(EndReason.ThreefoldRepetition), session.Outcome);
        Assert.Empty(session.LegalMoves);
    }

    [Fact]
    public async Task JournalPreservesDrawOfferAgreementAndResignation()
    {
        await using var session = Session();
        Play(session, "e2e4", "e7e5");
        Assert.True(session.OfferDraw()); Play(session, "g1f3");
        var store = Store();
        await store.SaveAsync(session.Export());
        await using var restored = Session();
        restored.Load(await store.LoadAsync());
        Assert.Equal(PieceSide.White, restored.DrawOffer);
        Assert.True(restored.AcceptDraw());
        session.Load(restored.Export());
        Assert.Equal(new GameOutcome(EndReason.Agreement), session.Outcome);
        session.Reset(); session.Resign();
        await store.SaveAsync(session.Export());
        restored.Load(await store.LoadAsync());
        Assert.Equal(new GameOutcome(EndReason.Resignation, PieceSide.Black), restored.Outcome);
    }

    [Fact]
    public async Task IntendedMoveClaimIsRestoredWithoutPlayingTheMove()
    {
        await using var session = Session("r3k3/8/8/8/8/8/8/R3K3 w - - 99 1");
        Assert.True(session.ClaimDraw(UciMove.Parse("a1b1")));
        var store = Store(); await store.SaveAsync(session.Export());
        await using var restored = Session(); restored.Load(await store.LoadAsync());
        Assert.Equal(new GameOutcome(EndReason.FiftyMoveRule), restored.Outcome);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Rook), restored[new Square(0, 0)]);
        Assert.Null(restored[new Square(1, 0)]);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Version\":3,\"InitialFen\":\"start\",\"Actions\":[]}")]
    [InlineData("{\"Version\":1,\"InitialFen\":\"start\",\"Actions\":[null]}")]
    [InlineData("{\"Version\":1,\"InitialFen\":\"start\",\"Actions\":[],\"EngineSide\":\"Purple\"}")]
    public async Task MalformedOrUnsupportedJsonIsRejected(string json)
    {
        var store = Store(); Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
        await File.WriteAllTextAsync(store.Path, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task CancelledSavePreservesPreviousFileAndCleansTemporaryFile()
    {
        await using var session = Session(); var store = Store();
        await store.SaveAsync(session.Export());
        var original = await File.ReadAllTextAsync(store.Path);
        Play(session, "e2e4");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(session.Export(), cancel.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(store.Path));
        Assert.Equal(new[] { store.Path }, Directory.GetFiles(Path.GetDirectoryName(store.Path)!));
    }

    [Fact]
    public async Task EngineSideAndPromotionSurviveJsonEncoding()
    {
        await using var session = Session("7k/P7/8/8/8/8/8/7K w - - 0 1");
        Play(session, "a7a8r");
        var store = Store();
        await store.SaveAsync(session.Export() with { EngineSide = PieceSide.White });
        var saved = await store.LoadAsync();
        Assert.Equal(PieceSide.White, saved.EngineSide);
        await using var restored = Session(); restored.Load(saved);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Rook), restored[new Square(0, 7)]);
        Assert.Equal(PieceSide.White, restored.EngineSide);
    }

    [Fact]
    public async Task PreparedLoadIsIsolatedCancellableAndTransferredOnlyOnce()
    {
        await using var source = Session(); Play(source, "e2e4", "c7c5");
        await using var target = Session();
        var before = target.Snapshot;
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => target.PrepareLoad(source.Export(), cancel.Token));
        var prepared = await Task.Run(() => target.PrepareLoad(source.Export()));
        Assert.Same(before, target.Snapshot);
        target.Load(prepared);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), target[new Square(4, 3)]);
        Assert.Equal(new Piece(PieceSide.Black, PieceKind.Pawn), target[new Square(2, 4)]);
        var loaded = target.Snapshot;
        Assert.Throws<InvalidOperationException>(() => target.Load(prepared));
        Assert.Same(loaded, target.Snapshot);
    }

    [Fact]
    public async Task ReplayingSaveKeepsCapturedRookRightsLostAndEnPassantAvailable()
    {
        var store = Store();
        await using var captured = Session("7k/8/8/8/8/8/Rb6/R3K3 b Q - 0 1");
        Play(captured, "b2a1", "a2a1", "h8g8");
        await store.SaveAsync(captured.Export());
        await using var restored = Session(); restored.Load(await store.LoadAsync());
        Assert.Equal("6k1/8/8/8/8/8/8/R3K3 w - - 1 3", restored.Snapshot.Fen);
        Assert.False(restored.CanMove(new(4, 0), new(2, 0)));

        await using var enPassant = Session();
        Play(enPassant, "e2e4", "a7a6", "e4e5", "d7d5");
        await store.SaveAsync(enPassant.Export()); restored.Load(await store.LoadAsync());
        Play(restored, "e5d6");
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), restored[new Square(3, 5)]);
        Assert.Null(restored[new Square(3, 4)]);
        Assert.Equal("0", restored.Snapshot.Fen.Split(' ')[4]);
    }

    [Fact]
    public async Task MissingAndOversizedFilesFailWithoutLoadingAGame()
    {
        var store = Store();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => store.LoadAsync());
        Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
        await File.WriteAllTextAsync(store.Path, new string(' ', 4 * 1024 * 1024 + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task InvalidJournalMetadataOrActionsCannotReplaceLiveGame()
    {
        await using var session = Session(); Play(session, "e2e4");
        var before = session.Snapshot; var saved = session.Export();
        var invalid = new[]
        {
            saved with { Version = 3 },
            saved with { EngineSide = (PieceSide)99 },
            saved with { Actions = [new((GameActionKind)99)] },
            saved with { Actions = [new(GameActionKind.DeclineDraw)] },
            saved with { Actions = [new(GameActionKind.Resign, UciMove.Parse("e2e4"))] }
        };
        foreach (var document in invalid)
        {
            Assert.Throws<InvalidDataException>(() => session.Load(document));
            Assert.Same(before, session.Snapshot);
        }
    }

    [Theory]
    [InlineData(OpponentDifficulty.Beginner)]
    [InlineData(OpponentDifficulty.Intermediate)]
    [InlineData(OpponentDifficulty.Advanced)]
    [InlineData(OpponentDifficulty.Expert)]
    public async Task DifficultyRoundTripsThroughJsonAndSession(OpponentDifficulty difficulty)
    {
        await using var session = Session();
        session.ConfigureDifficulty(difficulty);
        Play(session, "e2e4");
        var store = Store(); await store.SaveAsync(session.Export());
        var json = await File.ReadAllTextAsync(store.Path);
        Assert.Contains($"\"Difficulty\": \"{difficulty}\"", json);
        var saved = await store.LoadAsync();
        await using var restored = Session(); restored.Load(saved);
        Assert.Equal(difficulty, restored.Difficulty);
        Assert.Equal(difficulty, restored.Export().Difficulty);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), restored[new Square(4, 3)]);
    }

    [Fact]
    public async Task VersionOneSaveWithoutDifficultyLoadsAsIntermediate()
    {
        var store = Store(); Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
        await File.WriteAllTextAsync(store.Path, """
            {"Version":1,"InitialFen":"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
             "EngineSide":null,"Actions":[{"Kind":"Move","Move":"e2e4"}]}
            """);
        var saved = await store.LoadAsync();
        Assert.Equal(OpponentDifficulty.Intermediate, saved.Difficulty);
        await using var restored = Session(); restored.Load(saved);
        Assert.Equal(OpponentDifficulty.Intermediate, restored.Difficulty);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), restored[new Square(4, 3)]);
        Assert.Equal(2, restored.Export().Version);
    }

    [Theory]
    [InlineData("\"Impossible\"")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task InvalidVersionTwoDifficultyIsRejected(string difficulty)
    {
        var store = Store(); Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
        await File.WriteAllTextAsync(store.Path, "{\"Version\":2,\"InitialFen\":\"start\",\"Actions\":[],\"Difficulty\":" + difficulty + "}");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
