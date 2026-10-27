using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using TerminalChess.Infrastructure;

namespace TerminalChess.Tui;

public sealed class GameLoadControl : CanvasControl
{
    private readonly IReadOnlyList<GameSaveEntry> _entries;
    private int _selected;
    public event Action<GameSaveEntry>? Selected;
    public event Action? Cancelled;

    public GameLoadControl(IReadOnlyList<GameSaveEntry> entries) : base(80, 25)
    {
        _entries = entries;
        AutoSize = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Fill;
        BackgroundColor = Color.Black;
        CanvasKeyPressed += (_, key) => HandleKey(key);
        Paint += (_, e) =>
        {
            var g = e.Graphics;
            var width = e.CanvasWidth;
            var height = e.CanvasHeight;
            g.FillRect(0, 0, width, height, Color.Black);
            g.WriteString(2, 1, "Load game", Color.Cyan, Color.Black);
            var rows = Math.Max(1, height - 7);
            var first = Math.Max(0, _selected - rows + 1);
            for (var i = first; i < Math.Min(_entries.Count, first + rows); i++)
                g.WriteString(2, 3 + i - first, (i == _selected ? "> " : "  ") + _entries[i].Name,
                    i == _selected ? Color.Cyan : Color.White, Color.Black);
            if (_entries.Count == 0) g.WriteString(2, 3, "No saved games.", Color.White, Color.Black);
            g.WriteString(2, Math.Max(0, height - 2), "Up/Down: select  Enter: load  Esc: back", Color.White, Color.Black);
        };
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Modifiers != 0) return;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: _selected = Math.Max(0, _selected - 1); break;
            case ConsoleKey.DownArrow: _selected = Math.Min(Math.Max(0, _entries.Count - 1), _selected + 1); break;
            case ConsoleKey.Home: _selected = 0; break;
            case ConsoleKey.End: _selected = Math.Max(0, _entries.Count - 1); break;
            case ConsoleKey.Enter: if (_entries.Count > 0) Selected?.Invoke(_entries[_selected]); return;
            case ConsoleKey.Escape: Cancelled?.Invoke(); return;
        }
        Refresh();
    }
}
