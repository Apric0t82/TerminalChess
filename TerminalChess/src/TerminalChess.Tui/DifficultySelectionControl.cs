using TerminalChess.Core.Application;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;

namespace TerminalChess.Tui;

/// <summary>A centered startup selector; it never starts an opponent before explicit confirmation.</summary>
public sealed class DifficultySelectionControl : CanvasControl, IMouseAwareControl
{
    private static readonly Color Accent = new(102, 218, 214);
    private static readonly Color Gold = new(255, 211, 117);
    private static readonly Color Text = new(191, 205, 219);
    private static readonly Color Muted = new(139, 160, 179);
    private static readonly Color Highlight = new(14, 43, 48);
    private int _selected;
    private int? _pressed;
    private bool _suppressClick;
    private bool _confirmed;
    private int _left, _top, _width;
    private int _lastWidth, _lastHeight;
    private bool _layoutReady;

    public OpponentDifficulty SelectedDifficulty => OpponentStrength.Levels[_selected].Difficulty;
    public event Action<OpponentDifficulty>? Confirmed;

    public DifficultySelectionControl(OpponentDifficulty initial = OpponentDifficulty.Intermediate) : base(80, 25)
    {
        _ = OpponentStrength.For(initial);
        _selected = (int)initial;
        AutoSize = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Fill;
        BackgroundColor = Color.Black;
        Paint += (_, e) => Draw(e.Graphics, e.CanvasWidth, e.CanvasHeight);
        CanvasKeyPressed += (_, key) => HandleKey(key);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_confirmed || (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0) return;
        _pressed = null;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.W: _selected = Math.Max(0, _selected - 1); break;
            case ConsoleKey.DownArrow or ConsoleKey.S: _selected = Math.Min(3, _selected + 1); break;
            case ConsoleKey.Home: _selected = 0; break;
            case ConsoleKey.End: _selected = 3; break;
            case ConsoleKey.Enter or ConsoleKey.Spacebar:
                if (_layoutReady) Confirm();
                return;
        }
        Refresh();
    }

    private void Confirm()
    {
        if (_confirmed) return;
        _confirmed = true;
        Confirmed?.Invoke(SelectedDifficulty);
    }

    public new bool ProcessMouseEvent(MouseEventArgs args)
    {
        if (!IsEnabled || _confirmed || !_layoutReady) return false;
        var x = args.Position.X - Margin.Left;
        var y = args.Position.Y - Margin.Top;
        var hit = Hit(x, y);
        // As on the board, release wins over motion and press for combined terminal flags.
        if (args.HasFlag(MouseFlags.Button1Released))
        {
            if (hit is { } target && _pressed == target) Activate(target);
            _pressed = null;
            _suppressClick = true;
        }
        else if (args.HasAnyFlag(MouseFlags.ReportMousePosition, MouseFlags.Button1Dragged)) { }
        else if (args.HasAnyFlag(MouseFlags.Button1Pressed, MouseFlags.Button1DoubleClicked, MouseFlags.Button1TripleClicked))
        {
            _pressed = hit;
            _suppressClick = false;
            if (hit is >= 0 and < 4) _selected = hit.Value;
        }
        else if (args.HasFlag(MouseFlags.Button1Clicked))
        {
            if (!_suppressClick && hit is { } target) Activate(target);
        }
        else return false;
        args.Handled = true;
        if (!_confirmed) Refresh();
        return true;
    }

    private int? Hit(int x, int y)
    {
        if (x < _left || x >= _left + _width) return null;
        for (var index = 0; index < 4; index++)
            if (y >= _top + 3 + index * 3 && y < _top + 5 + index * 3) return index;
        var buttonLeft = _left + (_width - 14) / 2;
        return y == _top + 18 && x >= buttonLeft && x < buttonLeft + 14 ? 4 : null;
    }

    private void Activate(int target)
    {
        if (target == 4) Confirm();
        else _selected = target;
    }

    private void Draw(CanvasGraphics graphics, int width, int height)
    {
        graphics.FillRect(0, 0, width, height, Color.Black);
        if (_lastWidth != width || _lastHeight != height) _pressed = null;
        _lastWidth = width;
        _lastHeight = height;
        _layoutReady = width >= 48 && height >= 25;
        if (!_layoutReady)
        {
            graphics.WriteString(2, Math.Max(0, height / 2), "Resize terminal to at least 48 x 25.", Accent, Color.Black);
            return;
        }
        const int blockHeight = 21;
        _width = Math.Min(68, width - 4);
        _left = (width - _width) / 2;
        _top = (height - blockHeight) / 2;
        Center("Select AI opponent level", _top, Accent);
        for (var index = 0; index < 4; index++)
        {
            var strength = OpponentStrength.Levels[index];
            var row = _top + 3 + index * 3;
            var selected = _selected == index;
            var background = selected ? Highlight : Color.Black;
            graphics.FillRect(_left, row, _width, 2, background);
            graphics.WriteString(_left + 1, row, selected ? ">" : " ", Gold, background);
            graphics.WriteString(_left + 3, row, strength.Difficulty.ToString(), selected ? Gold : Text, background);
            graphics.WriteString(_left + 3, row + 1, strength.Description, selected ? Text : Muted, background);
        }
        var chosen = OpponentStrength.For(SelectedDifficulty);
        Center($"Stockfish Skill Level: {chosen.SkillLevel}", _top + 16, Accent);
        Center($"Search time per move: {chosen.MoveTimeMs} ms", _top + 17, Text);
        Center("[ Start game ]", _top + 18, Gold);
        Center("Up/Down: choose   Enter: play   Ctrl+Q: quit", _top + 20, Muted);

        void Center(string text, int row, Color color) =>
            graphics.WriteString((width - text.Length) / 2, row, text, color, Color.Black);
    }
}
