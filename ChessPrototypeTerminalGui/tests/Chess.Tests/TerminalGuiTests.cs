using System.Drawing;
using Chess.Core.Domain;
using Chess.Core.Interaction;
using Chess.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Chess.Tests;

public sealed class TerminalGuiTests
{
    private static readonly Square E2 = new(4, 1);
    private static readonly Square E4 = new(4, 3);

    private sealed class Host : IDisposable
    {
        public IApplication App { get; } = Application.Create();
        public ChessBoardControl Board { get; }
        public Runnable Root { get; }
        private readonly SessionToken _session;

        public Host(int width = 80, int height = 25, bool ascii = false, Point offset = default)
        {
            App.Init(DriverRegistry.Names.ANSI);
            App.Driver!.SetScreenSize(width + offset.X, height + offset.Y);
            Root = new Runnable { App = App };
            Board = new ChessBoardControl(new ChessBoardInteraction(new GameState()), ascii)
                { X = offset.X, Y = offset.Y };
            Root.Add(Board);
            _session = App.Begin(Root)!;
            Draw();
        }

        public void Draw() => App.LayoutAndDraw(true);
        public string Cell(int x, int y) => App.Driver!.Contents![y, x].Grapheme;
        public void Key(Key key) => App.Keyboard.RaiseKeyDownEvent(key);
        public void Mouse(int x, int y, MouseFlags flags)
        {
            App.Mouse.RaiseMouseEvent(new Mouse { ScreenPosition = new Point(x, y), Flags = flags });
            Draw();
        }

        public void Dispose()
        {
            App.End(_session);
            Root.Dispose();
            App.Dispose();
        }
    }

