using TerminalChess.Rules;
using TerminalChess.Core.Domain;
using TerminalChess.Core.Interaction;

namespace TerminalChess.Tests;

public sealed class InteractionTests
{
    private static readonly Square E2 = new(4, 1);
    private static readonly Square E4 = new(4, 3);
    private static ChessBoardInteraction Create() => new(new GeraGame());

    [Fact]
    public void PressSelectsAndDragPreviewsWithoutMutatingBoard()
    {
        var input = Create();
        var pawn = input.Game[E2];
        input.Pointer(PointerAction.Down, E2);
        Assert.Equal(E2, input.Selected);
        Assert.True(input.IsPointerDown);
        input.Pointer(PointerAction.Move, E4);
        Assert.True(input.IsDragging);
        Assert.Equal(E4, input.Target);
        Assert.Equal(pawn, input.Game[E2]);
        Assert.Null(input.Game[E4]);
        input.Pointer(PointerAction.Up, E4);
        Assert.Equal(pawn, input.Game[E4]);
        Assert.Null(input.Game[E2]);
        Assert.Null(input.Selected);
        Assert.False(input.IsPointerDown);
    }

    [Fact]
    public void ReleaseAtDestinationWorksWithoutAnIntermediateMotionReport()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Up, E4);
        Assert.Null(input.Game[E2]);
        Assert.NotNull(input.Game[E4]);
    }

    [Fact]
    public void ClickToMoveUsesTheSameSelection()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Up, E2);
        Assert.Equal(E2, input.Selected);
        input.Pointer(PointerAction.Down, E4);
        input.Pointer(PointerAction.Up, E4);
        Assert.Null(input.Game[E2]);
        Assert.NotNull(input.Game[E4]);
    }

    [Fact]
    public void ClickOnlyTerminalCanSelectAndMove()
    {
        var input = Create();
        input.Pointer(PointerAction.Click, E2);
        input.Pointer(PointerAction.Click, E4);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), input.Game[E4]);
        Assert.Null(input.Game[E2]);
        Assert.Equal(PieceSide.Black, input.Game.Turn);
    }

    [Fact]
    public void LeavingAndReenteringBoardCanCompleteDrag()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Move, null);
        Assert.Null(input.Target);
        input.Pointer(PointerAction.Move, E4);
        input.Pointer(PointerAction.Up, E4);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), input.Game[E4]);
        Assert.Null(input.Game[E2]);
    }

    [Fact]
    public void OutsideReleaseCancelsAndLaterReleasesDoNothing()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Move, null);
        input.Pointer(PointerAction.Up, null);
        input.Pointer(PointerAction.Up, E4);
        Assert.Null(input.Selected);
        Assert.Null(input.Target);
        Assert.NotNull(input.Game[E2]);
        Assert.Null(input.Game[E4]);
        Assert.False(input.IsDragging);
    }

    [Fact]
    public void SameSquareReleaseKeepsSelectionAndDoesNotDeletePiece()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Move, E4);
        input.Pointer(PointerAction.Up, E2);
        Assert.NotNull(input.Game[E2]);
        Assert.Equal(E2, input.Selected);
    }

    [Fact]
    public void ResentPressDoesNotRestartDragAtAnotherPiece()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Down, new Square(3, 1));
        Assert.Equal(E2, input.Selected);
        input.Pointer(PointerAction.Up, E4);
        Assert.Null(input.Game[E2]);
        Assert.NotNull(input.Game[new Square(3, 1)]);
    }

    [Fact]
    public void FriendlyOccupiedDropIsRejected()
    {
        var input = Create();
        var friendly = new Square(3, 1);
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Up, friendly);
        Assert.NotNull(input.Game[E2]);
        Assert.NotNull(input.Game[friendly]);
        Assert.False(input.IsPointerDown);
    }

    [Fact]
    public void ClickOnFriendlyPieceSwitchesSelection()
    {
        var input = Create();
        var friendly = new Square(3, 1);
        input.Pointer(PointerAction.Click, E2);
        input.Pointer(PointerAction.Down, friendly);
        input.Pointer(PointerAction.Up, friendly);
        Assert.Equal(friendly, input.Selected);
        Assert.NotNull(input.Game[E2]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanCaptureOpposingPieceByDragOrClick(bool drag)
    {
        var input = new ChessBoardInteraction(new GeraGame("k7/8/8/8/8/3p4/4P3/7K w - - 0 1"));
        var enemy = new Square(3, 2);
        if (drag)
        {
            input.Pointer(PointerAction.Down, E2);
            input.Pointer(PointerAction.Up, enemy);
        }
        else
        {
            input.Pointer(PointerAction.Down, E2);
            input.Pointer(PointerAction.Up, E2);
            input.Pointer(PointerAction.Down, enemy);
            input.Pointer(PointerAction.Up, enemy);
        }
        Assert.Equal(PieceSide.White, input.Game[enemy]?.Side);
        Assert.Null(input.Game[E2]);
    }

    [Fact]
    public void EmptyPressCannotDragThePreviouslySelectedPiece()
    {
        var input = Create();
        input.Pointer(PointerAction.Click, E2);
        input.Pointer(PointerAction.Down, new Square(3, 3));
        input.Pointer(PointerAction.Up, E4);
        Assert.NotNull(input.Game[E2]);
        Assert.Null(input.Game[E4]);
    }

    [Fact]
    public void KeyboardMovesWithoutMouseAndClampsAtEdges()
    {
        var input = Create();
        Assert.Equal(E2, input.Cursor);
        input.Command(BoardCommand.Activate);
        input.Command(BoardCommand.Up);
        input.Command(BoardCommand.Up);
        Assert.Equal(E4, input.Target);
        input.Command(BoardCommand.Activate);
        Assert.NotNull(input.Game[E4]);
        Assert.Null(input.Selected);
        for (var i = 0; i < 12; i++) { input.Command(BoardCommand.Left); input.Command(BoardCommand.Down); }
        Assert.Equal(new Square(0, 0), input.Cursor);
        for (var i = 0; i < 12; i++) { input.Command(BoardCommand.Right); input.Command(BoardCommand.Up); }
        Assert.Equal(new Square(7, 7), input.Cursor);
    }

    [Fact]
    public void EscapeRecoversLostReleaseAndNextPressStartsFresh()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Move, E4);
        input.Command(BoardCommand.Cancel);
        Assert.Null(input.Selected);
        Assert.False(input.IsPointerDown);
        input.Pointer(PointerAction.Down, E2);
        input.Pointer(PointerAction.Up, E4);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), input.Game[E4]);
        Assert.Null(input.Game[E2]);
    }

    [Fact]
    public void KeyboardCanContinueMouseSelectionAndResetRestoresPosition()
    {
        var input = Create();
        input.Pointer(PointerAction.Down, E2);
        input.Command(BoardCommand.Up);
        Assert.False(input.IsPointerDown);
        input.Command(BoardCommand.Activate);
        Assert.Null(input.Game[E2]);
        input.Command(BoardCommand.Reset);
        Assert.NotNull(input.Game[E2]);
        Assert.Null(input.Game[new Square(4, 2)]);
        Assert.Null(input.Selected);
    }
}
