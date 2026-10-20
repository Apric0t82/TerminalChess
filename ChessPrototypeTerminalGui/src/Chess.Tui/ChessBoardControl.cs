using Chess.Core.Domain;
using Chess.Core.Interaction;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Chess.Tui;

/// <summary>A single custom Terminal.Gui view; framework events stop at this boundary.</summary>
public sealed class ChessBoardControl : View
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

    public ChessBoardInteraction Interaction { get; }
    public bool AsciiPieces => _ascii;

    public ChessBoardControl(ChessBoardInteraction interaction, bool ascii = false)
    {
        Interaction = interaction;
        _ascii = ascii;
        CanFocus = true;
        MousePositionTracking = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            ReleaseMouse();
            App?.RequestStop();
            return true;
        }
        if (key.IsCtrl || key.IsAlt) return false;
        if (key.NoShift == Key.U) _ascii = !_ascii;
        else
        {
            BoardCommand? command = key.NoShift.KeyCode switch
            {
                KeyCode.CursorLeft or KeyCode.A => BoardCommand.Left,
                KeyCode.CursorRight or KeyCode.D => BoardCommand.Right,
                KeyCode.CursorUp or KeyCode.W => BoardCommand.Up,
                KeyCode.CursorDown or KeyCode.S => BoardCommand.Down,
                KeyCode.Enter or KeyCode.Space => BoardCommand.Activate,
                KeyCode.Esc => BoardCommand.Cancel,
                KeyCode.R => BoardCommand.Reset,
                _ => null
            };
            if (command is not { } action) return false;
            ReleaseMouse();
            Interaction.Command(action);
        }
        key.Handled = true;
        SetNeedsDraw();
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        UpdateMapper();
        var flags = mouse.Flags;
        var position = mouse.Position;
        var square = position is { } point ? _mapper?.Map(point.X, point.Y) : null;
        if (flags.HasFlag(MouseFlags.LeftButtonReleased))
        {
            Interaction.Pointer(PointerAction.Up, square);
            _suppressSyntheticClick = true;
            ReleaseMouse();
        }
        else if (flags.HasFlag(MouseFlags.PositionReport))
        {
            // SGR reports a held button as Pressed | PositionReport. Do not start a new drag.
            Interaction.Pointer(PointerAction.Move, square);
        }
        else if (flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            if (_mapper is null) return true;
            SetFocus();
            _suppressSyntheticClick = false;
            Interaction.Pointer(PointerAction.Down, square);
            // Low-level events are handled here before the framework's auto-grab stage.
            // Explicitly grab and release through the public v2.5 API instead.
            App?.Mouse.GrabMouse(this);
        }
        else if ((flags & (MouseFlags.LeftButtonClicked | MouseFlags.LeftButtonDoubleClicked
            | MouseFlags.LeftButtonTripleClicked)) != 0)
        {
            if (Interaction.IsPointerDown)
            {
                Interaction.Pointer(PointerAction.Up, square);
                _suppressSyntheticClick = true;
            }
            else if (!_suppressSyntheticClick) Interaction.Pointer(PointerAction.Click, square);
            ReleaseMouse();
        }
        else return false;

        mouse.Handled = true;
        SetNeedsDraw();
        return true;
    }

    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        UpdateMapper();
    }

    private void UpdateMapper()
    {
        var width = Viewport.Width;
        var height = Viewport.Height;
        BoardCoordinateMapper? next = null;
        if (width >= 48 && height >= 25)
        {
            var squareHeight = Math.Clamp((height - 9) / 8 - 1, 2, 4);
            var squareWidth = Math.Clamp((width - 6) / 8 - 1, 4, squareHeight >= 3 ? 8 : 5);
            next = new((width - squareWidth * 8) / 2, 3, squareWidth, squareHeight);
        }
        if (_mapper is { } old && (next is null || old.Left != next.Left
            || old.SquareWidth != next.SquareWidth || old.SquareHeight != next.SquareHeight))
        {
            Interaction.Pointer(PointerAction.Cancel, null);
            ReleaseMouse();
        }
        _mapper = next;
    }

    private void ReleaseMouse()
    {
        if (App?.Mouse.IsGrabbed(this) == true) App.Mouse.UngrabMouse();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseMouse();
        base.Dispose(disposing);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        UpdateMapper();
        Fill(0, 0, Viewport.Width, Viewport.Height, Background);
        Write(2, 0, "CHESS / board prototype", Color.White, Background);
        if (_mapper is not { } mapper)
        {
            Write(2, 2, "Resize terminal to at least 48 x 25.", Muted, Background);
            Write(2, 4, "Ctrl+Q quits.", Muted, Background);
            return true;
        }
        Write(2, 1, "Arrangement mode: no chess rules or turns", Muted, Background);
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var square = new Square(file, rank);
            var (x, y) = mapper.Origin(square);
            var bg = (file + rank) % 2 == 0 ? DarkSquare : LightSquare;
            if (Interaction.Selected == square) bg = Selection;
            if (Interaction.Target == square && Interaction.Selected is { } source && square != source)
                bg = Interaction.Game.CanMove(source, square) ? Destination : InvalidTarget;
            Fill(x, y, mapper.SquareWidth, mapper.SquareHeight, bg);
            if (Interaction.Selected is { } from && Interaction.Game.CanMove(from, square))
                Write(x, y, ".", Muted, bg);
            var piece = Interaction.Game[square];
            if (Interaction.IsDragging && Interaction.Selected == square) piece = null;
            if (Interaction.IsDragging && Interaction.Target == square && Interaction.Selected is { } dragged)
                piece = Interaction.Game[dragged];
            if (piece is { } p)
                Write(x + (mapper.SquareWidth - 1) / 2, y + (mapper.SquareHeight - 1) / 2,
                    PieceText(p), p.Side == PieceSide.White ? Color.White : new Color(8, 16, 24), bg);
            if (Interaction.Cursor == square)
            {
                Write(x, y + mapper.SquareHeight - 1, "[", Color.White, bg);
                Write(x + mapper.SquareWidth - 1, y + mapper.SquareHeight - 1, "]", Color.White, bg);
            }
        }
        for (var file = 0; file < 8; file++)
            Write(mapper.Left + file * mapper.SquareWidth + (mapper.SquareWidth - 1) / 2,
                mapper.Top + mapper.Height, ((char)('a' + file)).ToString(), Muted, Background);
        for (var rank = 0; rank < 8; rank++)
            Write(mapper.Left - 2, mapper.Top + (7 - rank) * mapper.SquareHeight,
                (rank + 1).ToString(), Muted, Background);
        var footer = mapper.Top + mapper.Height + 2;
        Write(2, footer, Interaction.Status, Color.White, Background);
        Write(2, footer + 1, "Drag or click source, then destination", Muted, Background);
        Write(2, footer + 2, "Arrows/WASD: cursor  Enter/Space: select", Muted, Background);
        Write(2, footer + 3, "Esc: cancel  R: reset  U: ASCII  Ctrl+Q: quit", Muted, Background);
        return true;
    }

    private void Write(int x, int y, string text, Color foreground, Color background)
    {
        if (x < 0 || y < 0 || x >= Viewport.Width || y >= Viewport.Height) return;
        SetAttribute(new Attribute(foreground, background));
        AddStr(x, y, text[..Math.Min(text.Length, Viewport.Width - x)]);
    }

    private void Fill(int x, int y, int width, int height, Color background)
    {
        var line = new string(' ', width);
        for (var row = 0; row < height; row++) Write(x, y + row, line, Color.White, background);
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
