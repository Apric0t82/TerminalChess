using Point = System.Drawing.Point;
using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using TerminalChess.Core.Domain;
using TerminalChess.Core.Interaction;
using TerminalChess.Rules;
using TerminalChess.Tui;

namespace TerminalChess.Tests;

public sealed class GameplayViewTests
{
    private const string PromotionFen = "7k/P7/8/8/8/8/8/7K w - - 0 1";
    private static CharacterBuffer Render(ChessBoardControl board, int width = 80, int height = 25)
    {
        var buffer = new CharacterBuffer(width, height);
        var bounds = new LayoutRect(0, 0, width, height);
        board.PaintDOM(buffer, bounds, bounds, Color.White, Color.Black);
        return buffer;
    }
    private static string Row(CharacterBuffer buffer, int y, int width = 80) =>
        string.Concat(Enumerable.Range(0, width).Select(x => buffer.GetCell(x, y).Character.ToString()));
    private static void Key(ChessBoardControl board, ConsoleKey key) =>
        board.HandleKey(new ConsoleKeyInfo('\0', key, false, false, false));
    private static void Mouse(ChessBoardControl board, int x, int y, params MouseFlags[] flags)
    {
        IMouseAwareControl target = board;
        var point = new Point(x, y);
        Assert.True(target.ProcessMouseEvent(new MouseEventArgs(flags.ToList(), point, point, point)));
    }
    private static void Promote(ChessBoardControl board)
    {
        Render(board);
        Mouse(board, 22, 5, MouseFlags.Button1Pressed); // a7
        Mouse(board, 22, 3, MouseFlags.Button1Released); // a8
        Render(board);
    }

    [Theory]
    [InlineData(ConsoleKey.Q, PieceKind.Queen)]
    [InlineData(ConsoleKey.R, PieceKind.Rook)]
    [InlineData(ConsoleKey.B, PieceKind.Bishop)]
    [InlineData(ConsoleKey.N, PieceKind.Knight)]
    public void PromotionKeysSelectThePieceInsteadOfOtherCommands(ConsoleKey key, PieceKind kind)
    {
        var game = new GeraGame(PromotionFen);
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        var before = game.Snapshot;
        Promote(board);
        Assert.Same(before, game.Snapshot);
        Assert.Contains("[Q queen]", Row(Render(board), 22));
        Key(board, key);
        Assert.Equal(new Piece(PieceSide.White, kind), game[new Square(0, 7)]);
        Assert.Null(game[new Square(0, 6)]);
        Assert.Null(board.Interaction.PendingPromotion);
        Assert.Equal(PieceSide.Black, game.Turn);
    }

    [Theory]
    [InlineData(4, PieceKind.Queen)]
    [InlineData(14, PieceKind.Rook)]
    [InlineData(24, PieceKind.Bishop)]
    [InlineData(35, PieceKind.Knight)]
    public void PromotionButtonsAcceptOneRawClickAndSuppressItsSyntheticDuplicate(int x, PieceKind kind)
    {
        var game = new GeraGame(PromotionFen);
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        Promote(board);
        Mouse(board, x, 22, MouseFlags.Button1Pressed);
        Mouse(board, x, 22, MouseFlags.Button1Released | MouseFlags.Button1Clicked);
        var promoted = game.Snapshot;
        Mouse(board, x, 22, MouseFlags.Button1Clicked);
        Assert.Equal(new Piece(PieceSide.White, kind), game[new Square(0, 7)]);
        Assert.Same(promoted, game.Snapshot);
        Assert.Null(board.Interaction.Selected);
    }

    [Fact]
    public void PromotionButtonMotionOrOutsideReleaseCannotChooseAPiece()
    {
        var game = new GeraGame(PromotionFen);
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        Promote(board);
        var before = game.Snapshot;
        Mouse(board, 4, 22, MouseFlags.Button1Pressed | MouseFlags.ReportMousePosition);
        Mouse(board, 4, 22, MouseFlags.Button1Released);
        Assert.Same(before, game.Snapshot);
        Mouse(board, 4, 22, MouseFlags.Button1Pressed);
        Mouse(board, -2, -2, MouseFlags.Button1Released);
        Mouse(board, 4, 22, MouseFlags.Button1Released);
        Assert.Same(before, game.Snapshot);
        Key(board, ConsoleKey.Escape);
        Assert.Null(board.Interaction.PendingPromotion);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), game[new Square(0, 6)]);
    }

    [Fact]
    public void PromotionClickOnlyAndResizeCancellationPreserveThePosition()
    {
        var game = new GeraGame(PromotionFen);
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        Render(board);
        Mouse(board, 22, 5, MouseFlags.Button1Clicked);
        Mouse(board, 22, 3, MouseFlags.Button1Clicked);
        Render(board);
        Mouse(board, 35, 22, MouseFlags.Button1Clicked);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Knight), game[new Square(0, 7)]);

        var other = new GeraGame(PromotionFen);
        using var resized = new ChessBoardControl(new ChessBoardInteraction(other));
        Promote(resized);
        var before = other.Snapshot;
        Render(resized, 48, 25);
        Assert.Null(resized.Interaction.PendingPromotion);
        Key(resized, ConsoleKey.Q);
        Assert.Same(before, other.Snapshot);
    }

    [Fact]
    public void LegalHighlightsExcludeUnreachableTargetsAndHeaderShowsTurnAndCheck()
    {
        var game = new GeraGame();
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        Render(board);
        Key(board, ConsoleKey.Enter);
        var selected = Render(board);
        Assert.Equal(new Rune('.'), selected.GetCell(40, 13).Character); // e3
        Assert.Equal(new Rune('.'), selected.GetCell(40, 11).Character); // e4
        Assert.Equal(new Rune(' '), selected.GetCell(40, 9).Character); // e5
        Assert.Contains("White to move", Row(selected, 1));
        Key(board, ConsoleKey.UpArrow); Key(board, ConsoleKey.UpArrow); Key(board, ConsoleKey.Enter);
        Assert.Contains("Black to move", Row(Render(board), 1));

        using var checkedBoard = new ChessBoardControl(new ChessBoardInteraction(new GeraGame("4r2k/8/8/8/8/8/8/R3K3 w - - 0 1")));
        Assert.Contains("White to move / CHECK", Row(Render(checkedBoard), 1));
    }

    [Theory]
    [InlineData("7k/5K2/6Q1/8/8/8/8/8 b - - 0 1", "Draw / Stalemate")]
    [InlineData("7k/6Q1/6K1/8/8/8/8/8 b - - 150 1", "White wins / Checkmate")]
    [InlineData("r3k3/8/8/8/8/8/8/R3K3 w - - 150 1", "Draw / 75-move rule")]
    public void OutcomesAreRenderedInTheBoardHeader(string fen, string expected)
    {
        using var board = new ChessBoardControl(new ChessBoardInteraction(new GeraGame(fen)));
        Assert.Contains(expected, Row(Render(board), 1));
    }

    [Fact]
    public void DrawAndResignKeysReachTheGameAndRestartClearsTheOutcome()
    {
        var game = new GeraGame("r3k3/8/8/8/8/8/8/R3K3 w - - 100 1");
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        Assert.Contains("Draw available", Row(Render(board), 2));
        Key(board, ConsoleKey.C);
        Assert.Equal(EndReason.FiftyMoveRule, game.Outcome?.Reason);
        Key(board, ConsoleKey.R);
        Assert.Null(game.Outcome);
        Key(board, ConsoleKey.X);
        Assert.Equal(new GameOutcome(EndReason.Resignation, PieceSide.Black), game.Outcome);
    }
}
