using Chess.Core.Domain;

namespace Chess.Core.Interaction;

public enum PointerAction { Down, Move, Up, Click, Cancel }
public enum BoardCommand { Left, Right, Up, Down, Activate, Cancel, Reset }

/// <summary>UI-independent state machine shared by mouse, touch-like clicks and keyboard.</summary>
public sealed class ChessBoardInteraction(GameState game)
{
    private Square? _pressedSquare;
    private Square? _dragSource;
    public GameState Game { get; } = game;
    public Square? Selected { get; private set; }
    public Square? Target { get; private set; }
    public Square Cursor { get; private set; } = new(4, 1);
    public bool IsPointerDown { get; private set; }
    public bool IsDragging { get; private set; }
    public string Status { get; private set; } = "Ready. Select a piece.";

    public void Pointer(PointerAction action, Square? square)
    {
        switch (action)
        {
            case PointerAction.Cancel:
                Cancel();
                return;
            case PointerAction.Down:
                // Some drivers resend presses while held. Never restart the gesture.
                if (IsPointerDown) { UpdatePointer(square); return; }
                IsPointerDown = true;
                _pressedSquare = square;
                _dragSource = square is { } s && Game[s] is not null ? s : null;
                Target = square;
                if (square is { } down)
                {
                    Cursor = down;
                    if (Game[down] is { } piece && (Selected is not { } selected
                        || Game[selected]?.Side == piece.Side))
                        Selected = down;
                }
                Status = Selected is { } source ? $"Selected {source}. Choose a target." : "Select a piece.";
                return;
            case PointerAction.Move:
                UpdatePointer(square);
                return;
            case PointerAction.Up:
                // Ignore stray/duplicate releases rather than accidentally completing a second move.
                if (!IsPointerDown) return;
                UpdatePointer(square);
                var sourceSquare = _dragSource;
                var wasDragging = IsDragging;
                EndGesture();
                if (square is not { } destination) { Cancel(); return; }
                if (wasDragging)
                {
                    if (sourceSquare is { } from) AttemptMove(from, destination);
                }
                else Activate(destination);
                return;
            case PointerAction.Click:
                // Click-only input is a complete gesture. The adapter suppresses synthetic duplicates.
                EndGesture();
                if (square is { } clicked) { Cursor = clicked; Target = clicked; Activate(clicked); }
                else Cancel();
                return;
        }
    }

    public void Command(BoardCommand command)
    {
        // Switching to keyboard abandons any incomplete pointer gesture.
        EndGesture();
        switch (command)
        {
            case BoardCommand.Cancel: Cancel(); return;
            case BoardCommand.Reset:
                Game.Reset(); Cancel(); Status = "Board reset."; return;
            case BoardCommand.Activate: Activate(Cursor); break;
            case BoardCommand.Left: Cursor = new(Math.Max(0, Cursor.File - 1), Cursor.Rank); break;
            case BoardCommand.Right: Cursor = new(Math.Min(7, Cursor.File + 1), Cursor.Rank); break;
            case BoardCommand.Up: Cursor = new(Cursor.File, Math.Min(7, Cursor.Rank + 1)); break;
            case BoardCommand.Down: Cursor = new(Cursor.File, Math.Max(0, Cursor.Rank - 1)); break;
        }
        Target = Cursor;
    }

    private void UpdatePointer(Square? square)
    {
        Target = square;
        if (square is { } target) Cursor = target;
        if (IsPointerDown && square != _pressedSquare)
        {
            IsDragging = true;
            Selected = _dragSource;
            Status = _dragSource is not { } from ? "No piece at drag origin."
                : square is { } to ? $"Dragging {from} -> {to}" : "Release outside to cancel.";
        }
    }

    private void Activate(Square square)
    {
        if (Selected == square) return;
        if (Selected is { } from && Game.CanMove(from, square)) AttemptMove(from, square);
        else if (Game[square] is not null)
        {
            Selected = square;
            Status = $"Selected {square}. Choose a target.";
        }
        else Status = "Select a piece first.";
    }

    private void AttemptMove(Square from, Square to)
    {
        if (from == to) { Status = $"Selected {from}."; return; }
        if (Game.TryMove(from, to))
        {
            Selected = null;
            Status = $"Moved {from} -> {to}.";
        }
        else Status = "Target occupied by a friendly piece. Move cancelled.";
    }

    private void EndGesture()
    {
        IsPointerDown = false;
        IsDragging = false;
        _pressedSquare = null;
        _dragSource = null;
    }

    private void Cancel()
    {
        EndGesture();
        Selected = null;
        Target = null;
        Status = "Selection cancelled.";
    }
}
