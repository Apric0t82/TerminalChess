using System.Collections.ObjectModel;
using Chess;
using TerminalChess.Core.Domain;
using Piece = TerminalChess.Core.Domain.Piece;
using Square = TerminalChess.Core.Domain.Square;

namespace TerminalChess.Rules;

/// <summary>
/// Owns the only live position. Legal moves come from Gera; draw metadata is derived
/// from accepted positions because Gera 1.2.0's optional draw rules are incomplete.
/// All calls belong to the application's serialized input thread.
/// </summary>
public sealed class GeraGame : IChessGame
{
    private ChessBoard _board = null!;
    private Dictionary<MoveIntent, Move> _moves = [];
    private readonly Dictionary<string, int> _repetitions = [];
    private string _positionKey = "";
    private long _revision;
    private int _halfMoves;
    private int _movesPlayed;

    public Piece? this[Square square] => ConvertPiece(_board[square.File, square.Rank]);
    public PieceSide Turn => _board.Turn == PieceColor.White ? PieceSide.White : PieceSide.Black;
    public bool IsCheck => Turn == PieceSide.White ? _board.WhiteKingChecked : _board.BlackKingChecked;
    public GameOutcome? Outcome { get; private set; }
    public IReadOnlyList<MoveIntent> LegalMoves { get; private set; } = Array.Empty<MoveIntent>();
    public PositionSnapshot Snapshot { get; private set; } = null!;
    public PieceSide? DrawOffer { get; private set; }
    public bool CanClaimDraw => Outcome is null && (_halfMoves >= 100 || _repetitions[_positionKey] >= 3);

    public GeraGame() => Reset();

    /// <summary>Starts from a valid chess position; FEN contains no earlier repetition history.</summary>
    public GeraGame(string fen) => Start(ChessBoard.LoadFromFen(fen, AutoEndgameRules.None));

    public void Reset() => Start(new ChessBoard { AutoEndgameRules = AutoEndgameRules.None });

    private void Start(ChessBoard board)
    {
        _board = board;
        _board.StandardiseCastlingPositions = true;
        Outcome = null;
        DrawOffer = null;
        _movesPlayed = 0;
        _repetitions.Clear();
        UpdatePosition();
    }

    public bool CanMove(Square from, Square to) => Outcome is null
        && LegalMoves.Any(move => move.From == from && move.To == to);

    public MoveResult TryMove(MoveIntent intent)
    {
        if (Outcome is not null) return MoveResult.GameOver;
        if (intent.Promotion is { } kind && kind is not (PieceKind.Queen or PieceKind.Rook or PieceKind.Bishop or PieceKind.Knight))
            return MoveResult.InvalidPromotion;
        if (!_moves.TryGetValue(intent, out var move))
        {
            if (intent.Promotion is null && CanMove(intent.From, intent.To)) return MoveResult.PromotionRequired;
            return MoveResult.Illegal;
        }

        // These are library-generated moves, including their explicit promotion choice.
        // Passing the generated object back preserves underpromotion during revalidation.
        if (!_board.Move(move)) return MoveResult.Illegal;
        PreserveCastlingRights();
        if (DrawOffer == Turn) DrawOffer = null; // A recipient's move declines an outstanding offer.
        _movesPlayed++;
        UpdatePosition();
        return MoveResult.Applied;
    }

    private void PreserveCastlingRights()
    {
        var fields = _board.ToFen().Split(' ');
        var previousRights = Snapshot.Fen.Split(' ')[2];
        var rights = new string(fields[2].Where(right =>
        {
            if (right == '-' || !previousRights.Contains(right)) return false;
            var side = char.IsUpper(right) ? PieceSide.White : PieceSide.Black;
            var rank = side == PieceSide.White ? 0 : 7;
            var file = char.ToUpperInvariant(right) == 'K' ? 7 : 0;
            return this[new Square(4, rank)] == new Piece(side, PieceKind.King)
                && this[new Square(file, rank)] == new Piece(side, PieceKind.Rook);
        }).ToArray());
        var canonicalRights = rights.Length == 0 ? "-" : rights;
        if (canonicalRights == fields[2] && canonicalRights == previousRights) return;
        fields[2] = canonicalRights;
        // Gera 1.2.0 can retain a captured rook's FEN right and later allow a
        // replacement rook to castle. Rebase whenever rights are lost, making the
        // loss permanent in the library's FEN state before a replacement can arrive.
        // The replacement library position retains turn, EP and move counters;
        // there is still one authoritative board and no application move generator.
        _board = ChessBoard.LoadFromFen(string.Join(' ', fields), AutoEndgameRules.None);
        _board.StandardiseCastlingPositions = true;
    }

    public bool ClaimDraw(MoveIntent? intendedMove = null)
    {
        if (Outcome is not null) return false;
        var count = _repetitions[_positionKey];
        var halfMoves = _halfMoves;
        if (intendedMove is { } intent)
        {
            if (!_moves.ContainsKey(intent)) return false;
            // A short-lived analysis position is discarded; a claim never commits a move.
            var analysis = new GeraGame(Snapshot.Fen);
            if (analysis.TryMove(intent) != MoveResult.Applied) return false;
            halfMoves = analysis._halfMoves;
            count = _repetitions.GetValueOrDefault(analysis._positionKey) + 1;
        }
        if (count < 3 && halfMoves < 100) return false;
        Finish(new(count >= 3 ? EndReason.ThreefoldRepetition : EndReason.FiftyMoveRule));
        return true;
    }

