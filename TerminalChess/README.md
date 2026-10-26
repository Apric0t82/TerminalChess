# TerminalChess

Local two-player or Stockfish opponent chess in a cross-platform terminal, using **.NET 10**,
**SharpConsoleUI 2.7.4**, and **Gera.Chess 1.2.0**. Milestone 1 includes legal
turns/moves, castling, en passant, explicit promotion choice, check, checkmate,
stalemate, draw adjudication, resignation, and a new-game command. Milestone 2
adds asynchronous local Stockfish play and versioned JSON saving/loading.
Milestone 3 adds a startup opponent-level selector and persistent strength presets.

## Downloaded release: start and play

Extract the complete package for your OS/architecture, then launch
`TerminalChess.exe` on Windows or `./TerminalChess` on Linux/macOS in an ANSI
terminal. **No .NET or Stockfish installation is needed.** Keep the `stockfish`
folder beside the game executable. The package includes the .NET runtime and
Stockfish 19; playing makes no network requests or downloads.

The human plays White against Stockfish by default. Use `--human black` to let
Stockfish open, `--local` for two local players, or `--help` for controls. An
interactive terminal of at least **48 columns × 25 rows** is required.

The first screen is a centered selector on a black background, with a cyan title,
gold selection, and the selected engine settings below the four level descriptions.
Use Up/Down (or W/S), then Enter/Space to start. Mouse users can select a level
and click **Start game**. Intermediate is initially selected for a new game;
startup `--load` highlights that save's difficulty. No engine search begins before
confirmation, including when the human plays Black. `--local` skips the selector.

| Level | Description | Stockfish Skill Level | Search time per move |
| --- | --- | --- | --- |
| Beginner | Forgiving opponent, shallow tactics | 0 | 100 ms |
| Intermediate | Reasonable tactics, occasional mistakes | 8 | 500 ms |
| Advanced | Strong positional and tactical play | 15 | 1500 ms |
| Expert | Full-strength Stockfish | 20 | 3000 ms |

Expert applies no artificial weakening (`Skill Level 20`, `UCI_LimitStrength false`)
within its three-second search budget. These are skill/time presets, not Elo ratings.
The chosen level applies to each fresh engine process, survives R/new game, and is
saved with the game. F9 restores a save's difficulty without another startup screen.

The game is a self-contained single-file executable; Stockfish remains a
separate executable in the bundled folder, communicating through UCI. Preserve
the included sources, GPL license, notices, and `package.json` when redistributing.

## Build and run from source

From the repository root, with the .NET 10 SDK/runtime installed:

```sh
dotnet restore TerminalChess/TerminalChess.slnx
dotnet build TerminalChess/TerminalChess.slnx -c Release --no-restore
dotnet test TerminalChess/TerminalChess.slnx -c Release --no-build --no-restore
dotnet run --project TerminalChess/src/TerminalChess.Tui -c Release --no-build --no-restore
```

Append `-- --ascii` to start with ASCII pieces, or `-- --help` for usage. An
interactive ANSI terminal of at least **48 columns × 25 rows** is required.
Redirected input/output and `TERM=dumb` are rejected. The solution's local
`NuGet.Config` explicitly uses nuget.org.

Source builds do not download an engine. Use `-- --local` for two-player play,
or provide/discover an engine as described below. The release packaging command
performs the engine download during the build.

## Publish self-contained release packages

From the repository root, with PowerShell 7.2+ and the .NET 10 SDK:

```powershell
pwsh -NoProfile -File TerminalChess/packaging/Publish-Release.ps1 -RuntimeIdentifier win-x64
```

