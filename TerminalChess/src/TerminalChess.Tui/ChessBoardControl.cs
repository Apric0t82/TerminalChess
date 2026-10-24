using TerminalChess.Core.Domain;
using TerminalChess.Core.Application;
using TerminalChess.Core.Interaction;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;

namespace TerminalChess.Tui;

/// <summary>
/// CanvasControl.ProcessMouseEvent is non-virtual and only exposes click/motion events.
/// Reimplementing IMouseAwareControl is the supported raw input seam. The framework
/// captures this control on Button1Pressed before dispatching to this interface.
/// </summary>
public sealed class ChessBoardControl : CanvasControl, IMouseAwareControl
{
    private static readonly Color Background = new(19, 27, 36);
    private static readonly Color Muted = new(166, 184, 197);
    private static readonly Color LightSquare = new(130, 147, 153);
    private static readonly Color DarkSquare = new(59, 83, 100);
    private static readonly Color Selection = new(160, 111, 36);
    private static readonly Color Destination = new(32, 119, 115);
    private static readonly Color InvalidTarget = new(140, 58, 63);
    private BoardCoordinateMapper? _mapper;
    private bool _suppressSyntheticClick;
    private bool _ascii;
    private int _promotionRow;
    private PieceKind? _promotionPressed;

    public ChessBoardInteraction Interaction { get; }
    public bool AsciiPieces => _ascii;
    public string PersistenceStatus { get; set; } = "";
    public event Action? SaveRequested;
    public event Action? LoadRequested;

    public ChessBoardControl(ChessBoardInteraction interaction, bool ascii = false) : base(80, 25)
    {
        Interaction = interaction;
        _ascii = ascii;
        AutoSize = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Fill;
        BackgroundColor = Background;
        Paint += (_, e) => Draw(e.Graphics, e.CanvasWidth, e.CanvasHeight);
        CanvasKeyPressed += (_, key) => HandleKey(key);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        _promotionPressed = null;
        var session = Interaction.Game as GameSession;
        if (key.Key == ConsoleKey.F5) { SaveRequested?.Invoke(); Refresh(); return; }
        if (key.Key == ConsoleKey.F9) { LoadRequested?.Invoke(); Refresh(); return; }
        PersistenceStatus = "";
        if (key.Key == ConsoleKey.T) { session?.StartOpponent(); Refresh(); return; }
        if (key.Key == ConsoleKey.L) { session?.UseLocalPlayers(); Refresh(); return; }
        if (key.Key == ConsoleKey.Escape && session?.IsThinking == true) session.CancelOpponent();
        if (Interaction.PendingPromotion is not null && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
            && key.Key is ConsoleKey.Q or ConsoleKey.R or ConsoleKey.B or ConsoleKey.N)
            Interaction.ChoosePromotion(key.Key switch
            {
                ConsoleKey.Q => PieceKind.Queen, ConsoleKey.R => PieceKind.Rook,
                ConsoleKey.B => PieceKind.Bishop, _ => PieceKind.Knight
            });
        else if (key.Key == ConsoleKey.U) _ascii = !_ascii;
        else
        {
            BoardCommand? command = key.Key switch
            {
                ConsoleKey.LeftArrow or ConsoleKey.A => BoardCommand.Left,
                ConsoleKey.RightArrow or ConsoleKey.D => BoardCommand.Right,
                ConsoleKey.UpArrow or ConsoleKey.W => BoardCommand.Up,
                ConsoleKey.DownArrow or ConsoleKey.S => BoardCommand.Down,
                ConsoleKey.Enter or ConsoleKey.Spacebar => BoardCommand.Activate,
                ConsoleKey.Escape => BoardCommand.Cancel,
                ConsoleKey.R => BoardCommand.Reset,
                ConsoleKey.C => BoardCommand.ClaimDraw,
                ConsoleKey.F => BoardCommand.OfferDraw,
                ConsoleKey.Y => BoardCommand.AcceptDraw,
                ConsoleKey.N => BoardCommand.DeclineDraw,
                ConsoleKey.X => BoardCommand.Resign,
                _ => null
            };
            if (command is { } action) Interaction.Command(action);
        }
        Refresh();
    }

