using Point = System.Drawing.Point;
using System.Text;
using Chess.Core.Domain;
using Chess.Core.Interaction;
using Chess.Tui;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;

namespace Chess.Tests;

public sealed class SharpConsoleAdapterTests
{
    private static readonly Square E2 = new(4, 1);
    private static readonly Square E4 = new(4, 3);

    private static ChessBoardControl Create()
    {
        var board = new ChessBoardControl(new ChessBoardInteraction(new GameState()));
        Render(board);
        return board;
    }

    private static CharacterBuffer Render(ChessBoardControl board, int width = 80, int height = 25)
    {
        var buffer = new CharacterBuffer(width, height);
        var bounds = new LayoutRect(0, 0, width, height);
        board.PaintDOM(buffer, bounds, bounds, Color.White, Color.Black);
        return buffer;
    }

    private static Point Cell(Square square) => new(20 + square.File * 5 + 2, 3 + (7 - square.Rank) * 2);

    private static void Send(ChessBoardControl board, Point point, params MouseFlags[] flags)
    {
        // Use the same interface dispatch as WindowEventDispatcher, not a direct subclass call.
        IMouseAwareControl target = board;
        var args = new MouseEventArgs(flags.ToList(), point, point, point);
        Assert.True(target.ProcessMouseEvent(args));
        Assert.True(args.Handled);
    }

    [Fact]
    public void SgrMotionWithPressFlagContinuesTheOriginalDrag()
    {
        using var board = Create();
        Send(board, Cell(E2), MouseFlags.Button1Pressed);
        // Combined flags must work as well as separate entries.
        Send(board, Cell(E4), MouseFlags.Button1Pressed | MouseFlags.ReportMousePosition);
        Assert.True(board.Interaction.IsDragging);
        Assert.Equal(E2, board.Interaction.Selected);
        Assert.Equal(E4, board.Interaction.Target);
        Send(board, Cell(E4), MouseFlags.Button1Released | MouseFlags.Button1Clicked);
        Assert.Null(board.Interaction.Game[E2]);
        Assert.NotNull(board.Interaction.Game[E4]);
        Assert.Null(board.Interaction.Selected);
        Send(board, Cell(E4), MouseFlags.Button1Clicked);
        Send(board, Cell(E4), MouseFlags.Button1Released);
        Assert.Null(board.Interaction.Selected);
    }

    [Fact]
    public void ExplicitDragFlagsAndNegativeCapturedCoordinatesCancel()
    {
        using var board = Create();
        Send(board, Cell(E2), MouseFlags.Button1Pressed);
        Send(board, new Point(-8, -4), MouseFlags.Button1Dragged);
        Assert.True(board.Interaction.IsDragging);
        Assert.Null(board.Interaction.Target);
        Send(board, new Point(-8, -4), MouseFlags.Button1Released);
        Assert.NotNull(board.Interaction.Game[E2]);
        Assert.Null(board.Interaction.Selected);
        Assert.False(board.Interaction.IsPointerDown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickToMoveWorksWithClickOnlyOrRawEvents(bool raw)
    {
        using var board = Create();
        foreach (var square in new[] { E2, E4 })
        {
            if (raw) Send(board, Cell(square), MouseFlags.Button1Pressed);
            Send(board, Cell(square), raw ? MouseFlags.Button1Released | MouseFlags.Button1Clicked : MouseFlags.Button1Clicked);
        }
        Assert.Null(board.Interaction.Game[E2]);
        Assert.NotNull(board.Interaction.Game[E4]);
    }

    [Fact]
    public void HoverCannotStartOrFinishAMove()
    {
        using var board = Create();
        Send(board, Cell(E2), MouseFlags.ReportMousePosition);
        Send(board, Cell(E4), MouseFlags.ReportMousePosition);
        Assert.Null(board.Interaction.Selected);
        Assert.False(board.Interaction.IsPointerDown);
        Assert.NotNull(board.Interaction.Game[E2]);
        Assert.Null(board.Interaction.Game[E4]);
    }

    [Theory]
    [InlineData(MouseFlags.Button1DoubleClicked)]
    [InlineData(MouseFlags.Button1TripleClicked)]
    public void UnixMultiClickPressCanStillChooseAClickToMoveDestination(MouseFlags rapidPress)
    {
        using var board = Create();
        Send(board, Cell(E2), MouseFlags.Button1Pressed);
        Send(board, Cell(E2), MouseFlags.Button1Released, MouseFlags.Button1Clicked);
        Send(board, Cell(E4), rapidPress);
        Assert.True(board.Interaction.IsPointerDown);
        Assert.NotNull(board.Interaction.Game[E2]);
        Send(board, Cell(E4), MouseFlags.Button1Released);
        Assert.Null(board.Interaction.Game[E2]);
        Assert.NotNull(board.Interaction.Game[E4]);
    }

    [Fact]
    public void EscapeAndKeyboardSelectionWorkThroughTheControlAdapter()
    {
        using var board = Create();
        Send(board, Cell(E2), MouseFlags.Button1Pressed);
        board.HandleKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        Assert.Null(board.Interaction.Selected);
        Assert.False(board.Interaction.IsPointerDown);
        board.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));
        board.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        board.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.Null(board.Interaction.Game[E2]);
        Assert.NotNull(board.Interaction.Game[new Square(4, 2)]);
    }

