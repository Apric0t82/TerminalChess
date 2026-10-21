using TerminalChess.Core.Domain;
using TerminalChess.Core.Interaction;
using TerminalChess.Rules;

namespace TerminalChess.Tests;

public sealed class GameplayInteractionTests
{
    private static Square S(string value) => new(value[0] - 'a', value[1] - '1');
    private static ChessBoardInteraction Promotion() => new(new GeraGame("7k/P7/8/8/8/8/8/7K w - - 0 1"));
    private static void BeginPromotion(ChessBoardInteraction input)
    {
        input.Pointer(PointerAction.Down, S("a7"));
        input.Pointer(PointerAction.Up, S("a8"));
    }

    [Fact]
    public void OnlyTheSideToMoveCanBeSelected()
    {
        var input = new ChessBoardInteraction(new GeraGame());
        input.Pointer(PointerAction.Click, S("e7"));
        Assert.Null(input.Selected);
        input.Pointer(PointerAction.Click, S("e2"));
        input.Pointer(PointerAction.Click, S("e4"));
        Assert.Equal(PieceSide.Black, input.Game.Turn);
        input.Pointer(PointerAction.Click, S("d2"));
        Assert.Null(input.Selected);
        input.Pointer(PointerAction.Click, S("e7"));
        Assert.Equal(S("e7"), input.Selected);
    }

    [Fact]
    public void IllegalKeyboardMoveKeepsPositionAndSelectionForRecovery()
    {
        var input = new ChessBoardInteraction(new GeraGame());
        var before = input.Game.Snapshot;
        input.Command(BoardCommand.Activate);
        for (var i = 0; i < 3; i++) input.Command(BoardCommand.Up);
        input.Command(BoardCommand.Activate);
        Assert.Same(before, input.Game.Snapshot);
        Assert.Equal(S("e2"), input.Selected);
        Assert.Contains("Illegal", input.Status);
        input.Command(BoardCommand.Down);
        input.Command(BoardCommand.Activate);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), input.Game[S("e4")]);
        Assert.Null(input.Selected);
    }

    [Theory]
    [InlineData(PieceKind.Queen)]
    [InlineData(PieceKind.Rook)]
    [InlineData(PieceKind.Bishop)]
    [InlineData(PieceKind.Knight)]
    public void PromotionPausesTheMoveUntilAChoiceIsMade(PieceKind kind)
    {
        var input = Promotion();
        var before = input.Game.Snapshot;
        BeginPromotion(input);
        Assert.Equal(new MoveIntent(S("a7"), S("a8")), input.PendingPromotion);
        Assert.Same(before, input.Game.Snapshot);
        Assert.False(input.IsPointerDown);
        input.Pointer(PointerAction.Click, S("h1"));
        input.Command(BoardCommand.Resign);
        input.ChoosePromotion(PieceKind.King);
        Assert.Same(before, input.Game.Snapshot);
        input.ChoosePromotion(kind);
        Assert.Equal(new Piece(PieceSide.White, kind), input.Game[S("a8")]);
        Assert.Null(input.Game[S("a7")]);
        Assert.Null(input.PendingPromotion);
        Assert.Null(input.Selected);
        Assert.Equal(PieceSide.Black, input.Game.Turn);
    }

    [Fact]
    public void CancelAndResetDiscardPendingPromotionWithoutMovingThePawn()
    {
        var input = Promotion();
        var before = input.Game.Snapshot;
        BeginPromotion(input);
        input.Command(BoardCommand.ClaimDraw);
        Assert.Null(input.Game.Outcome);
        Assert.NotNull(input.PendingPromotion);
        input.Command(BoardCommand.Cancel);
        Assert.Null(input.PendingPromotion);
        Assert.Same(before, input.Game.Snapshot);
        BeginPromotion(input);
        input.ChoosePromotion(PieceKind.Rook);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Rook), input.Game[S("a8")]);

        input = Promotion(); BeginPromotion(input);
        input.Command(BoardCommand.Reset);
        Assert.Null(input.PendingPromotion);
        Assert.Equal(32, input.Game.Snapshot.Pieces.Count);
        Assert.Equal(20, input.Game.LegalMoves.Count);
    }

    [Fact]
    public void ClaimBeforeChoosingPromotionUsesTheUnchangedCurrentPosition()
    {
        var game = new GeraGame("7k/P7/8/8/8/8/8/7K w - - 100 1");
        var input = new ChessBoardInteraction(game);
        var before = game.Snapshot;
        BeginPromotion(input);
        input.Command(BoardCommand.ClaimDraw);
        Assert.Equal(EndReason.FiftyMoveRule, game.Outcome?.Reason);
        Assert.Equal(before.Fen, game.Snapshot.Fen);
        Assert.Null(input.PendingPromotion);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), game[new Square(0, 6)]);
    }

    [Fact]
    public void DrawCommandsAndResignationAreAvailableThroughTheInteractionPath()
    {
        var input = new ChessBoardInteraction(new GeraGame("r3k3/8/8/8/8/8/8/R3K3 w - - 99 1"));
        input.Pointer(PointerAction.Click, S("a1"));
        input.Pointer(PointerAction.Move, S("b1"));
        var before = input.Game.Snapshot;
        input.Command(BoardCommand.ClaimDraw);
        Assert.Equal(EndReason.FiftyMoveRule, input.Game.Outcome?.Reason);
        Assert.Equal(before.Fen, input.Game.Snapshot.Fen);
        Assert.Null(input.Selected);
        input.Command(BoardCommand.Reset);
        input.Command(BoardCommand.Resign);
        Assert.Equal(new GameOutcome(EndReason.Resignation, PieceSide.Black), input.Game.Outcome);
        var ended = input.Game.Snapshot;
        input.Pointer(PointerAction.Click, S("e2"));
        input.Command(BoardCommand.Activate);
        Assert.Same(ended, input.Game.Snapshot);
        Assert.Null(input.Selected);
    }

    [Fact]
    public void DrawAgreementUsesTheSameCommandsAsTheView()
    {
        var input = new ChessBoardInteraction(new GeraGame());
        foreach (var square in new[] { "e2", "e4", "e7", "e5" }) input.Pointer(PointerAction.Click, S(square));
        input.Command(BoardCommand.OfferDraw);
        input.Pointer(PointerAction.Click, S("g1")); input.Pointer(PointerAction.Click, S("f3"));
        Assert.Equal(PieceSide.White, input.Game.DrawOffer);
        input.Command(BoardCommand.DeclineDraw);
        Assert.Null(input.Game.DrawOffer);
        input.Command(BoardCommand.OfferDraw);
        input.Pointer(PointerAction.Click, S("g8")); input.Pointer(PointerAction.Click, S("f6"));
        input.Command(BoardCommand.AcceptDraw);
        Assert.Equal(new GameOutcome(EndReason.Agreement), input.Game.Outcome);
        Assert.Null(input.Selected);
    }
}