    public bool OfferDraw()
    {
        if (Outcome is not null || _movesPlayed < 2 || DrawOffer is not null) return false;
        DrawOffer = Turn;
        return true;
    }

    public bool AcceptDraw()
    {
        if (Outcome is not null || DrawOffer is null || DrawOffer == Turn) return false;
        Finish(new(EndReason.Agreement));
        return true;
    }

    public void DeclineDraw()
    {
        if (DrawOffer != Turn) DrawOffer = null;
    }

    public void Resign()
    {
        if (Outcome is null) Finish(new(EndReason.Resignation, Turn == PieceSide.White ? PieceSide.Black : PieceSide.White));
    }

    private void UpdatePosition()
    {
        _moves = GenerateMoves(_board);
        var fen = _board.ToFen();
        var fields = fen.Split(' ');
        _halfMoves = int.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture);
        // Repetition identity includes an EP target only if a legal EP capture exists.
        var enPassant = _moves.Values.Any(move => move.IsEnPassant) ? fields[3] : "-";
        _positionKey = $"{fields[0]} {fields[1]} {fields[2]} {enPassant}";
        var count = _repetitions.GetValueOrDefault(_positionKey) + 1;
        _repetitions[_positionKey] = count;

        // Checkmate takes precedence over a 75-move draw on the same move.
        if (_moves.Count == 0)
            Outcome = IsCheck ? new(EndReason.Checkmate, Turn == PieceSide.White ? PieceSide.Black : PieceSide.White)
                : new(EndReason.Stalemate);
        else if (HasInsufficientMaterial()) Outcome = new(EndReason.InsufficientMaterial);
        else if (count >= 5) Outcome = new(EndReason.FivefoldRepetition);
        else if (_halfMoves >= 150) Outcome = new(EndReason.SeventyFiveMoveRule);

        Publish(fen);
    }

    private static Dictionary<MoveIntent, Move> GenerateMoves(ChessBoard board)
    {
        var result = new Dictionary<MoveIntent, Move>();
        for (short rank = 0; rank < 8; rank++)
        for (short file = 0; file < 8; file++)
        {
            if (board[file, rank]?.Color != board.Turn) continue;
            // The all-pieces overload starts tasks and waits. Per-piece generation is
            // sequential and avoids synchronization-context callbacks from worker tasks.
            foreach (var move in board.Moves(new Position(file, rank), generateSan: false))
            {
                var intent = new MoveIntent(new(move.OriginalPosition.X, move.OriginalPosition.Y),
                    new(move.NewPosition.X, move.NewPosition.Y), ConvertPiece(move.Promotion)?.Kind);
                result.TryAdd(intent, move);
            }
        }
        return result;
    }

    private bool HasInsufficientMaterial()
    {
        var minorPieces = new List<(Piece Piece, Square Square)>();
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var square = new Square(file, rank);
            if (this[square] is not { } piece || piece.Kind == PieceKind.King) continue;
            if (piece.Kind is PieceKind.Pawn or PieceKind.Rook or PieceKind.Queen) return false;
            minorPieces.Add((piece, square));
        }
        if (minorPieces.Count <= 1) return true;
        // All bishops on one square color cannot mate. Two knights vs king can
        // reach mate with cooperation, so inability to force mate is not a draw.
        return minorPieces.All(p => p.Piece.Kind == PieceKind.Bishop)
            && minorPieces.Select(p => (p.Square.File + p.Square.Rank) % 2).Distinct().Count() == 1;
    }

    private void Finish(GameOutcome outcome)
    {
        Outcome = outcome;
        DrawOffer = null;
        Publish(Snapshot.Fen);
    }

    private void Publish(string fen)
    {
        if (Outcome is not null) { _moves.Clear(); DrawOffer = null; }
        LegalMoves = Array.AsReadOnly(_moves.Keys.ToArray());
        var pieces = new Dictionary<Square, Piece>();
        for (var rank = 0; rank < 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var square = new Square(file, rank);
            if (this[square] is { } piece) pieces.Add(square, piece);
        }
        Snapshot = new(fen, Turn, IsCheck, Outcome, ++_revision, new ReadOnlyDictionary<Square, Piece>(pieces));
    }

    private static Piece? ConvertPiece(Chess.Piece? piece)
    {
        if (piece is null) return null;
        var kind = char.ToLowerInvariant(piece.ToFenChar()) switch
        {
            'p' => PieceKind.Pawn, 'n' => PieceKind.Knight, 'b' => PieceKind.Bishop,
            'r' => PieceKind.Rook, 'q' => PieceKind.Queen, 'k' => PieceKind.King,
            _ => throw new InvalidOperationException("Unexpected rules-library piece.")
        };
        return new(piece.Color == PieceColor.White ? PieceSide.White : PieceSide.Black, kind);
    }
}
