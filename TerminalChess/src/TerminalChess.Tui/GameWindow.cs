using TerminalChess.Core.Application;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace TerminalChess.Tui;

public static class GameWindow
{
    public static Window Create(ConsoleWindowSystem system, GameSession session,
        ChessBoardControl board, DifficultySelectionControl selector)
    {
        var chooseDifficulty = session.EngineSide is not null;
        IWindowControl first = chooseDifficulty ? selector : board;
        var window = new WindowBuilder(system).WithTitle("TerminalChess")
            .Maximized().Frameless().Resizable(false).AddControl(first).Build();
        if (chooseDifficulty)
        {
            selector.Confirmed += difficulty =>
            {
                window.RemoveContent(selector);
                window.AddControl(board);
                window.FocusManager.SetFocus(board, FocusReason.Programmatic);
                session.ConfigureDifficulty(difficulty);
            };
        }
        system.AddWindow(window);
        window.FocusManager.SetFocus((IFocusableControl)first, FocusReason.Programmatic);
        if (!chooseDifficulty) system.EnqueueOnUIThread(session.StartOpponent);
        return window;
    }
}
