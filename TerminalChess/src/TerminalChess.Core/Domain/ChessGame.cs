namespace TerminalChess.Core.Domain;

public enum PieceSide { White, Black }
public enum PieceKind { Pawn, Knight, Bishop, Rook, Queen, King }
public readonly record struct Piece(PieceSide Side, PieceKind Kind);
public readonly record struct MoveIntent(Square From, Square To, PieceKind? Promotion = null);
public enum MoveResult { Applied, Illegal, PromotionRequired, InvalidPromotion, GameOver }
public enum EndReason
{
    Checkmate, Stalemate, InsufficientMaterial, ThreefoldRepetition, FiftyMoveRule,
    FivefoldRepetition, SeventyFiveMoveRule, Agreement, Resignation
}
public sealed record GameOutcome(EndReason Reason, PieceSide? Winner = null);
public sealed record PositionSnapshot(string Fen, PieceSide Turn, bool IsCheck,
    GameOutcome? Outcome, long Revision, IReadOnlyDictionary<Square, Piece> Pieces);

/// <summary>The single game owner. Views consume values, never a rules-package board.</summary>
public interface IChessGame
{
    Piece? this[Square square] { get; }
    PieceSide Turn { get; }
    bool IsCheck { get; }
    GameOutcome? Outcome { get; }
    IReadOnlyList<MoveIntent> LegalMoves { get; }
    PositionSnapshot Snapshot { get; }
    bool CanClaimDraw { get; }
    PieceSide? DrawOffer { get; }
    bool CanMove(Square from, Square to);
    MoveResult TryMove(MoveIntent move);
    bool ClaimDraw(MoveIntent? intendedMove = null);
    bool OfferDraw();
    bool AcceptDraw();
    void DeclineDraw();
    void Resign();
    void Reset();
}
