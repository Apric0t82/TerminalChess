using Point = System.Drawing.Point;
using System.Threading.Channels;
using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;
using TerminalChess.Core.Interaction;
using TerminalChess.Rules;
using TerminalChess.Tui;

namespace TerminalChess.Tests;

public sealed class DifficultySelectionTests
{
    private static CharacterBuffer Render(DifficultySelectionControl selector, int width = 80, int height = 25)
    {
        var buffer = new CharacterBuffer(width, height);
        var bounds = new LayoutRect(0, 0, width, height);
        selector.PaintDOM(buffer, bounds, bounds, Color.White, Color.Black);
        return buffer;
    }
    private static string Row(CharacterBuffer buffer, int row, int width) =>
        string.Concat(Enumerable.Range(0, width).Select(x => buffer.GetCell(x, row).Character.ToString()));
    private static void Key(DifficultySelectionControl selector, ConsoleKey key) =>
        selector.HandleKey(new ConsoleKeyInfo('\0', key, false, false, false));
    private static void Mouse(DifficultySelectionControl selector, int x, int y, params MouseFlags[] flags)
    {
        IMouseAwareControl target = selector;
        var point = new Point(x, y);
        target.ProcessMouseEvent(new MouseEventArgs(flags.ToList(), point, point, point));
    }

    [Theory]
    [InlineData(48, 25)]
    [InlineData(80, 25)]
    [InlineData(120, 49)]
    public void SelectorIsCenteredOnBlackWithAllDescriptionsAndColoredSelection(int width, int height)
    {
        using var selector = new DifficultySelectionControl();
        var buffer = Render(selector, width, height);
        var top = (height - 21) / 2;
        var left = (width - Math.Min(68, width - 4)) / 2;
        const string title = "Select AI opponent level";
        Assert.Equal((width - title.Length) / 2, Row(buffer, top, width).IndexOf(title, StringComparison.Ordinal));
        var names = new[] { "Beginner", "Intermediate", "Advanced", "Expert" };
        var descriptions = new[]
        {
            "Forgiving opponent, shallow tactics", "Reasonable tactics, occasional mistakes",
            "Strong positional and tactical play", "Full-strength Stockfish"
        };
        for (var index = 0; index < 4; index++)
        {
            Assert.Contains(names[index], Row(buffer, top + 3 + index * 3, width));
            Assert.Contains(descriptions[index], Row(buffer, top + 4 + index * 3, width));
        }
        Assert.Contains("Stockfish Skill Level: 8", Row(buffer, top + 16, width));
        Assert.Contains("Search time per move: 500 ms", Row(buffer, top + 17, width));
        Assert.Equal(Color.Black, buffer.GetCell(0, 0).Background);
        Assert.Equal(Color.Black, buffer.GetCell(width - 1, height - 1).Background);
        Assert.Equal(new Color(102, 218, 214), buffer.GetCell((width - title.Length) / 2, top).Foreground);
        Assert.Equal(new Color(255, 211, 117), buffer.GetCell(left + 3, top + 6).Foreground);
        Assert.Equal(new Color(14, 43, 48), buffer.GetCell(left, top + 6).Background);
    }

    [Theory]
    [InlineData(OpponentDifficulty.Beginner, 0, 100)]
    [InlineData(OpponentDifficulty.Intermediate, 8, 500)]
    [InlineData(OpponentDifficulty.Advanced, 15, 1500)]
    [InlineData(OpponentDifficulty.Expert, 20, 3000)]
    public void EachLevelDisplaysItsActualSkillAndSearchTime(OpponentDifficulty level, int skill, int milliseconds)
    {
        using var selector = new DifficultySelectionControl(level);
        var buffer = Render(selector);
        Assert.Equal(skill, OpponentStrength.For(level).SkillLevel);
        Assert.Equal(milliseconds, OpponentStrength.For(level).MoveTimeMs);
        Assert.Contains($"Stockfish Skill Level: {skill}", Row(buffer, 18, 80));
        Assert.Contains($"Search time per move: {milliseconds} ms", Row(buffer, 19, 80));
    }

    [Fact]
    public void KeyboardSelectionUpdatesDetailsAndConfirmsOnlyOnce()
    {
        using var selector = new DifficultySelectionControl(); Render(selector);
        var confirmed = new List<OpponentDifficulty>(); selector.Confirmed += confirmed.Add;
        Key(selector, ConsoleKey.UpArrow); Key(selector, ConsoleKey.UpArrow);
        Assert.Equal(OpponentDifficulty.Beginner, selector.SelectedDifficulty);
        Key(selector, ConsoleKey.S); Key(selector, ConsoleKey.DownArrow);
        var buffer = Render(selector);
        Assert.Contains("Stockfish Skill Level: 15", Row(buffer, 18, 80));
        Assert.Empty(confirmed);
        Key(selector, ConsoleKey.Enter); Key(selector, ConsoleKey.Spacebar);
        Key(selector, ConsoleKey.DownArrow);
        Assert.Equal(new[] { OpponentDifficulty.Advanced }, confirmed);
        Assert.Equal(OpponentDifficulty.Advanced, selector.SelectedDifficulty);
    }