Supported targets: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`,
and `osx-arm64`. The default target is the host runtime identifier. The script
uses `Properties/PublishProfiles/Bundled.pubxml`, publishes Release without
trimming, and downloads a pinned official Stockfish 19 universal archive.
`packaging/stockfish.json` records each asset name and SHA-256 from its official
release metadata. Cached downloads are rechecked on every build; a checksum
mismatch stops packaging before extraction. Ordinary builds/tests never download Stockfish.

Packages and SHA-256 sidecars are written to
`artifacts/packages/TerminalChess-<rid>.zip` for Windows or `.tar.gz` for Unix.
The unpacked build lives under `artifacts/publish/<rid>/<build-id>/`. All engine
downloads/build outputs are ignored by Git. Official sources, build scripts,
documentation, GPL license, authors, embedded NNUE networks, and a provenance
notice travel with the engine; only its executable filename is normalized.
The package also retains .NET runtime notices, NuGet dependency metadata, and
license files supplied by those packages under `licenses/`.

Publishing for the host platform automatically runs `packaging/Test-Release.ps1`:
it verifies package files and the engine checksum, launches the game help from
an unrelated working directory with .NET lookup disabled, and runs a real UCI
handshake/search/shutdown. Cross-published packages need this smoke check on
their target OS/architecture. Unix packages should be built on Linux/macOS to
preserve executable permissions; the script uses tar for Unix archives.
The bundled game can extract native runtime libraries to the standard .NET
bundle cache on first launch. OS libraries required by .NET still apply.

## Stockfish opponent

Release packages discover the bundled engine automatically. To override it, or
to run a source checkout without a bundle, use a Stockfish executable from the
[official project](https://stockfishchess.org/download/) and pass its path:

```sh
dotnet run --project TerminalChess/src/TerminalChess.Tui -c Release --no-build --no-restore -- --stockfish "/path/to/stockfish"
```

On Windows the executable can be a quoted path such as
`"C:\Tools\Stockfish\stockfish.exe"`. On Linux/macOS ensure it has executable
permissions. The app invokes the executable directly without a shell. Keep any
files required by a custom engine distribution alongside its executable.

Automatic discovery (also selected by `--stockfish auto`) checks `STOCKFISH_PATH`,
then `stockfish/stockfish.exe` on Windows or `stockfish/stockfish` on Unix **beside
the game executable**, then `stockfish` on PATH. A supplied path/name takes
precedence. Launching from another working directory still finds the bundle.
Use `--local` to disable engine play. The human plays White by default; append
`--human black` to let Stockfish open as White. The board keeps White at the
bottom. Skill and search time follow the chosen level, with one engine thread
and 16 MiB of hash.

Each turn launches a fresh local UCI subprocess, completes `uci`/`isready`,
sends the selected `Skill Level`, disables Elo limiting, supplies the initial FEN
and accepted move history, and requests `go movetime` with the selected time.
This keeps engine repetition history and isolates searches. Startup, I/O, and
search run off the UI thread. The total engine deadline is the search time plus
10 seconds for startup/protocol overhead, followed
by at most 500 ms of cooperative shutdown before killing an unresponsive process
tree. No engine output is written over the TUI.

Gera validates every returned move, including promotions. During an engine turn,
human move, draw, and resignation commands are disabled. Stockfish declines draw
offers by moving; it does not offer/accept or claim optional draws. Automatic
draws still apply. Reset, loading, Escape, switching to local play, and exit
cancel pending searches; late results cannot change a superseded game. Missing
binaries, invalid moves, timeouts, and process exit leave the board intact and
show an error. **T** retries the engine turn; **L** continues with two local players.

## JSON persistence

**F5** saves the current committed game; **F9** loads it. The default file is
`TerminalChess/game.json` under .NET's per-user `ApplicationData` directory
(for example `%APPDATA%` on Windows). Use `--save` to choose a file explicitly
on any platform:

```sh
dotnet run --project TerminalChess/src/TerminalChess.Tui -c Release --no-build --no-restore -- --stockfish auto --save ./game.json
dotnet run --project TerminalChess/src/TerminalChess.Tui -c Release --no-build --no-restore -- --load --save ./game.json
```

Saving is explicit; quitting and restarting do not automatically save. The
version 2 JSON contains the initial FEN, opponent side (or local mode), difficulty, and an
ordered journal of accepted moves and draw/resignation actions. Replay restores
repetition, halfmove counts, draw offers, promotions, castling-right corrections,
and outcomes. Pending selection/drag/promotion and in-flight engine searches are
not saved. Loading an engine turn starts a fresh search. Startup `--load`
preserves the saved mode/side unless `--local` or `--human` explicitly overrides
it. F9 restores the saved mode/side. The executable still comes from automatic
discovery or `--stockfish` and is not stored in the save.
Existing version 1 saves without a difficulty still load and default to Intermediate;
the next save upgrades them to version 2.

Writes use a temporary sibling file followed by atomic replacement. JSON parsing
and isolated rules replay run asynchronously; a valid replay is transferred to
the UI owner before replacing the current game. Bad files, unsupported versions,
invalid actions, or failed writes display an error and preserve the current
game/previous save. If the position changes while a file is loading, the load is
discarded; F9 retries. Files over 4 MiB are rejected. This is a game-save format,
not a settings or credential store.

## Controls

| Input | Action |
| --- | --- |
| Drag a piece to a destination | Attempt a legal move for the side to move |
| Click source, then destination | Attempt the same move without dragging |
| Arrows or WASD | Move the keyboard cursor (initially e2) |
| Enter or Space | Select the cursor's piece or attempt the selected move |
| Q / R / B / N during promotion | Choose queen / rook / bishop / knight |
| Click a promotion choice in the footer | Choose the promotion piece with the mouse |
| Escape | Cancel selection, drag, or pending promotion |
| Escape during an engine search | Pause/cancel the search |
| T / L | Retry an engine turn / switch to local two-player play |
| F5 / F9 | Save / load the JSON game |
| C | Claim a draw in the current position; otherwise claim using the selected source and hovered/keyboard destination as the intended move |
| F | Offer a draw before moving; the offer is passed to the opponent with the move |
| Y / N | Accept / decline the opponent's offer on their turn |
| X | Resign for the side to move |
| R | Start a new game (during promotion, R chooses a rook; Escape then R starts a new game) |
| U | Toggle ASCII pieces |
| Ctrl+Q | Quit |

White is at the bottom. Only the side to move can be selected. Amber marks the
selected square; dots show **legal** destinations; teal/red mark valid/invalid
targets. The header shows whose turn it is, check, or the final result. An
illegal move leaves the position unchanged and permits another target.

Promotion is part of a single move: the pawn stays at its source until a choice
is made. Escape or a board-geometry change cancels the pending choice without
moving the pawn. Moves stop when a game ends; R starts a new game.

## Rules and draw policy

`GeraGame` owns the sole live Gera position and caches its generated legal moves
after transitions. `TerminalChess.Core` contains package-independent squares,
pieces, move intents, snapshots, and the interaction state machine. The TUI
contains drawing and SharpConsoleUI event translation. `TerminalChess.Rules`
contains the Gera adapter. `Core/Application/GameSession` owns serialized human
and engine transitions and the replayable game journal. `TerminalChess.Infrastructure`
contains UCI process I/O and JSON file persistence; the executable composes them
with the view.

The adapter disables Gera's optional automatic draw rules and derives repetition
metadata from accepted positions. It implements:

- Claimable threefold repetition and the 50-move rule (100 halfmoves), including
  valid intended-move claims. A successful intended-move claim ends the game
  without playing that move; an invalid claim leaves the position unchanged.
- Automatic fivefold repetition and the 75-move rule (150 halfmoves), with
  checkmate taking precedence on the last move.
- Repetition identity including side to move, castling rights, and an en passant
  target only when a legal en passant capture is possible.
- Stalemate and conservative material draws: kings only; king plus one bishop
  or knight versus king; and positions containing only kings and bishops all
  confined to the same square color. Two knights versus king is not automatically
  drawn, because cooperative mate remains possible.
- Draw agreement after both players have made at least one move. An opponent
  declines an outstanding offer by answering N or making a move. Resignation
  awards the other side the win; detected material draws already end the game.

**Limitation:** arbitrary dead positions, such as blocked positions containing
pawns where neither side can ever mate, are not generally recognized. The
material checks above are conservative; this is not a claim of exhaustive FIDE
dead-position adjudication. Clocks and timeout outcomes are not implemented.

The adapter also works around Gera's captured-rook castling-right issue. When
rights are lost, it rebases the library position through FEN to make that loss
permanent, preserving turn, en passant and move counters. Legality and pieces
continue to come from the library; no second board or move generator is kept.

Snapshots are immutable values. FEN-based construction is available to automated
fixtures; loading a FEN starts repetition history at that position because FEN
does not contain preceding positions. JSON saves replay the full accepted history
from their initial FEN instead of loading only the final FEN.

Typed UCI moves such as `e2e4` and `e7e8n`, LLM features,
and extra panels remain deferred. Move intents already carry source, destination,
and promotion independently of the input method.

## Terminal behavior and verification

The existing single-board UI was brought over from the retained SharpConsoleUI
prototype. Unicode symbols, ASCII fallback, adaptive 4–8-column by 2–4-row
squares, drag preview, click/keyboard controls, and Escape recovery are retained.
There is no piece-size toggle. The original
[framework API investigation](../ChessPrototype/README.md#sharpconsoleui-mouse-api-investigation)
documents the pinned package's raw mouse-event seam and capture behavior.

Mouse capture routes events within the application. Releases outside the
terminal can be lost; Escape cancels the pending gesture. Combined release and
motion flags and synthetic-click suppression are covered by tests. A stationary
pointer is never treated as a release. Rapid multi-click flags still have the
framework capture limitations documented in the prototype.

Automated tests cover rules, rejection consistency, draw boundaries, promotion,
interaction, terminal-cell mapping, canvas output, and headless framework input
routing. These do not establish real-terminal font appearance or OS mouse
behavior. Manual validation remains required on Windows, Linux, and macOS:

- Drag/click and keyboard play; both castling sides, en passant and all promotion
  choices; illegal targets; turn/check/result display; draws and restart.
- Release outside the board/window/terminal; Escape after a lost release; rapid
  repeated clicks and promotion-button release suppression.
- Resize during selection and promotion; 48×25 and 80×49 layouts; Unicode and
  ASCII rendering; terminal restoration on exit.
- Play both human colors against an installed Stockfish; pause/retry, restart
  while thinking, missing/terminated engine recovery, and quitting during search.
- F5/F9 and startup `--load`, including an engine-to-move save, repeated positions,
  completed games, malformed JSON, and an unwritable save path.

Automated engine tests launch the small `tests/FakeUciEngine` executable to
exercise UCI handshakes, history, promotions, stderr draining, failed/no moves,
startup/exit/timeouts, cancellation, and forced shutdown without requiring an
installed Stockfish. Session tests cover UI dispatch, input gating, reset/load
staleness, failure recovery, and disposal. Persistence tests cover replayed draw
history/outcomes, promotion, opponent mode, invalid files, cancellation, and
preserving the previous save. These controlled tests do not replace a manual
smoke test with a real Stockfish binary on each target platform.

The [library evaluation](../docs/chess-library-evaluation.md) records the choice
and upstream findings. Rules-package notices are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