    [Theory]
    [InlineData(48, 25)]
    [InlineData(80, 25)]
    [InlineData(80, 35)]
    [InlineData(80, 49)]
    public void FrameworkRendersAllOriginalPiecesAtTheCurrentSquareSizes(int width, int height)
    {
        using var host = new Host(width, height);
        Assert.Equal(width, host.Board.Viewport.Width);
        Assert.Equal(height, host.Board.Viewport.Height);
        var count = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var text = host.Cell(x, y);
            if (text.Length == 1 && text[0] is >= '\u2654' and <= '\u265f') count++;
        }
        Assert.Equal(32, count);
        Assert.Contains("CHESS / board prototype", host.App.Driver!.ToString());
        Assert.Contains("Ready. Select a piece.", host.App.Driver.ToString());
    }

    [Fact]
    public void KeyboardHasStartupFocusAndMovesWithoutAMouse()
    {
        using var host = new Host();
        Assert.True(host.Board.HasFocus);
        host.Key(Key.Enter);
        host.Key(Key.CursorUp);
        host.Key(Key.CursorUp);
        host.Key(Key.Space);
        Assert.Null(host.Board.Interaction.Game[E2]);
        Assert.NotNull(host.Board.Interaction.Game[E4]);
        host.Key(Key.R);
        Assert.NotNull(host.Board.Interaction.Game[E2]);
    }

    [Fact]
    public void SgrDragKeepsSourceUntilReleaseAndSuppressesDuplicateClick()
    {
        using var host = new Host();
        host.Mouse(42, 15, MouseFlags.LeftButtonPressed);
        Assert.True(host.App.Mouse.IsGrabbed(host.Board));
        host.Mouse(42, 11, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport);
        Assert.True(host.Board.Interaction.IsDragging);
        Assert.Equal(E2, host.Board.Interaction.Selected);
        Assert.Equal(E4, host.Board.Interaction.Target);
        Assert.NotNull(host.Board.Interaction.Game[E2]);
        Assert.Equal(" ", host.Cell(42, 15));
        Assert.Equal("♙", host.Cell(42, 11));
        host.Mouse(42, 11, MouseFlags.LeftButtonReleased | MouseFlags.LeftButtonClicked);
        Assert.Null(host.Board.Interaction.Game[E2]);
        Assert.NotNull(host.Board.Interaction.Game[E4]);
        Assert.False(host.App.Mouse.IsGrabbed());
        host.Mouse(42, 11, MouseFlags.LeftButtonClicked);
        host.Mouse(42, 11, MouseFlags.LeftButtonReleased);
        Assert.Null(host.Board.Interaction.Selected);
    }

    [Fact]
    public void GrabRoutesOutsideViewReleaseAndCancels()
    {
        using var host = new Host();
        host.Mouse(42, 15, MouseFlags.LeftButtonPressed);
        host.Mouse(-3, -5, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport);
        Assert.Null(host.Board.Interaction.Target);
        Assert.True(host.Board.Interaction.IsDragging);
        host.Mouse(-3, -5, MouseFlags.LeftButtonReleased);
        Assert.NotNull(host.Board.Interaction.Game[E2]);
        Assert.Null(host.Board.Interaction.Selected);
        Assert.False(host.Board.Interaction.IsPointerDown);
        Assert.False(host.App.Mouse.IsGrabbed());
    }

    [Fact]
    public void OutsideBoardReleaseOverFooterCancels()
    {
        using var host = new Host();
        host.Mouse(42, 15, MouseFlags.LeftButtonPressed);
        host.Mouse(42, 22, MouseFlags.LeftButtonReleased);
        Assert.Null(host.Board.Interaction.Selected);
        Assert.NotNull(host.Board.Interaction.Game[E2]);
        Assert.False(host.App.Mouse.IsGrabbed());
    }

    [Fact]
    public void EscapeReleasesGrabAndKeepsKeyboardFocusAfterLostRelease()
    {
        using var host = new Host();
        host.Mouse(42, 15, MouseFlags.LeftButtonPressed);
        host.Key(Key.Esc);
        Assert.False(host.App.Mouse.IsGrabbed());
        Assert.False(host.Board.Interaction.IsPointerDown);
        Assert.Null(host.Board.Interaction.Selected);
        Assert.True(host.Board.HasFocus);
        Assert.True(host.Root.IsRunning);
        Assert.False(host.Root.StopRequested);
        host.Key(Key.Enter);
        host.Key(Key.W);
        host.Key(Key.Enter);
        Assert.NotNull(host.Board.Interaction.Game[new Square(4, 2)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickToMoveSupportsRawSequencesAndClickOnlyEvents(bool raw)
    {
        using var host = new Host();
        foreach (var y in new[] { 15, 11 })
        {
            if (raw) host.Mouse(42, y, MouseFlags.LeftButtonPressed);
            host.Mouse(42, y, raw ? MouseFlags.LeftButtonReleased : MouseFlags.LeftButtonClicked);
        }
        Assert.Null(host.Board.Interaction.Game[E2]);
        Assert.NotNull(host.Board.Interaction.Game[E4]);
    }

    [Fact]
    public void FriendlyTargetIsRejectedAndHoverCannotMoveAPiece()
    {
        using var host = new Host();
        host.Mouse(42, 15, MouseFlags.PositionReport);
        Assert.Null(host.Board.Interaction.Selected);
        host.Mouse(42, 15, MouseFlags.LeftButtonPressed);
        host.Mouse(37, 15, MouseFlags.LeftButtonReleased);
        Assert.NotNull(host.Board.Interaction.Game[E2]);
        Assert.NotNull(host.Board.Interaction.Game[new Square(3, 1)]);
    }

    [Fact]
    public void MouseCoordinatesAreRelativeToTheViewEvenWithAnOffset()
    {
        using var host = new Host(offset: new Point(5, 3));
        host.Mouse(47, 18, MouseFlags.LeftButtonPressed);
        host.Mouse(47, 14, MouseFlags.LeftButtonReleased);
        Assert.Null(host.Board.Interaction.Game[E2]);
        Assert.NotNull(host.Board.Interaction.Game[E4]);
    }

    [Fact]
    public void ResizeCancelsActiveDragAndReleasesGrab()
    {
        using var host = new Host();
        host.Mouse(42, 15, MouseFlags.LeftButtonPressed);
        host.App.Driver!.SetScreenSize(48, 25);
        host.Draw();
        Assert.False(host.Board.Interaction.IsPointerDown);
        Assert.Null(host.Board.Interaction.Selected);
        Assert.False(host.App.Mouse.IsGrabbed());
        host.Mouse(42, 11, MouseFlags.LeftButtonReleased);
        Assert.NotNull(host.Board.Interaction.Game[E2]);
    }

    [Fact]
    public void SmallTerminalShowsMessageAndDisablesBoardHitTesting()
    {
        using var host = new Host(40, 15);
        Assert.Contains("Resize terminal", host.App.Driver!.ToString());
        host.Mouse(22, 10, MouseFlags.LeftButtonPressed);
        Assert.False(host.Board.Interaction.IsPointerDown);
        Assert.False(host.App.Mouse.IsGrabbed());
    }

    [Fact]
    public void AsciiFlagAndToggleKeepTheOriginalFallback()
    {
        using var host = new Host(ascii: true);
        Assert.Equal("P", host.Cell(42, 15));
        Assert.Equal("p", host.Cell(42, 5));
        host.Key(Key.U);
        host.Draw();
        Assert.False(host.Board.AsciiPieces);
        Assert.Equal("♙", host.Cell(42, 15));
        host.Key(Key.U.WithShift);
        Assert.True(host.Board.AsciiPieces);
    }

    [Fact]
    public void CtrlQRequestsApplicationStopAndReleasesGrab()
    {
        using var host = new Host();
        host.Mouse(42, 15, MouseFlags.LeftButtonPressed);
        host.Key(Key.Q.WithCtrl);
        Assert.True(host.Root.StopRequested);
        Assert.False(host.App.Mouse.IsGrabbed());
    }

    [Fact]
    public void BlockingApplicationLoopRunsKeyboardMoveAndExitsWithCtrlQ()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(80, 25);
        using var root = new Runnable();
        var board = new ChessBoardControl(new ChessBoardInteraction(new GameState()));
        root.Add(board);
        board.SetFocus();
        var iterations = 0;
        app.Iteration += (_, _) =>
        {
            if (++iterations == 1)
            {
                foreach (var key in new[] { Key.Enter, Key.CursorUp, Key.CursorUp, Key.Enter, Key.Q.WithCtrl })
                    app.Keyboard.RaiseKeyDownEvent(key);
            }
            else if (iterations >= 5) app.RequestStop();
        };
        app.Run(root);
        Assert.Equal(1, iterations);
        Assert.Null(board.Interaction.Game[E2]);
        Assert.NotNull(board.Interaction.Game[E4]);
        Assert.False(root.IsRunning);
    }
}
