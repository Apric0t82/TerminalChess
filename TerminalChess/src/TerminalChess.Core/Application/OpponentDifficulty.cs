namespace TerminalChess.Core.Application;

public enum OpponentDifficulty { Beginner, Intermediate, Advanced, Expert }

/// <summary>Application policy shared by the selector, saves, and opponent requests.</summary>
public sealed record OpponentStrength(OpponentDifficulty Difficulty, string Description, int SkillLevel, int MoveTimeMs)
{
    public static IReadOnlyList<OpponentStrength> Levels { get; } = Array.AsReadOnly(new[]
    {
        new OpponentStrength(OpponentDifficulty.Beginner, "Forgiving opponent, shallow tactics", 0, 100),
        new OpponentStrength(OpponentDifficulty.Intermediate, "Reasonable tactics, occasional mistakes", 8, 500),
        new OpponentStrength(OpponentDifficulty.Advanced, "Strong positional and tactical play", 15, 1500),
        new OpponentStrength(OpponentDifficulty.Expert, "Full-strength Stockfish", 20, 3000)
    });

    public static OpponentStrength For(OpponentDifficulty difficulty) => Enum.IsDefined(difficulty)
        ? Levels[(int)difficulty] : throw new ArgumentOutOfRangeException(nameof(difficulty));
}
