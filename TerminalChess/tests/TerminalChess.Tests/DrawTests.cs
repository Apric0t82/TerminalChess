using TerminalChess.Core.Domain;
using TerminalChess.Rules;

namespace TerminalChess.Tests;

public sealed class DrawTests
{
    private const string Rooks = "r3k3/8/8/8/8/8/8/R3K3";
    private static Square S(string value) => new(value[0] - 'a', value[1] - '1');
    private static MoveIntent M(string from, string to) => new(S(from), S(to));
    private static void Play(GeraGame game, string from, string to) =>
        Assert.Equal(MoveResult.Applied, game.TryMove(M(from, to)));
    private static void KnightCycle(GeraGame game)
    {
        Play(game, "g1", "f3"); Play(game, "g8", "f6");
        Play(game, "f3", "g1"); Play(game, "f6", "g8");
    }

    [Theory]
    [InlineData("7k/8/8/8/8/8/8/7K w - - 0 1", true)]
    [InlineData("7k/8/8/8/8/8/8/6BK w - - 0 1", true)]
    [InlineData("7k/8/8/8/8/8/8/6NK w - - 0 1", true)]
    [InlineData("5b1k/8/8/8/8/8/8/6BK w - - 0 1", true)]
    [InlineData("5b1k/8/8/8/8/8/8/4B1BK w - - 0 1", true)]
    [InlineData("6bk/8/8/8/8/8/8/6BK w - - 0 1", false)]
    [InlineData("7k/8/8/8/8/8/8/5NNK w - - 0 1", false)]
    [InlineData("7k/8/8/8/8/8/8/5BNK w - - 0 1", false)]
    public void MaterialDrawRequiresImpossibilityOfMateRatherThanInabilityToForceIt(string fen, bool drawn)
    {
        var game = new GeraGame(fen);
        Assert.Equal(drawn ? new GameOutcome(EndReason.InsufficientMaterial) : null, game.Outcome);
        Assert.Equal(drawn, game.LegalMoves.Count == 0);
    }

    [Fact]
    public void ThreefoldIsClaimableAndFivefoldIsAutomatic()
    {
        var claim = new GeraGame();
        Assert.False(claim.ClaimDraw());
        KnightCycle(claim);
        Assert.False(claim.CanClaimDraw);
        KnightCycle(claim);
        Assert.True(claim.CanClaimDraw);
        Assert.Null(claim.Outcome);
        Assert.True(claim.ClaimDraw());
        Assert.Equal(new GameOutcome(EndReason.ThreefoldRepetition), claim.Outcome);
        Assert.Empty(claim.LegalMoves);

        var automatic = new GeraGame();
        for (var i = 0; i < 3; i++) KnightCycle(automatic);
        Assert.Null(automatic.Outcome);
        KnightCycle(automatic);
        Assert.Equal(new GameOutcome(EndReason.FivefoldRepetition), automatic.Outcome);
        Assert.False(automatic.CanClaimDraw);
    }

    [Fact]
    public void RepetitionRecognizesSixPlyCycles()
    {
        var game = new GeraGame($"{Rooks} w - - 0 1");
        for (var i = 0; i < 2; i++)
        {
            Play(game, "e1", "f1"); Play(game, "e8", "f8");
            Play(game, "f1", "f2"); Play(game, "f8", "f7");
            Play(game, "f2", "e1"); Play(game, "f7", "e8");
        }
        Assert.True(game.CanClaimDraw);
        Assert.True(game.ClaimDraw());
        Assert.Equal(EndReason.ThreefoldRepetition, game.Outcome?.Reason);
    }

    [Fact]
    public void IntendedMoveCanEstablishDrawWithoutChangingThePosition()
    {
        var game = new GeraGame();
        KnightCycle(game);
        Play(game, "g1", "f3"); Play(game, "g8", "f6"); Play(game, "f3", "g1");
        Assert.False(game.CanClaimDraw);
        var before = game.Snapshot;
        Assert.False(game.ClaimDraw(M("f6", "f3")));
        Assert.Same(before, game.Snapshot);
        Assert.True(game.ClaimDraw(M("f6", "g8")));
        Assert.Equal(before.Fen, game.Snapshot.Fen);
        Assert.Equal(PieceSide.Black, game.Turn);
        Assert.Equal(new Piece(PieceSide.Black, PieceKind.Knight), game[S("f6")]);
        Assert.Null(game[S("g8")]);
        Assert.Equal(EndReason.ThreefoldRepetition, game.Outcome?.Reason);
    }

