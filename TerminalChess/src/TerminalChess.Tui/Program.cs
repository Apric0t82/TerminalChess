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
        TerminalChess [--ascii] [--local | --stockfish <executable>] [--human white|black]
                         [--save <file.json>] [--load] (save directory; exact startup load path)
        Play bundled Stockfish by default; --local selects two local players.
        Engine: explicit path, STOCKFISH_PATH, bundled stockfish folder, then PATH.
        --stockfish auto uses automatic discovery. Human defaults to White.
        Choose Beginner/Intermediate/Advanced/Expert on the centered startup screen.
        Skill levels: 0/8/15/20; search times: 100/500/1500/3000 ms per move.
        Drag/click, or Arrows/WASD + Enter/Space. Promotion: Q/R/B/N or click.
        C claims a draw; F offers a draw before moving; Y/N answers; X resigns.
        Stockfish declines offers by moving; automatic draws still apply.
        Esc cancels selection or pauses engine; T retries; L switches to local play.
        F5 creates a save; F9 opens the game list. --load opens it at startup.
        Saves: application data / TerminalChess / game_MMDDYYYY_HHMMSS.json.
        R starts a new game; U toggles ASCII; Ctrl+Q quits. No typed UCI move input.
        Use an interactive ANSI terminal of at least 48 columns x 25 rows.
        """);
    return 0;
}

StartupOptions options;
try
{
    options = StartupOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"{ex.Message} Use --help for usage.");
    return 2;
}

SavedGame? saved = null;
JsonGameStore store;
GameSaveLibrary library;
try
{
    store = new JsonGameStore(options.SavePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerminalChess", "game.json"));
    library = new GameSaveLibrary(Path.GetDirectoryName(store.Path)!);
    if (options.Load && options.SavePath is not null) saved = options.ConfigureLoadedGame(await store.LoadAsync());
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
var executable = StockfishExecutable.Resolve(options.Stockfish);
using var lifetime = new CancellationTokenSource();
await using var session = new GameSession(new GeraGame(), new StockfishOpponent(executable),
    action => system.InvokeAsync(action), fen => new GeraGame(fen),
    options.EngineSide, saved?.Difficulty ?? OpponentDifficulty.Intermediate,
    deferOpponent: (saved is null ? options.EngineSide : saved.EngineSide) is not null);
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
using var board = new ChessBoardControl(interaction, options.Ascii);
session.Changed += board.Refresh;
var fileOperation = Task.CompletedTask;
board.SaveRequested += () => BeginPersistence(false);
using var selector = new DifficultySelectionControl(session.Difficulty);
var window = GameWindow.Create(system, session, board, selector);
board.LoadRequested += OpenGameList;
if (options.Load && options.SavePath is null) system.EnqueueOnUIThread(OpenGameList);
try { return system.Run(); }
finally
{
    lifetime.Cancel();
    session.Changed -= board.Refresh;
    await session.DisposeAsync();
    await fileOperation;
}

void OpenGameList()
{
    if (!fileOperation.IsCompleted) return;
    fileOperation = OpenAsync();
    async Task OpenAsync()
    {
        try
        {
            var entries = await Task.Run(library.List, lifetime.Token);
            await system.InvokeAsync(() =>
            {
                if (lifetime.IsCancellationRequested) return;
                var list = new GameLoadControl(entries);
                GameWindow.ShowLoadList(window, board, selector, list,
                    entry => BeginPersistence(true, entry.Path));
            }).WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { await NoticeAsync($"Load failed: {ex.Message}"); }
    }
}

void BeginPersistence(bool loading, string? loadPath = null)
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
                var document = await new JsonGameStore(loadPath ?? store.Path).LoadAsync(lifetime.Token).ConfigureAwait(false);
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
                await library.SaveAsync(snapshot, DateTime.Now, lifetime.Token).ConfigureAwait(false);
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
