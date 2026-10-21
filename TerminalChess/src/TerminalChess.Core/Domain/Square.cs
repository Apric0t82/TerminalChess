namespace TerminalChess.Core.Domain;

/// <summary>File 0 is a; rank 0 is rank 1. White is displayed at the bottom.</summary>
public readonly record struct Square
{
    public int File { get; }
    public int Rank { get; }

    public Square(int file, int rank)
    {
        if (file is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(file));
        if (rank is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(rank));
        File = file;
        Rank = rank;
    }

    public override string ToString() => $"{(char)('a' + File)}{Rank + 1}";
}
