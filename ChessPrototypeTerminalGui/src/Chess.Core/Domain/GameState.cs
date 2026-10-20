namespace Chess.Core.Domain;

public enum PieceSide { White, Black }
public enum PieceKind { Pawn, Knight, Bishop, Rook, Queen, King }
public readonly record struct Piece(PieceSide Side, PieceKind Kind);

/// <summary>
/// Temporary arrangement board. Either side can move any piece to any empty or enemy square.
/// This deliberately does not implement chess legality, turns, check, or special moves.
/// Replace this model with an authoritative chess rules library before adding game play.
/// </summary>
public sealed class GameState
{
    private readonly Piece?[,] _pieces = new Piece?[8, 8];
    public Piece? this[Square square] => _pieces[square.File, square.Rank];

    public GameState() => Reset();

    public void Reset()
    {
        Array.Clear(_pieces);
        PieceKind[] backRank = [PieceKind.Rook, PieceKind.Knight, PieceKind.Bishop,
            PieceKind.Queen, PieceKind.King, PieceKind.Bishop, PieceKind.Knight, PieceKind.Rook];
        for (var file = 0; file < 8; file++)
        {
            _pieces[file, 0] = new(PieceSide.White, backRank[file]);
            _pieces[file, 1] = new(PieceSide.White, PieceKind.Pawn);
            _pieces[file, 6] = new(PieceSide.Black, PieceKind.Pawn);
            _pieces[file, 7] = new(PieceSide.Black, backRank[file]);
        }
    }

    public bool CanMove(Square from, Square to) => from != to && this[from] is { } piece
        && (this[to] is not { } occupant || occupant.Side != piece.Side);

    public bool TryMove(Square from, Square to)
    {
        if (!CanMove(from, to)) return false;
        _pieces[to.File, to.Rank] = this[from];
        _pieces[from.File, from.Rank] = null;
        return true;
    }
}