    public new bool ProcessMouseEvent(MouseEventArgs args)
    {
        if (!IsEnabled) return false;
        if (args.HasAnyFlag(MouseFlags.Button1Pressed, MouseFlags.Button1Clicked)) PersistenceStatus = "";
        if (Interaction.PendingPromotion is not null && _mapper is not null
            && args.Position.Y - Margin.Top == _promotionRow)
        {
            var x = args.Position.X - Margin.Left;
            PieceKind? choice = x switch
            {
                >= 2 and < 11 => PieceKind.Queen, >= 11 and < 19 => PieceKind.Rook,
                >= 20 and < 30 => PieceKind.Bishop, >= 31 and < 41 => PieceKind.Knight, _ => null
            };
            if (args.HasFlag(MouseFlags.Button1Released))
            {
                if (choice is { } kind && _promotionPressed == kind) Interaction.ChoosePromotion(kind);
                _promotionPressed = null;
                _suppressSyntheticClick = true;
            }
            else if (args.HasFlag(MouseFlags.ReportMousePosition) || args.HasFlag(MouseFlags.Button1Dragged))
            {
                // As on the board, held motion must not become a new button press.
            }
            else if (args.HasAnyFlag(MouseFlags.Button1Pressed, MouseFlags.Button1DoubleClicked, MouseFlags.Button1TripleClicked))
            {
                _promotionPressed = choice;
                _suppressSyntheticClick = false;
            }
            else if (args.HasFlag(MouseFlags.Button1Clicked) && !_suppressSyntheticClick && choice is { } kind)
                Interaction.ChoosePromotion(kind);
            args.Handled = true;
            Refresh();
            return true;
        }
        // Preserve negative/out-of-control coordinates delivered by framework capture.
        var square = _mapper?.Map(args.Position.X - Margin.Left, args.Position.Y - Margin.Top);
        if (args.HasFlag(MouseFlags.Button1Released))
        {
            _promotionPressed = null;
            Interaction.Pointer(PointerAction.Up, square);
            _suppressSyntheticClick = true;
        }
        else if (args.HasFlag(MouseFlags.ReportMousePosition) || args.HasFlag(MouseFlags.Button1Dragged))
        {
            // SGR motion can include Button1Pressed. Motion must win over fresh-press handling.
            Interaction.Pointer(PointerAction.Move, square);
        }
        else if (args.HasAnyFlag(MouseFlags.Button1Pressed, MouseFlags.Button1DoubleClicked,
            MouseFlags.Button1TripleClicked))
        {
            // The Unix driver can replace a rapid subsequent press with a multi-click flag.
            _suppressSyntheticClick = false;
            Interaction.Pointer(PointerAction.Down, square);
        }
        else if (args.HasFlag(MouseFlags.Button1Clicked))
        {
            if (Interaction.IsPointerDown)
            {
                Interaction.Pointer(PointerAction.Up, square);
                _suppressSyntheticClick = true;
            }
            else if (!_suppressSyntheticClick) Interaction.Pointer(PointerAction.Click, square);
        }
        else return false;

        args.Handled = true;
        Refresh();
        return true;
    }

    private void Draw(CanvasGraphics graphics, int width, int height)
    {
        graphics.FillRect(0, 0, width, height, Background);
        var session = Interaction.Game as GameSession;
        graphics.WriteString(2, 0, session?.EngineSide is { } side
            ? $"TERMINAL CHESS / Stockfish: {side}" : "TERMINAL CHESS / local game", Color.White, Background);
        if (width < 48 || height < 25)
        {
            _promotionPressed = null;
            if (_mapper is not null) Interaction.Pointer(PointerAction.Cancel, null);
            _mapper = null;
            graphics.WriteString(2, 2, "Resize terminal to at least 48 x 25.", Muted, Background);
            graphics.WriteString(2, 4, "Ctrl+Q quits.", Muted, Background);
            return;
        }

        graphics.WriteString(2, 1, GameSummary(), Color.White, Background);
        if (!string.IsNullOrEmpty(PersistenceStatus))
            graphics.WriteString(2, 2, PersistenceStatus, Muted, Background);
        else if (session is not null && !string.IsNullOrEmpty(session.Status))
            graphics.WriteString(2, 2, session.Status, Muted, Background);
        else if (Interaction.Game.DrawOffer is { } offer)
            graphics.WriteString(2, 2, $"{offer} offers a draw. Opponent: Y/N", Muted, Background);
        else if (Interaction.Game.CanClaimDraw)
            graphics.WriteString(2, 2, "Draw available. C: claim or continue playing", Muted, Background);
        var squareHeight = Math.Clamp((height - 9) / 8 - 1, 2, 4);
        var squareWidth = Math.Clamp((width - 6) / 8 - 1, 4, squareHeight >= 3 ? 8 : 5);
        var mapper = new BoardCoordinateMapper((width - squareWidth * 8) / 2, 3, squareWidth, squareHeight);
        if (_mapper is { } old && (old.Left != mapper.Left || old.SquareWidth != mapper.SquareWidth
            || old.SquareHeight != mapper.SquareHeight))
        {
            _promotionPressed = null;
            Interaction.Pointer(PointerAction.Cancel, null);
        }
        _mapper = mapper;

        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var square = new Square(file, rank);
            var (x, y) = mapper.Origin(square);
            var bg = (file + rank) % 2 == 0 ? DarkSquare : LightSquare;
            if (Interaction.Selected == square) bg = Selection;
            if (Interaction.Target == square && Interaction.Selected is { } source && square != source)
                bg = Interaction.Game.CanMove(source, square) ? Destination : InvalidTarget;
            graphics.FillRect(x, y, squareWidth, squareHeight, bg);
            if (Interaction.Selected is { } from && Interaction.Game.CanMove(from, square))
                graphics.WriteString(x, y, ".", Muted, bg);

            var piece = Interaction.Game[square];
            if (Interaction.IsDragging && Interaction.Selected == square) piece = null;
            if (Interaction.IsDragging && Interaction.Target == square && Interaction.Selected is { } dragged)
                piece = Interaction.Game[dragged];
            if (piece is { } p)
            {
                graphics.WriteString(x + (squareWidth - 1) / 2, y + (squareHeight - 1) / 2,
                    PieceText(p), p.Side == PieceSide.White ? Color.White : new Color(8, 16, 24), bg);
            }
            if (Interaction.Cursor == square)
            {
                graphics.WriteString(x, y + squareHeight - 1, "[", Color.White, bg);
                graphics.WriteString(x + squareWidth - 1, y + squareHeight - 1, "]", Color.White, bg);
            }
        }

