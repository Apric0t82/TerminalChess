using Chess.Core.Domain;

namespace Chess.Core.Interaction;

/// <summary>Maps terminal cells, not pixels. Right and bottom edges are exclusive.</summary>
public sealed class BoardCoordinateMapper
{
    public int Left { get; }
    public int Top { get; }
    public int SquareWidth { get; }
    public int SquareHeight { get; }
    public int Width => SquareWidth * 8;
    public int Height => SquareHeight * 8;

    public BoardCoordinateMapper(int left, int top, int squareWidth, int squareHeight)
    {
        if (squareWidth <= 0) throw new ArgumentOutOfRangeException(nameof(squareWidth));
        if (squareHeight <= 0) throw new ArgumentOutOfRangeException(nameof(squareHeight));
        Left = left;
        Top = top;
        SquareWidth = squareWidth;
        SquareHeight = squareHeight;
    }

    public Square? Map(int x, int y)
    {
        // Test bounds before integer division: C# rounds negative division toward zero.
        if (x < Left || y < Top || x >= (long)Left + Width || y >= (long)Top + Height)
            return null;
        return new Square((x - Left) / SquareWidth, 7 - (y - Top) / SquareHeight);
    }

    public (int X, int Y) Origin(Square square) =>
        (Left + square.File * SquareWidth, Top + (7 - square.Rank) * SquareHeight);
}
