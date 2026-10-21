using TerminalChess.Core.Domain;
using TerminalChess.Rules;

namespace TerminalChess.Tests;

public sealed class RulesTests
{
    private static Square S(string square) => new(square[0] - 'a', square[1] - '1');
    private static MoveIntent M(string from, string to, PieceKind? promotion = null) => new(S(from), S(to), promotion);
    private static void Play(GeraGame game, string from, string to, PieceKind? promotion = null) =>
        Assert.Equal(MoveResult.Applied, game.TryMove(M(from, to, promotion)));

    [Fact]
    public void StartingPositionHasTwentyLegalMovesAndAlternatesTurns()
    {
        var game = new GeraGame();
        Assert.Equal(PieceSide.White, game.Turn);
        Assert.Equal(32, game.Snapshot.Pieces.Count);
        Assert.Equal(20, game.LegalMoves.Count);
        Assert.Equal(new[] { S("e3"), S("e4") }, game.LegalMoves.Where(m => m.From == S("e2")).Select(m => m.To).OrderBy(s => s.Rank));
        Play(game, "e2", "e4");
        Assert.Equal(PieceSide.Black, game.Turn);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), game[S("e4")]);
        Assert.Null(game[S("e2")]);
        Assert.False(game.CanMove(S("e4"), S("e5")));
        Play(game, "e7", "e5");
        Assert.Equal(PieceSide.White, game.Turn);
        Assert.Equal("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", game.Snapshot.Fen);
    }

    [Theory]
    [InlineData("e2", "e2")]
    [InlineData("e3", "e4")]
    [InlineData("e7", "e5")]
    [InlineData("e2", "e5")]
    [InlineData("e2", "e1")]
    [InlineData("c1", "h6")]
    [InlineData("e1", "d1")]
    [InlineData("e2", "d3")]
    public void RejectedMoveLeavesSnapshotAndLegalMovesUnchanged(string from, string to)
    {
        var game = new GeraGame();
        var before = game.Snapshot;
        var legal = game.LegalMoves.ToArray();
        Assert.Equal(MoveResult.Illegal, game.TryMove(M(from, to)));
        Assert.Same(before, game.Snapshot);
        Assert.Equal(legal, game.LegalMoves);
        Assert.Equal(PieceSide.White, game.Turn);
    }

    [Fact]
    public void PinsAndCheckMustBeResolvedBeforeAnotherPieceCanMove()
    {
        var pinned = new GeraGame("k3r3/8/8/8/8/8/4R3/4K3 w - - 0 1");
        Assert.False(pinned.CanMove(S("e2"), S("d2")));
        Assert.Equal(MoveResult.Illegal, pinned.TryMove(M("e2", "d2")));
        Play(pinned, "e2", "e8");
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Rook), pinned[S("e8")]);

        var checkedGame = new GeraGame("4r2k/8/8/8/8/8/8/R3K3 w - - 0 1");
        Assert.True(checkedGame.IsCheck);
        var before = checkedGame.Snapshot;
        Assert.Equal(MoveResult.Illegal, checkedGame.TryMove(M("a1", "a2")));
        Assert.Equal(MoveResult.Illegal, checkedGame.TryMove(M("e1", "e2")));
        Assert.Same(before, checkedGame.Snapshot);
        Play(checkedGame, "e1", "d1");
        Assert.False(checkedGame.IsCheck);
    }

    [Theory]
    [InlineData("w", "e1", "g1", "h1", "f1", PieceSide.White)]
    [InlineData("w", "e1", "c1", "a1", "d1", PieceSide.White)]
    [InlineData("b", "e8", "g8", "h8", "f8", PieceSide.Black)]
    [InlineData("b", "e8", "c8", "a8", "d8", PieceSide.Black)]
    public void CastlingMovesKingAndRookTogether(string turn, string from, string to, string rookFrom, string rookTo, PieceSide side)
    {
        var game = new GeraGame($"r3k2r/8/8/8/8/8/8/R3K2R {turn} KQkq - 0 1");
        Play(game, from, to);
        Assert.Equal(new Piece(side, PieceKind.King), game[S(to)]);
        Assert.Equal(new Piece(side, PieceKind.Rook), game[S(rookTo)]);
        Assert.Null(game[S(from)]);
        Assert.Null(game[S(rookFrom)]);
    }

    [Theory]
    [InlineData("4kr2/8/8/8/8/8/8/4K2R w K - 0 1")]
    [InlineData("4r2k/8/8/8/8/8/8/4K2R w K - 0 1")]
    [InlineData("4k1r1/8/8/8/8/8/8/4K2R w K - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4KB1R w K - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K2R w - - 0 1")]
    public void CastlingCannotCrossCheckObstaclesOrMissingRights(string fen)
    {
        var game = new GeraGame(fen);
        var before = game.Snapshot;
        Assert.False(game.CanMove(S("e1"), S("g1")));
        Assert.Equal(MoveResult.Illegal, game.TryMove(M("e1", "g1")));
        Assert.Same(before, game.Snapshot);
    }

    [Fact]
    public void ReturningRookDoesNotRestoreCastlingRights()
    {
        var game = new GeraGame("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        Play(game, "h1", "h2"); Play(game, "e8", "d8");
        Play(game, "h2", "h1"); Play(game, "d8", "e8");
        Assert.False(game.CanMove(S("e1"), S("g1")));
        Assert.Equal("-", game.Snapshot.Fen.Split(' ')[2]);
    }

    [Fact]
    public void ReplacingACapturedRookDoesNotRestoreCastlingRights()
    {
        var game = new GeraGame("7k/8/8/8/8/8/Rb6/R3K3 b Q - 0 1");
        Play(game, "b2", "a1");
        Assert.Equal("7k/8/8/8/8/8/R7/b3K3 w - - 0 2", game.Snapshot.Fen);
        Play(game, "a2", "a1"); Play(game, "h8", "g8");
        Assert.Equal("6k1/8/8/8/8/8/8/R3K3 w - - 1 3", game.Snapshot.Fen);
        Assert.False(game.CanMove(S("e1"), S("c1")));
    }

    [Fact]
    public void EnPassantRemovesTheAdjacentPawnAndExpiresAfterOneTurn()
    {
        var game = new GeraGame();
        Play(game, "e2", "e4"); Play(game, "a7", "a6");
        Play(game, "e4", "e5"); Play(game, "d7", "d5");
        Assert.True(game.CanMove(S("e5"), S("d6")));
        Play(game, "e5", "d6");
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), game[S("d6")]);
        Assert.Null(game[S("d5")]); Assert.Null(game[S("e5")]);
        Assert.Equal("0", game.Snapshot.Fen.Split(' ')[4]);

        var expired = new GeraGame("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        Play(expired, "e1", "d1"); Play(expired, "e8", "d8");
        Assert.False(expired.CanMove(S("e5"), S("d6")));
    }

    [Fact]
    public void EnPassantCannotExposeOwnKing()
    {
        var game = new GeraGame("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        var before = game.Snapshot;
        Assert.Equal(MoveResult.Illegal, game.TryMove(M("e5", "d6")));
        Assert.Same(before, game.Snapshot);
    }

    [Theory]
    [InlineData(PieceKind.Queen)]
    [InlineData(PieceKind.Rook)]
    [InlineData(PieceKind.Bishop)]
    [InlineData(PieceKind.Knight)]
    public void PromotionRequiresAnExplicitChoiceAndSupportsUnderpromotion(PieceKind kind)
    {
        var game = new GeraGame("7k/P7/8/8/8/8/8/7K w - - 0 1");
        var before = game.Snapshot;
        Assert.Equal(4, game.LegalMoves.Count(m => m.From == S("a7")));
        Assert.Equal(MoveResult.PromotionRequired, game.TryMove(M("a7", "a8")));
        Assert.Equal(MoveResult.InvalidPromotion, game.TryMove(M("a7", "a8", PieceKind.King)));
        Assert.Equal(MoveResult.InvalidPromotion, game.TryMove(M("a7", "a8", PieceKind.Pawn)));
        Assert.Same(before, game.Snapshot);
        Play(game, "a7", "a8", kind);
        Assert.Equal(new Piece(PieceSide.White, kind), game[S("a8")]);
        Assert.Null(game[S("a7")]);
        Assert.Equal(PieceSide.Black, game.Turn);
    }

    [Fact]
    public void BlackCanCaptureAndUnderpromote()
    {
        var game = new GeraGame("7k/8/8/8/8/8/p7/1R5K b - - 0 1");
        Play(game, "a2", "b1", PieceKind.Knight);
        Assert.Equal(new Piece(PieceSide.Black, PieceKind.Knight), game[S("b1")]);
        Assert.Null(game[S("a2")]);
        Assert.Equal(new GameOutcome(EndReason.InsufficientMaterial), game.Outcome);
    }

    [Fact]
    public void CheckmateStopsPlayAndResetStartsAFreshGame()
    {
        var game = new GeraGame();
        Play(game, "f2", "f3"); Play(game, "e7", "e5");
        Play(game, "g2", "g4"); Play(game, "d8", "h4");
        Assert.True(game.IsCheck);
        Assert.Equal(new GameOutcome(EndReason.Checkmate, PieceSide.Black), game.Outcome);
        Assert.Empty(game.LegalMoves);
        var mate = game.Snapshot;
        Assert.Equal(MoveResult.GameOver, game.TryMove(M("e2", "e3")));
        Assert.Same(mate, game.Snapshot);
        game.Reset();
        Assert.Null(game.Outcome); Assert.False(game.IsCheck);
        Assert.False(game.CanClaimDraw); Assert.Null(game.DrawOffer);
        Assert.Equal(20, game.LegalMoves.Count);
        Assert.Equal(32, game.Snapshot.Pieces.Count);
        Assert.True(game.Snapshot.Revision > mate.Revision);
    }

    [Fact]
    public void StalemateIsADrawWithoutCheck()
    {
        var game = new GeraGame("7k/5K2/6Q1/8/8/8/8/8 b - - 0 1");
        Assert.False(game.IsCheck);
        Assert.Equal(new GameOutcome(EndReason.Stalemate), game.Outcome);
        Assert.Empty(game.LegalMoves);
    }

    [Fact]
    public void SnapshotsRemainImmutableAfterFurtherMoves()
    {
        var game = new GeraGame();
        var initial = game.Snapshot;
        Play(game, "e2", "e4");
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), initial.Pieces[S("e2")]);
        Assert.False(initial.Pieces.ContainsKey(S("e4")));
        Assert.Equal(PieceSide.White, initial.Turn);
        Assert.True(game.Snapshot.Revision > initial.Revision);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<Square, Piece>)initial.Pieces).Clear());
    }
}