    [Theory]
    [InlineData(80, 25)]
    [InlineData(80, 35)]
    [InlineData(48, 25)]
    [InlineData(80, 49)]
    public void RealCanvasRenderingContainsAllPiecesAndRankAndFileLabels(int width, int height)
    {
        using var board = Create();
        var buffer = Render(board, width, height);
        var cells = Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width)
            .Select(x => buffer.GetCell(x, y).Character.ToString())).ToArray();
        Assert.Equal(32, cells.Count(c => c.Length == 1 && c[0] is >= '\u2654' and <= '\u265f'));
        Assert.Contains("a", cells);
        Assert.Contains("h", cells);
        Assert.Contains("8", cells);
        Assert.Contains("1", cells);
    }

    [Fact]
    public void AsciiToggleRendersUppercaseWhiteAndLowercaseBlackPieces()
    {
        using var board = Create();
        board.HandleKey(new ConsoleKeyInfo('u', ConsoleKey.U, false, false, false));
        Assert.True(board.AsciiPieces);
        var buffer = Render(board);
        var white = Cell(E2);
        var black = Cell(new Square(4, 6));
        Assert.Equal(new Rune('P'), buffer.GetCell(white.X, white.Y).Character);
        Assert.Equal(new Rune('p'), buffer.GetCell(black.X, black.Y).Character);
    }

    [Fact]
    public void SmallerSquaresMapDragAndPreviewTheOriginalSymbol()
    {
        using var board = Create();
        Render(board, 80, 49);
        Send(board, new Point(43, 28), MouseFlags.Button1Pressed); // e2 on the 8 x 4 squares
        Send(board, new Point(43, 20), MouseFlags.Button1Dragged);
        var preview = Render(board, 80, 49);
        Assert.Equal(E4, board.Interaction.Target);
        Assert.Equal(new Rune(' '), preview.GetCell(43, 28).Character);
        Assert.Equal(new Rune('♙'), preview.GetCell(43, 20).Character);
        Assert.NotNull(board.Interaction.Game[E2]);
        Send(board, new Point(43, 20), MouseFlags.Button1Released);
        Assert.Null(board.Interaction.Game[E2]);
        Assert.NotNull(board.Interaction.Game[E4]);
    }

    [Fact]
    public void ResizeDuringDragCancelsSoOldGeometryCannotMakeAMove()
    {
        using var board = Create();
        Send(board, Cell(E2), MouseFlags.Button1Pressed);
        Send(board, Cell(E4), MouseFlags.Button1Dragged);
        Render(board, 48, 25);
        Assert.False(board.Interaction.IsPointerDown);
        Send(board, Cell(E4), MouseFlags.Button1Released);
        Assert.NotNull(board.Interaction.Game[E2]);
        Assert.Null(board.Interaction.Game[E4]);
    }

    [Fact]
    public void TooSmallTerminalHidesBoardAndDisablesItsMapping()
    {
        using var board = Create();
        Send(board, Cell(E2), MouseFlags.Button1Pressed);
        var buffer = Render(board, 40, 15);
        Assert.False(board.Interaction.IsPointerDown);
        Send(board, Cell(E4), MouseFlags.Button1Released);
        Assert.NotNull(board.Interaction.Game[E2]);
        Assert.Equal(new Rune('R'), buffer.GetCell(2, 2).Character);
    }
}
