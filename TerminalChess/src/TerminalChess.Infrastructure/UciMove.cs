using TerminalChess.Core.Domain;

namespace TerminalChess.Infrastructure;

public static class UciMove
{
    public static MoveIntent Parse(string value)
    {
        if (value.Length is not (4 or 5) || value[0] is < 'a' or > 'h' || value[2] is < 'a' or > 'h'
            || value[1] is < '1' or > '8' || value[3] is < '1' or > '8')
            throw new InvalidDataException("Engine did not return a valid move.");
        PieceKind? promotion = value.Length == 4 ? null : value[4] switch
        {
            'q' => PieceKind.Queen, 'r' => PieceKind.Rook, 'b' => PieceKind.Bishop, 'n' => PieceKind.Knight,
            _ => throw new InvalidDataException("Engine returned an invalid promotion.")
        };
        return new(new(value[0] - 'a', value[1] - '1'), new(value[2] - 'a', value[3] - '1'), promotion);
    }

    public static string Format(MoveIntent move) => $"{move.From}{move.To}" + (move.Promotion switch
    {
        null => "", PieceKind.Queen => "q", PieceKind.Rook => "r", PieceKind.Bishop => "b", PieceKind.Knight => "n",
        _ => throw new InvalidDataException("Invalid promotion.")
    });
}
