using TerminalChess.Core.Application;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace TerminalChess.Tui;

public static class GameWindow
{
    public static void ShowLoadList(Window window, ChessBoardControl board,
        DifficultySelectionControl selector, GameLoadControl list, Action<TerminalChess.Infrastructure.GameSaveEntry> load)
    {
        IWindowControl previous = window.ContainsControl(selector) && selector.Visible ? selector : board;
        // RemoveContent disposes controls in SharpConsoleUI 2.7.4. Keep reusable views alive.
        previous.Visible = false;
        window.AddControl(list);
        window.FocusManager.SetFocus(list, FocusReason.Programmatic);
        var closed = false;
        void Close(bool selected)
        {
            if (closed) return;
            closed = true;
            window.RemoveContent(list);
            IWindowControl target = selected ? board : previous;
            if (!window.ContainsControl(target)) window.AddControl(target);
            target.Visible = true;
            window.FocusManager.SetFocus((IFocusableControl)target, FocusReason.Programmatic);
            if (target == board) board.Refresh();
        }
        list.Cancelled += () => Close(false);
        list.Selected += entry =>
        {
            if (closed) return;
            Close(true);
            load(entry);
        };
    }

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
