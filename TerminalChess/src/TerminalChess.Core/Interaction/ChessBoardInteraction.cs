using TerminalChess.Core.Domain;
using TerminalChess.Core.Application;

namespace TerminalChess.Core.Interaction;

public enum PointerAction { Down, Move, Up, Click, Cancel }
public enum BoardCommand { Left, Right, Up, Down, Activate, Cancel, Reset, ClaimDraw, OfferDraw, AcceptDraw, DeclineDraw, Resign }

/// <summary>UI-independent state machine shared by mouse, touch-like clicks and keyboard.</summary>
public sealed class ChessBoardInteraction(IChessGame game)
{
    private Square? _pressedSquare;
    private Square? _dragSource;
    public IChessGame Game { get; } = game;
    public MoveIntent? PendingPromotion { get; private set; }
    public Square? Selected { get; private set; }
    public Square? Target { get; private set; }
    public Square Cursor { get; private set; } = new(4, 1);
    public bool IsPointerDown { get; private set; }
    public bool IsDragging { get; private set; }
    public string Status { get; private set; } = "Ready. Select a piece.";

    public void Pointer(PointerAction action, Square? square)
    {
        if (Game is GameSession { IsHumanTurn: false } && action != PointerAction.Cancel) return;
        if (PendingPromotion is not null && action != PointerAction.Cancel) return;
        if (Game.Outcome is not null && action != PointerAction.Cancel) return;
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
                _dragSource = square is { } s && Game[s]?.Side == Game.Turn ? s : null;
                Target = square;
                if (square is { } down)
                {
                    Cursor = down;
                    if (Game[down]?.Side == Game.Turn)
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
        if (command == BoardCommand.Cancel) { Cancel(); return; }
        if (command == BoardCommand.Reset) { Game.Reset(); Cancel(); Status = "New game. White to move."; return; }
        if (Game is GameSession { IsHumanTurn: false }) return;
        if (PendingPromotion is not null)
        {
            if (command == BoardCommand.ClaimDraw)
            {
                // A promotion cannot establish repetition or a 50-move claim:
                // it is an irreversible pawn move. The current position can qualify.
                Status = Game.ClaimDraw() ? "Draw claimed." : "No draw claim. Choose Q/R/B/N or Esc.";
                if (Game.Outcome is not null) CancelSelection();
            }
            return;
        }
        if (Game.Outcome is not null) { Status = "Game over. R starts a new game."; return; }
        switch (command)
        {
            case BoardCommand.ClaimDraw:
                var intended = !Game.CanClaimDraw && Selected is { } source && Target is { } target
                    ? new MoveIntent(source, target) : (MoveIntent?)null;
                Status = Game.ClaimDraw(intended) ? "Draw claimed." : "No valid draw claim for this position/move.";
                if (Game.Outcome is not null) CancelSelection();
                return;
            case BoardCommand.OfferDraw:
                Status = Game.OfferDraw() ? "Draw offered. Make your move; opponent: Y/N." : "Draw offer unavailable.";
                return;
            case BoardCommand.AcceptDraw:
                Status = Game.AcceptDraw() ? "Draw agreed." : "No opponent draw offer to accept.";
                if (Game.Outcome is not null) CancelSelection();
                return;
            case BoardCommand.DeclineDraw: Game.DeclineDraw(); Status = "Draw offer declined."; return;
            case BoardCommand.Resign: Game.Resign(); CancelSelection(); Status = "Resigned. R starts a new game."; return;
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
        if (Game[square]?.Side == Game.Turn)
        {
            Selected = square;
            Status = $"Selected {square}. Choose a target.";
        }
        else if (Selected is { } from) AttemptMove(from, square);
        else Status = $"Select a {Game.Turn} piece.";
    }

    private void AttemptMove(Square from, Square to)
    {
        if (from == to) { Status = $"Selected {from}."; return; }
        var result = Game.TryMove(new(from, to));
        if (result == MoveResult.Applied)
        {
            Selected = null;
            Status = $"Moved {from} -> {to}.";
        }
        else if (result == MoveResult.PromotionRequired)
        {
            PendingPromotion = new(from, to);
            Status = "Promote: Q queen, R rook, B bishop, N knight.";
        }
        else Status = "Illegal move. Choose a legal target.";
    }

    public void ChoosePromotion(PieceKind kind)
    {
        if (PendingPromotion is not { } move) return;
        if (Game.TryMove(move with { Promotion = kind }) != MoveResult.Applied)
        {
            Status = "Choose Q, R, B or N for promotion.";
            return;
        }
        CancelSelection();
        Status = $"Promoted to {kind}.";
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
        CancelSelection();
        Status = "Selection cancelled.";
    }

    private void CancelSelection()
    {
        Selected = null;
        Target = null;
        PendingPromotion = null;
    }
}
