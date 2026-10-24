using System.Text;
using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;
using TerminalChess.Core.Interaction;
using TerminalChess.Infrastructure;
using TerminalChess.Rules;
using TerminalChess.Tui;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        TerminalChess.Tui [--ascii] [--stockfish <executable>] [--human white|black]
                         [--save <file.json>] [--load]
        Local two-player chess by default; --stockfish enables an engine opponent.
        Stockfish executable: explicit path/name, STOCKFISH_PATH, or stockfish on PATH.
        Use --stockfish auto to use STOCKFISH_PATH/PATH. Human defaults to White.
        Search limit: 500 ms per move. Engine binary is user-supplied, not bundled.
        Drag/click, or Arrows/WASD + Enter/Space. Promotion: Q/R/B/N or click.
        C claims a draw; F offers a draw before moving; Y/N answers; X resigns.
        Stockfish declines offers by moving; automatic draws still apply.
        Esc cancels selection or pauses engine; T retries; L switches to local play.
        F5 saves JSON; F9 loads it. --load resumes the save at startup.
        Default save: application data / TerminalChess / game.json. No automatic save.
        R starts a new game; U toggles ASCII; Ctrl+Q quits. No typed UCI move input.
        Use an interactive ANSI terminal of at least 48 columns x 25 rows.
        """);
    return 0;
}

var ascii = false;
var load = false;
string? engine = null;
var human = PieceSide.White;
var humanSpecified = false;
var savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerminalChess", "game.json");
try
{
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--ascii": ascii = true; break;
            case "--load": load = true; break;
            case "--stockfish": engine = Value(); break;
            case "--save": savePath = Value(); break;
            case "--human":
                humanSpecified = true;
                human = Value() switch
                {
                    "white" => PieceSide.White, "black" => PieceSide.Black,
                    _ => throw new ArgumentException("--human must be white or black.")
                };
                break;
            default: throw new ArgumentException($"Unknown argument: {args[i]}");
        }
        string Value() => ++i < args.Length && !string.IsNullOrWhiteSpace(args[i]) && !args[i].StartsWith("--")
            ? args[i] : throw new ArgumentException("Missing option value.");
    }
    if (humanSpecified && engine is null) throw new ArgumentException("--human requires --stockfish.");
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"{ex.Message} Use --help for usage.");
    return 2;
}

SavedGame? saved = null;
JsonGameStore store;
try
{
    store = new JsonGameStore(savePath);
    if (load) saved = await store.LoadAsync();
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Cannot open saved game: {ex.Message}");
    return 1;
}
if (Console.IsInputRedirected || Console.IsOutputRedirected
    || Environment.GetEnvironmentVariable("TERM") == "dumb")
{
    Console.Error.WriteLine("TerminalChess.Tui needs an interactive ANSI terminal. Run from a terminal, or use --help.");
    return 1;
}

Console.OutputEncoding = Encoding.UTF8;
var system = new ConsoleWindowSystem(new NetConsoleDriver(RenderMode.Buffer),
    options: new ConsoleWindowSystemOptions(ShowTopPanel: false, ShowBottomPanel: false));
var configuredEngine = Environment.GetEnvironmentVariable("STOCKFISH_PATH");
var executable = engine is not null and not "auto" ? engine
    : !string.IsNullOrWhiteSpace(configuredEngine) ? configuredEngine : "stockfish";
using var lifetime = new CancellationTokenSource();
await using var session = new GameSession(new GeraGame(), new StockfishOpponent(executable),
    action => system.InvokeAsync(action), fen => new GeraGame(fen),
    engine is null ? null : human == PieceSide.White ? PieceSide.Black : PieceSide.White);
try
{
    if (saved is not null) session.Load(saved);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Cannot restore saved game: {ex.Message}");
    return 1;
}
var interaction = new ChessBoardInteraction(session);
using var board = new ChessBoardControl(interaction, ascii);
session.Changed += board.Refresh;
var fileOperation = Task.CompletedTask;
board.SaveRequested += () => BeginPersistence(false);
board.LoadRequested += () => BeginPersistence(true);
var window = new WindowBuilder(system).WithTitle("TerminalChess")
    .Maximized().Frameless().Resizable(false).AddControl(board).Build();
system.AddWindow(window);
window.FocusManager.SetFocus(board, FocusReason.Programmatic);
system.EnqueueOnUIThread(session.StartOpponent);
try { return system.Run(); }
finally
{
    lifetime.Cancel();
    session.Changed -= board.Refresh;
    await session.DisposeAsync();
    await fileOperation;
}

void BeginPersistence(bool loading)
{
    if (!fileOperation.IsCompleted) return;
    var revision = session.Snapshot.Revision;
    var snapshot = session.Export();
    board.PersistenceStatus = loading ? "Loading game..." : "Saving game...";
    fileOperation = PersistAsync();
    async Task PersistAsync()
    {
        try
        {
            if (loading)
            {
                var document = await store.LoadAsync(lifetime.Token).ConfigureAwait(false);
                var prepared = await Task.Run(() => session.PrepareLoad(document, lifetime.Token), lifetime.Token).ConfigureAwait(false);
                await system.InvokeAsync(() =>
                {
                    if (lifetime.IsCancellationRequested) return;
                    if (session.Snapshot.Revision != revision)
                    {
                        board.PersistenceStatus = "Position changed during load. F9: retry";
                        board.Refresh();
                        return;
                    }
                    session.Load(prepared);
                    interaction.Command(BoardCommand.Cancel);
                    board.PersistenceStatus = "Game loaded. F5:save F9:load";
                    board.Refresh();
                }).WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
            else
            {
                await store.SaveAsync(snapshot, lifetime.Token).ConfigureAwait(false);
                await NoticeAsync("Game saved. F5:save F9:load");
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try { await NoticeAsync($"{(loading ? "Load" : "Save")} failed: {ex.Message}"); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }
}

async Task NoticeAsync(string message) => await system.InvokeAsync(() =>
{
    if (lifetime.IsCancellationRequested) return;
    board.PersistenceStatus = message;
    board.Refresh();
}).WaitAsync(lifetime.Token).ConfigureAwait(false);
