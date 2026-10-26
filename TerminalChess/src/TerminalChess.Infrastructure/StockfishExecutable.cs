namespace TerminalChess.Infrastructure;

public static class StockfishExecutable
{
    public static string Resolve(string? requested = null) => Resolve(requested,
        Environment.GetEnvironmentVariable("STOCKFISH_PATH"), AppContext.BaseDirectory,
        OperatingSystem.IsWindows(), File.Exists);

    public static string Resolve(string? requested, string? configured, string applicationDirectory,
        bool windows, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(requested) && requested != "auto") return requested;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        // AppContext.BaseDirectory stays app-relative for self-contained single-file deployments.
        // Never search the working directory: launching a release from another folder must work.
        var bundled = Path.Combine(applicationDirectory, "stockfish", windows ? "stockfish.exe" : "stockfish");
        return fileExists(bundled) ? bundled : "stockfish";
    }
}