    [Fact]
    public void RepetitionIdentityIncludesCastlingRights()
    {
        var game = new GeraGame("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        for (var i = 0; i < 2; i++)
        {
            Play(game, "h1", "h2"); Play(game, "e8", "d8");
            Play(game, "h2", "h1"); Play(game, "d8", "e8");
        }
        Assert.False(game.CanClaimDraw); // Initial position had K rights; only two occurrences without them.
    }

    [Theory]
    [InlineData("4k3/8/8/3p4/8/8/8/4K2R w - d6 0 1", "h1", "h2", true)]
    [InlineData("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "e1", "d1", true)]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K2R w - d6 0 1", "h1", "h2", false)]
    public void OnlyLegalEnPassantRightsAffectRepetition(string fen, string from, string to, bool claimable)
    {
        var game = new GeraGame(fen);
        for (var i = 0; i < 2; i++)
        {
            Play(game, from, to); Play(game, fen.StartsWith('k') ? "a8" : "e8", fen.StartsWith('k') ? "b8" : "f8");
            Play(game, to, from); Play(game, fen.StartsWith('k') ? "b8" : "f8", fen.StartsWith('k') ? "a8" : "e8");
        }
        Assert.Equal(claimable, game.CanClaimDraw);
    }

    [Fact]
    public void FiftyMoveClaimAndSeventyFiveMoveAutomaticDrawUseHalfmoveBoundaries()
    {
        var game = new GeraGame($"{Rooks} w - - 99 1");
        Assert.False(game.CanClaimDraw);
        var before = game.Snapshot;
        Assert.True(game.ClaimDraw(M("a1", "b1")));
        Assert.Equal(before.Fen, game.Snapshot.Fen);
        Assert.Equal(EndReason.FiftyMoveRule, game.Outcome?.Reason);

        var fifty = new GeraGame($"{Rooks} w - - 99 1");
        Play(fifty, "a1", "b1");
        Assert.Null(fifty.Outcome);
        Assert.True(fifty.CanClaimDraw);
        Assert.True(fifty.ClaimDraw());
        Assert.Equal(EndReason.FiftyMoveRule, fifty.Outcome?.Reason);

        var automatic = new GeraGame($"{Rooks} w - - 148 1");
        Play(automatic, "a1", "b1");
        Assert.Null(automatic.Outcome);
        Play(automatic, "a8", "b8");
        Assert.Equal(EndReason.SeventyFiveMoveRule, automatic.Outcome?.Reason);
        Assert.Equal("150", automatic.Snapshot.Fen.Split(' ')[4]);
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/P7/R3K3 w - - 99 1", "a2", "a3")]
    [InlineData("4k3/8/8/8/8/8/8/Rr2K3 w - - 99 1", "a1", "b1")]
    public void PawnMovesAndCapturesResetTheHalfmoveClock(string fen, string from, string to)
    {
        var game = new GeraGame(fen);
        Play(game, from, to);
        Assert.Equal("0", game.Snapshot.Fen.Split(' ')[4]);
        Assert.False(game.CanClaimDraw);
        Assert.Null(game.Outcome);
    }

    [Fact]
    public void CheckmateTakesPriorityAtTheSeventyFiveMoveBoundary()
    {
        var game = new GeraGame("7k/5Q2/6K1/8/8/8/8/8 w - - 149 1");
        Play(game, "f7", "g7");
        Assert.Equal("150", game.Snapshot.Fen.Split(' ')[4]);
        Assert.Equal(new GameOutcome(EndReason.Checkmate, PieceSide.White), game.Outcome);
    }

    [Fact]
    public void DrawOffersPersistUntilTheRecipientAcceptsDeclinesOrMoves()
    {
        var game = new GeraGame();
        Assert.False(game.OfferDraw()); Assert.False(game.AcceptDraw());
        Play(game, "e2", "e4"); Play(game, "e7", "e5");
        Assert.True(game.OfferDraw()); Assert.False(game.AcceptDraw());
        Assert.Equal(PieceSide.White, game.DrawOffer);
        Play(game, "g1", "f3");
        Assert.Equal(PieceSide.White, game.DrawOffer);
        Assert.True(game.AcceptDraw());
        Assert.Equal(new GameOutcome(EndReason.Agreement), game.Outcome);
        Assert.Null(game.DrawOffer);
        Assert.False(game.OfferDraw()); Assert.False(game.AcceptDraw());

        game.Reset();
        Play(game, "e2", "e4"); Play(game, "e7", "e5");
        Assert.True(game.OfferDraw()); Play(game, "g1", "f3");
        game.DeclineDraw(); Assert.Null(game.DrawOffer);
        Assert.True(game.OfferDraw()); Play(game, "g8", "f6");
        Assert.Equal(PieceSide.Black, game.DrawOffer);
        Play(game, "f1", "c4"); Assert.Null(game.DrawOffer);
        Assert.Null(game.Outcome);
    }

    [Fact]
    public void ResignationAwardsTheOtherSideAndResetClearsDrawHistory()
    {
        var game = new GeraGame();
        KnightCycle(game); KnightCycle(game);
        Assert.True(game.CanClaimDraw);
        game.Reset();
        Assert.False(game.CanClaimDraw);
        Play(game, "e2", "e4");
        game.Resign();
        Assert.Equal(new GameOutcome(EndReason.Resignation, PieceSide.White), game.Outcome);
        Assert.Equal(MoveResult.GameOver, game.TryMove(M("e7", "e5")));
    }
}