        for (var file = 0; file < 8; file++)
            graphics.WriteString(mapper.Left + file * squareWidth + (squareWidth - 1) / 2,
                mapper.Top + mapper.Height, ((char)('a' + file)).ToString(), Muted, Background);
        for (var rank = 0; rank < 8; rank++)
            graphics.WriteString(mapper.Left - 2, mapper.Top + (7 - rank) * squareHeight,
                (rank + 1).ToString(), Muted, Background);

        var footer = mapper.Top + mapper.Height + 2;
        _promotionRow = footer + 1;
        var status = session is not null && !string.IsNullOrEmpty(session.Status) ? session.Status : Interaction.Status;
        // Keep recovery keys visible at the minimum supported terminal width.
        if (session is { IsHumanTurn: false, IsThinking: false } && Interaction.Game.Outcome is null)
            status = "Engine paused/failed. T:retry L:local";
        graphics.WriteString(2, footer, status, Color.White, Background);
        graphics.WriteString(2, footer + 1, Interaction.PendingPromotion is not null
            ? "[Q queen][R rook] [B bishop] [N knight]"
            : "Drag/click; Arrows/WASD + Enter/Space", Muted, Background);
        graphics.WriteString(2, footer + 2, "F5:save F9:load C:claim F:draw Y/N:answer", Muted, Background);
        graphics.WriteString(2, footer + 3, "Esc:cancel R:new U:ASCII X:resign Ctrl+Q:quit", Muted, Background);
    }

    private string GameSummary()
    {
        var game = Interaction.Game;
        if (game.Outcome is not { } outcome)
            return $"{game.Turn} to move{(game.IsCheck ? " / CHECK" : "")}";
        if (outcome.Winner is { } winner)
            return $"{winner} wins / {outcome.Reason}. R: new game";
        var reason = outcome.Reason switch
        {
            EndReason.InsufficientMaterial => "insufficient material",
            EndReason.ThreefoldRepetition => "threefold repetition",
            EndReason.FivefoldRepetition => "fivefold repetition",
            EndReason.FiftyMoveRule => "50-move claim",
            EndReason.SeventyFiveMoveRule => "75-move rule",
            _ => outcome.Reason.ToString()
        };
        return $"Draw / {reason}. R: new game";
    }

    private string PieceText(Piece piece)
    {
        if (_ascii)
        {
            var letter = piece.Kind switch
            {
                PieceKind.Pawn => 'P', PieceKind.Knight => 'N', PieceKind.Bishop => 'B',
                PieceKind.Rook => 'R', PieceKind.Queen => 'Q', _ => 'K'
            };
            return (piece.Side == PieceSide.White ? letter : char.ToLowerInvariant(letter)).ToString();
        }
        return (piece.Side, piece.Kind) switch
        {
            (PieceSide.White, PieceKind.King) => "♔", (PieceSide.White, PieceKind.Queen) => "♕",
            (PieceSide.White, PieceKind.Rook) => "♖", (PieceSide.White, PieceKind.Bishop) => "♗",
            (PieceSide.White, PieceKind.Knight) => "♘", (PieceSide.White, PieceKind.Pawn) => "♙",
            (PieceSide.Black, PieceKind.King) => "♚", (PieceSide.Black, PieceKind.Queen) => "♛",
            (PieceSide.Black, PieceKind.Rook) => "♜", (PieceSide.Black, PieceKind.Bishop) => "♝",
            (PieceSide.Black, PieceKind.Knight) => "♞", _ => "♟"
        };
    }
}
