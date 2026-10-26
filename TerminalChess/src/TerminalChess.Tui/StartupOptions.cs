using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;

namespace TerminalChess.Tui;

public sealed record StartupOptions
{
    public bool Ascii { get; private init; }
    public bool Load { get; private init; }
    public bool Local { get; private init; }
    public PieceSide Human { get; private init; } = PieceSide.White;
    public bool HumanSpecified { get; private init; }
    public string? Stockfish { get; private init; }
    public string? SavePath { get; private init; }
    public PieceSide? EngineSide => Local ? null : Human == PieceSide.White ? PieceSide.Black : PieceSide.White;

    public SavedGame ConfigureLoadedGame(SavedGame saved) => Local || HumanSpecified
        ? saved with { EngineSide = EngineSide } : saved;

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions();
        for (var i = 0; i < args.Length; i++)
        {
            options = args[i] switch
            {
                "--ascii" => options with { Ascii = true },
                "--load" => options with { Load = true },
                "--local" => options with { Local = true },
                "--stockfish" => options with { Stockfish = Value() },
                "--save" => options with { SavePath = Value() },
                "--human" => options with
                {
                    HumanSpecified = true,
                    Human = Value() switch
                    {
                        "white" => PieceSide.White, "black" => PieceSide.Black,
                        _ => throw new ArgumentException("--human must be white or black.")
                    }
                },
                _ => throw new ArgumentException($"Unknown argument: {args[i]}")
            };
            string Value() => ++i < args.Length && !string.IsNullOrWhiteSpace(args[i]) && !args[i].StartsWith("--")
                ? args[i] : throw new ArgumentException("Missing option value.");
        }
        if (options.Local && (options.Stockfish is not null || options.HumanSpecified))
            throw new ArgumentException("--local cannot be combined with --stockfish or --human.");
        return options;
    }
}