    [Fact]
    public void MouseChoosesLevelAndStartButtonRequiresMatchingRelease()
    {
        using var selector = new DifficultySelectionControl(); Render(selector);
        var confirmed = new List<OpponentDifficulty>(); selector.Confirmed += confirmed.Add;
        Mouse(selector, 10, 14, MouseFlags.Button1Pressed); // Expert.
        Mouse(selector, 10, 14, MouseFlags.Button1Released | MouseFlags.Button1Clicked);
        Assert.Equal(OpponentDifficulty.Expert, selector.SelectedDifficulty);
        Assert.Contains("Search time per move: 3000 ms", Row(Render(selector), 19, 80));
        Mouse(selector, 40, 20, MouseFlags.Button1Pressed);
        Mouse(selector, -1, -1, MouseFlags.Button1Released);
        Assert.Empty(confirmed);
        Mouse(selector, 40, 20, MouseFlags.Button1Pressed);
        Mouse(selector, 40, 20, MouseFlags.Button1Released | MouseFlags.ReportMousePosition | MouseFlags.Button1Pressed);
        Mouse(selector, 40, 20, MouseFlags.Button1Clicked);
        Assert.Equal(new[] { OpponentDifficulty.Expert }, confirmed);
    }

    [Fact]
    public void ClickOnlyInputCanSelectAndStartWithoutRawPressEvents()
    {
        using var selector = new DifficultySelectionControl(); Render(selector);
        OpponentDifficulty? confirmed = null; selector.Confirmed += level => confirmed = level;
        Mouse(selector, 10, 5, MouseFlags.Button1Clicked);
        Assert.Equal(OpponentDifficulty.Beginner, selector.SelectedDifficulty);
        Mouse(selector, 40, 20, MouseFlags.Button1Clicked);
        Assert.Equal(OpponentDifficulty.Beginner, confirmed);
    }

    [Fact]
    public void ResizeClearsPendingStartAndSmallTerminalCannotConfirm()
    {
        using var selector = new DifficultySelectionControl(); Render(selector);
        var count = 0; selector.Confirmed += _ => count++;
        Mouse(selector, 40, 20, MouseFlags.Button1Pressed);
        Render(selector, 48, 25);
        Mouse(selector, 24, 20, MouseFlags.Button1Released);
        Assert.Equal(0, count);
        var small = Render(selector, 47, 24);
        Assert.Contains("Resize terminal", Row(small, 12, 47));
        Key(selector, ConsoleKey.Enter);
        Assert.Equal(0, count);
        Render(selector, 48, 25); Key(selector, ConsoleKey.Enter);
        Assert.Equal(1, count);
    }

    private sealed class WaitingOpponent : IChessOpponent
    {
        public Channel<OpponentPosition> Requests { get; } = Channel.CreateUnbounded<OpponentPosition>();
        public async Task<MoveIntent> GetMoveAsync(OpponentPosition position, CancellationToken token)
        {
            await Requests.Writer.WriteAsync(position, token);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWindowStartsWithSelectorThenFocusesBoardAndStartsChosenOpponent(bool mouse)
    {
        using var driver = new HeadlessConsoleDriver(80, 25);
        var system = new ConsoleWindowSystem(driver, options: new ConsoleWindowSystemOptions(
            ShowTopPanel: false, ShowBottomPanel: false, EnableFrameRateLimiting: false,
            EnableAnimations: false)) { BlockWhenIdle = false };
        var opponent = new WaitingOpponent();
        await using var game = new GameSession(new GeraGame(), opponent, a => system.InvokeAsync(a),
            fen => new GeraGame(fen), PieceSide.White, deferOpponent: true);
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        using var selector = new DifficultySelectionControl();
        var window = GameWindow.Create(system, game, board, selector);
        using var hosted = system.BeginHosted(); hosted.Tick();
        Assert.True(window.ContainsControl(selector)); Assert.False(window.ContainsControl(board));
        Assert.True(selector.HasFocus);
        game.StartOpponent();
        Assert.False(opponent.Requests.Reader.TryRead(out _));
        if (mouse)
        {
            driver.SimulateMouseEvent([MouseFlags.Button1Pressed], new Point(10, 11)); hosted.Tick();
            driver.SimulateMouseEvent([MouseFlags.Button1Released], new Point(10, 11)); hosted.Tick();
            driver.SimulateMouseEvent([MouseFlags.Button1Pressed], new Point(40, 20)); hosted.Tick();
            driver.SimulateMouseEvent([MouseFlags.Button1Released | MouseFlags.Button1Clicked], new Point(40, 20)); hosted.Tick();
        }
        else
        {
            driver.SimulateKeyPress(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false)); hosted.Tick();
            driver.SimulateKeyPress(new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false)); hosted.Tick();
        }
        Assert.False(window.ContainsControl(selector)); Assert.True(window.ContainsControl(board));
        Assert.True(board.HasFocus);
        var request = await opponent.Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(OpponentDifficulty.Advanced, request.Difficulty);
        Assert.Equal(OpponentDifficulty.Advanced, game.Difficulty);
    }

    [Fact]
    public async Task LocalWindowSkipsDifficultySelection()
    {
        using var driver = new HeadlessConsoleDriver(80, 25);
        var system = new ConsoleWindowSystem(driver, options: new ConsoleWindowSystemOptions(
            ShowTopPanel: false, ShowBottomPanel: false, EnableAnimations: false)) { BlockWhenIdle = false };
        var opponent = new WaitingOpponent();
        await using var game = new GameSession(new GeraGame(), opponent, a => system.InvokeAsync(a), fen => new GeraGame(fen));
        using var board = new ChessBoardControl(new ChessBoardInteraction(game));
        using var selector = new DifficultySelectionControl();
        var window = GameWindow.Create(system, game, board, selector);
        using var hosted = system.BeginHosted(); hosted.Tick();
        Assert.True(window.ContainsControl(board)); Assert.False(window.ContainsControl(selector));
        Assert.True(board.HasFocus);
        Assert.False(opponent.Requests.Reader.TryRead(out _));
    }
}
