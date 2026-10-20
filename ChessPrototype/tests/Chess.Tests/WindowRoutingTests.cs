using Point = System.Drawing.Point;
using Chess.Core.Domain;
using Chess.Core.Interaction;
using Chess.Tui;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;

namespace Chess.Tests;

/// <summary>Exercises production framework layout, focus, input dispatch, capture and rendering.</summary>
public sealed class WindowRoutingTests
{
    private sealed class Host : IDisposable
    {
        public HeadlessConsoleDriver Driver { get; } = new(80, 25);
        public ConsoleWindowSystem System { get; }
        public ChessBoardControl Board { get; } = new(new ChessBoardInteraction(new GameState()));
        public HostedSession Session { get; }

        public Host()
        {
            System = new ConsoleWindowSystem(Driver, options: new ConsoleWindowSystemOptions(
                ShowTopPanel: false, ShowBottomPanel: false, EnableFrameRateLimiting: false,
                EnableAnimations: false, EnableDiagnostics: true)) { BlockWhenIdle = false };
            var window = new WindowBuilder(System).WithTitle("Chess prototype")
                .Maximized().Frameless().Resizable(false).AddControl(Board).Build();
            System.AddWindow(window);
            window.FocusManager.SetFocus(Board, FocusReason.Programmatic);
            Session = System.BeginHosted();
            Session.Tick();
        }

        public void Mouse(Point point, params MouseFlags[] flags)
        {
            Driver.SimulateMouseEvent(flags.ToList(), point);
            Session.Tick();
        }

        public void Key(ConsoleKey key)
        {
            Driver.SimulateKeyPress(new ConsoleKeyInfo('\0', key, false, false, false));
            Session.Tick();
        }

        public void Dispose()
        {
            Session.Dispose();
            Board.Dispose();
            Driver.Dispose();
        }
    }

    [Fact]
    public void FullWindowRendersAndKeyboardHasFocusAtStartup()
    {
        using var host = new Host();
        Assert.Equal(80, host.Board.CanvasWidth);
        Assert.Equal(25, host.Board.CanvasHeight);
        Assert.True(host.Board.HasFocus);
        host.Key(ConsoleKey.Enter);
        host.Key(ConsoleKey.UpArrow);
        host.Key(ConsoleKey.UpArrow);
        host.Key(ConsoleKey.Enter);
        Assert.Null(host.Board.Interaction.Game[new Square(4, 1)]);
        Assert.NotNull(host.Board.Interaction.Game[new Square(4, 3)]);
    }

    [Fact]
    public void FrameworkCaptureRoutesReleaseEvenOutsideTheWindow()
    {
        using var host = new Host();
        host.Mouse(new Point(42, 15), MouseFlags.Button1Pressed); // e2
        Assert.Equal(new Square(4, 1), host.Board.Interaction.Selected);
        host.Mouse(new Point(-10, -10), MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition);
        Assert.True(host.Board.Interaction.IsDragging);
        Assert.Null(host.Board.Interaction.Target);
        host.Mouse(new Point(-10, -10), MouseFlags.Button1Released);
        Assert.False(host.Board.Interaction.IsPointerDown);
        Assert.Null(host.Board.Interaction.Selected);
        Assert.NotNull(host.Board.Interaction.Game[new Square(4, 1)]);
    }

    [Fact]
    public void FullFrameworkRoutesSgrDragAndReleaseIntoOneMove()
    {
        using var host = new Host();
        host.Mouse(new Point(42, 15), MouseFlags.Button1Pressed);
        host.Mouse(new Point(42, 11), MouseFlags.Button1Pressed, MouseFlags.ReportMousePosition);
        Assert.Equal(new Square(4, 3), host.Board.Interaction.Target);
        host.Mouse(new Point(42, 11), MouseFlags.Button1Released, MouseFlags.Button1Clicked);
        Assert.Null(host.Board.Interaction.Game[new Square(4, 1)]);
        Assert.NotNull(host.Board.Interaction.Game[new Square(4, 3)]);
        Assert.Null(host.Board.Interaction.Selected);
    }

    [Fact]
    public void EscapeCancelsLostReleaseWithoutLosingKeyboardFocus()
    {
        using var host = new Host();
        host.Mouse(new Point(42, 15), MouseFlags.Button1Pressed);
        host.Key(ConsoleKey.Escape);
        Assert.False(host.Board.Interaction.IsPointerDown);
        Assert.Null(host.Board.Interaction.Selected);
        Assert.True(host.Board.HasFocus);
        host.Key(ConsoleKey.Enter);
        host.Key(ConsoleKey.UpArrow);
        host.Key(ConsoleKey.Enter);
        Assert.NotNull(host.Board.Interaction.Game[new Square(4, 2)]);
    }
}
