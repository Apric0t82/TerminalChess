# Chess in the terminal — architecture

Decision record for subsequent development sessions. Updated 2026-10-26.

## Chosen direction and current state

Build a cross-platform C#/.NET chess application for Windows, Linux, and macOS, with a terminal UI and a future AI opponent. **SharpConsoleUI is the chosen UI framework.** The active application is the new [TerminalChess](../TerminalChess/README.md) solution, created at the user's request for milestone 1. [ChessPrototype](../ChessPrototype/README.md) is retained as the arrangement-mode interaction experiment. The independent [Terminal.Gui prototype](../ChessPrototypeTerminalGui/README.md) also remains a comparison experiment. Neither prototype is the active development target.

The active solution targets `net10.0` and pins SharpConsoleUI `2.7.4` and Gera.Chess `1.2.0`. Its executable renders one board through `CanvasControl`, with original Unicode pieces, an ASCII fallback, smaller adaptive squares, legal-destination highlighting, drag preview, click-to-move, and keyboard controls. There is no piece-size toggle. Detailed controls and terminal notes are in the application README; the prototype README retains the original framework API investigation.

**Milestones 1 and 2 and milestone 3 strength configuration are implemented.** Local players or a human and a local Stockfish subprocess share the legal board, with enforced turns, castling, en passant, explicit Q/R/B/N promotion, check, checkmate, stalemate, draw claims/agreement, resignation, and reset. Versioned JSON saves preserve accepted move/draw history, outcomes, and difficulty. Startup offers four Stockfish strength presets. Draw adjudication has the explicit dead-position limitation below. Typed UCI move entry, personality/LLM integration, and a web API remain deferred. Real-terminal validation across the target operating systems remains outstanding.

## Chess rules and game state

A **C# chess rules library is the authoritative source of legal moves and position state**. Do not build a complete chess engine inside this application, and do not use Stockfish or an LLM to validate user moves.

The user confirmed **Gera.Chess 1.2.0**, after reviewing draws, promotion, maintenance, licensing, and future UCI notation input. The [evaluation](chess-library-evaluation.md) records alternatives and qualifications. The package targets .NET 8 and runs under this application's .NET 10 target. MIT notices for the rules integration are included in the application.

`TerminalChess.Rules/GeraGame` implements the application-owned `IChessGame` contract and owns the sole live Gera position. It caches legal moves at transitions using the per-piece generator rather than the all-pieces overload's task-and-wait implementation. Rendering and input consume application-owned values; they do not use Gera types. Immutable snapshots include FEN, side to move, check, outcome, pieces, and a monotonically increasing revision.

Gera's optional automatic draw rules are disabled. The adapter derives repetition identities/counts from accepted positions, accounting for side to move, castling rights, and legally usable en passant targets. It supports claimable threefold/50-move draws (including intended-move claims without committing the move), automatic fivefold/75-move draws, and agreement after both players have moved. Checkmate takes precedence at the 75-move boundary. Conservative material draws cover bare kings, one minor piece versus a bare king, and bishops all confined to one square color. Two knights versus king is not automatically drawn. **Arbitrary dead positions, including blocked pawn positions, are not generally recognized.** No clocks or timeout adjudication are implemented.

An additional tested adapter correction makes castling-right losses permanent: Gera can otherwise allow a replacement rook to regain a captured rook's rights. On a loss of rights, the adapter rebases the library position through FEN, preserving turn, en passant and move counters. This retains a single authoritative library board. A temporary library position can be used to evaluate an intended draw claim and is discarded without changing the live game.

Promotion remains pending in the interaction state until an explicit keyboard or footer-button choice is made; Escape and geometry changes cancel it without changing the position. Mouse, keyboard, and future typed input all submit source/destination/promotion intents. Typed UCI moves such as `e2e4` or `e7e8n` are explicitly deferred; Gera's lack of complete direct UCI parsing will be handled by a later notation adapter. Full UCI transport belongs to Stockfish infrastructure.

Keep application-owned concepts such as squares, move intents, results, and position snapshots at the application boundary. Introduce only the adapter needed to keep a selected library's types from spreading through rendering and input code. Avoid a second mutable position model that can disagree with the rules library.

The intended move path is:

1. Mouse or keyboard produces a source/destination intent, including promotion choice when required.
2. The application asks the rules component to validate and apply it.
3. A successful move produces the new position and game status; an invalid move leaves the position unchanged and returns a useful rejection.
4. The UI redraws the resulting state. If the game continues and the opponent should play, the application schedules the opponent turn.

User and opponent moves must pass through this same rules boundary. Drag preview, cursor position, hover, and selection are presentation/interaction state; they must not change the authoritative position before a move is accepted. Highlights must be derived from legal moves once the rules library is integrated, replacing the current arrangement destinations.

## Stockfish opponent

**Stockfish runs as a separate local process through UCI.** `TerminalChess.Infrastructure/StockfishOpponent` owns startup, protocol messages, search limits, timeouts, cancellation, and shutdown. `Core/Application/GameSession` decides when the opponent moves and applies its proposal through `IChessGame`; domain and TUI code do not parse engine output or launch processes. The transport follows the [upstream UCI implementation](https://github.com/official-stockfish/Stockfish/blob/master/src/uci.cpp).

The implemented integration:

- Captures an immutable snapshot plus the initial FEN and accepted move history, then returns a proposed move, including an explicit promotion when supplied.
- Validates that proposal through Gera before committing it. Human input cannot play the engine's side, even after an engine failure.
- Checks both the monotonic session revision and search identity before committing; reset, loading, cancellation, local fallback, and exit invalidate earlier searches, including already queued completions.
- Runs startup/search/I/O on workers and dispatches the result through the pinned framework's inspected `ConsoleWindowSystem.InvokeAsync` API. Mutable game transitions stay on the UI thread.
- Reports missing binaries, failed startup, invalid/no moves, deadline expiry, and unexpected exit without changing the position. Escape pauses a search; T retries; L switches the existing game to local two-player play. There is no automatic retry loop.
- Uses the pinned bundled Stockfish 19 executable in published releases. Resolution is explicit `--stockfish` path/name, `STOCKFISH_PATH`, the app-relative `stockfish/stockfish.exe` (Windows) or `stockfish/stockfish` (Unix), then `stockfish` on PATH. `AppContext.BaseDirectory` keeps discovery independent of the working directory and supports .NET single-file publishing. The running game never downloads an engine.

One fresh process per turn deliberately isolates protocol output and cancellation. It performs `uci`/`uciok`, sets one thread and 16 MiB hash, disables `UCI_LimitStrength`, sets the selected `Skill Level`, sends `ucinewgame`/`isready`, supplies `position fen ... moves ...`, and searches with the selected `go movetime` value. Sending the full history lets Stockfish recognize repetitions. A deadline of the selected search time plus ten seconds bounds startup and search. Completion/failure/cancellation sends stop/quit with a 500 ms grace period, then kills an unresponsive process tree and awaits exit. Standard error is continuously drained without accumulating unbounded logs. This integration favors simple lifecycle ownership over retaining engine hash between turns.

Default play is human White versus Stockfish; `--human black` makes Stockfish open. `--local` preserves local two-player mode. Startup parsing is isolated in `StartupOptions`; explicit local/human-side flags can override a startup save's mode, while F9 restores its recorded mode. Stockfish declines draw offers by moving and does not make optional draw claims or negotiate agreement; automatic rule outcomes still apply. The centered startup selector chooses strength before engine work begins; local mode bypasses it.

## Self-contained release packaging

At the user's request, `TerminalChess/packaging/Publish-Release.ps1` publishes a self-contained .NET 10 single-file `TerminalChess` executable using the opt-in `Bundled.pubxml` profile. Stockfish remains a separate executable beside it, with its original source/build scripts, GPL license, authors, and documentation. This preserves the existing process/UCI boundary. No engine binaries or generated packages are checked into Git; downloads, staging folders, archives, and SHA-256 sidecars live under ignored `artifacts/`.

The packaging manifest pins official Stockfish 19 (`sf_19`) universal binaries and SHA-256 digests for Windows/Linux x64/Arm64 and macOS x64/Arm64. Downloads are verified before extraction; cached downloads are rechecked. The original executable is only renamed, and its hash plus release/asset provenance is recorded in `package.json`. The complete upstream source directory accompanies the binary, including exact network names/retrieval instructions; the NNUE weights remain embedded in the upstream executable. Original .NET runtime notices and NuGet package metadata/supplied notices are included separately. No trimming, Native AOT, or framework/package upgrades are introduced.

Windows packages are ZIP; Unix packages are tar.gz and must be built on Unix to preserve executable modes. The native-host smoke script checks required files/checksums, starts the published app from an unrelated working directory with .NET lookup disabled, then runs a real bundled-engine UCI handshake/search/shutdown. Cross-published packages must be smoke-tested on their own target OS/architecture. Interactive terminal and mouse verification remain separate from these automation checks. Users extract the whole package and start the game without installing .NET or Stockfish.

## Opponent strength and startup selection

`Core/Application/OpponentStrength` owns four immutable presets: Beginner (Skill 0, 100 ms), Intermediate (Skill 8, 500 ms), Advanced (Skill 15, 1500 ms), and Expert (Skill 20, 3000 ms). Names/descriptions and skill/time values are shared by the TUI and each immutable opponent request. Skill 20 disables artificial weakening; Expert still has its explicit search budget. No Elo rating is promised. `UCI_LimitStrength` is disabled so it cannot override `Skill Level`.

`DifficultySelectionControl` paints the first screen black, centers the four descriptions and live skill/time details, uses cyan/gold accents, and supports keyboard and mouse selection. At the existing 48x25 minimum the descriptions use a second line. The selected level initially comes from a startup save, or Intermediate for a new game. Confirmation replaces the selector with the existing board in the same frameless window and transfers focus. The session defers engine searches until confirmation, including loads with Stockfish to move. Local mode starts directly on the board.

`GameSession` captures difficulty with each search and retains it across reset. Reconfiguration cancels prior work and advances the revision so a late result from another difficulty cannot apply. F9 restores the saved difficulty and schedules a fresh search if needed. Personality/LLM hooks remain deferred. `IChessOpponent` remains the single small application capability; no engine-plugin framework is introduced.

## JSON game persistence

At the user's request, milestone 2 also includes explicit JSON saving/loading. `JsonGameStore` writes a version 2 document containing the initial FEN, engine side (null for local), difficulty, and the ordered journal of accepted moves and draw/resignation actions. Version 1 files remain readable and default to Intermediate when no difficulty is present. Version 2 requires a valid named difficulty; unsupported versions/levels are rejected. The document does not persist the engine executable path, transient selection/promotion, in-flight searches, or credentials. The save is a journal rather than a final-FEN snapshot so repetition history, intended-move draw claims, draw offers, special moves, castling-right normalization, and outcomes can be reconstructed by the same rules adapter.

F5 saves and F9 loads; `--load` resumes at startup. The default is `TerminalChess/game.json` in per-user .NET ApplicationData; `--save` chooses a path. There is no autosave. Mode and side come from the loaded game unless explicitly overridden by startup `--local`/`--human`; executable discovery uses current arguments/environment and the bundled engine. Writes serialize to a temporary sibling and atomically replace the target only on success. Failed/cancelled writes preserve the previous file. File parsing and isolated replay run asynchronously, with a one-use prepared game transferred back to the UI owner only after validation. A changed live position during loading causes the load to be discarded. Malformed/unsupported JSON, invalid journals, and files over 4 MiB are rejected without replacing the live game. This is intentionally a small local file adapter, with no database or persistence framework.

## Optional LLM integration

The application must remain playable with the rules library and Stockfish **without an LLM**. The intended LLM role is opponent personality, conversation, and optional commentary. No provider, model, SDK, prompt contract, or credential mechanism has been selected.

An LLM must never decide legality, rewrite the authoritative position, or bypass the normal move-application path. If personality later influences move selection, it may only select from engine-generated candidates already validated by the rules component; the selected move must still pass the rules boundary before application. This is a future option, not implemented behavior.

Provider access belongs in infrastructure behind the application capability that needs it. Use asynchronous calls, cancellation, bounded waiting, and a usable fallback if the provider is absent or fails. Keep provider credentials and transport types out of domain objects and source-controlled configuration. Conversational output must not block the board or the engine turn.

## Layers and dependency boundaries

Maintain a clean separation of responsibilities without creating projects solely to fill a diagram.

| Boundary | Responsibility | Current implementation / intended growth |
| --- | --- | --- |
| Domain / application contracts | Chess values, move intents, immutable snapshots, outcomes, and `IChessGame` | `TerminalChess.Core/Domain` |
| Application | Serialized game ownership, opponent orchestration, replayable save contracts; selection, drag/cursor, promotion and input commands | `TerminalChess.Core/Application/GameSession` and `TerminalChess.Core/Interaction` |
| TUI | Rendering, layout, focus, controls, glyphs, colors, and translation of framework events | `TerminalChess.Tui/ChessBoardControl` and startup composition in `Program` |
| Rules adaptation | Single authoritative Gera position, cached legal moves, draw metadata, and package corrections | `TerminalChess.Rules/GeraGame` |
| Infrastructure | Stockfish process/UCI and versioned JSON game files | `TerminalChess.Infrastructure`; optional LLM provider access remains deferred |

`TerminalChess.Core` must remain free of UI-framework, rules-package, UCI transport, and LLM SDK types. The application depends on domain concepts and the small service contracts it actually needs. Adapters implement those contracts and depend inward. The executable composes the implementations and views. The rules-package adapter sits at the domain/application boundary; this does not create an alternate board or move generator.

The core combines application-owned chess concepts, the session, and interaction code in one assembly, separated by folders/namespaces. Rules adaptation is a separate assembly because it owns the third-party dependency. Infrastructure depends only on Core and owns process/filesystem details. The executable composes `GameSession` around a `GeraGame`, and injects the session into interaction/rendering. Input and accepted opponent results serialize on the UI thread. A loaded journal is replayed into an isolated Gera instance, then ownership transfers to the session; it is not a second independently mutable live board. The application-owned `SavedGame`/`GameAction` records contain no JSON or filesystem APIs.

Keep game transitions serialized under one application owner. Engine and LLM work should consume snapshots and return results that the application can accept or discard. Neither background work nor rendering may mutate a position independently.

## UI and input architecture

Preserve the current single-board experience rather than adding panels or advanced window management as part of backend work. Keep mouse and keyboard on the same interaction path.

- `ChessBoardControl` adapts SharpConsoleUI mouse/key events and performs drawing. Its framework event types stop at this boundary.
- `ChessBoardInteraction` accepts `PointerAction`, `BoardCommand`, and nullable `Square` values. It manages selection, drag state, preview targets, and cursor navigation independently of SharpConsoleUI.
- `BoardCoordinateMapper` converts terminal cells to board squares. Files are zero-based (`0 = a`); ranks are zero-based (`0 = rank 1`). With White at the bottom, the top visible rank is rank 8. Right/bottom board edges are exclusive; outside coordinates map to no square.
- Keep original Unicode chess symbols and ASCII fallback. The current cell-based text renderer does not offer independent percentage scaling of those symbols. Current square sizes adapt from 4–8 columns and 2–4 rows; the size toggle and pixel-art experiments were removed.
- Mouse-down starts a possible drag; held motion previews it; mouse-up attempts a move. Releasing outside the board cancels. Clicking source/destination and keyboard selection remain alternatives.

SharpConsoleUI's built-in canvas handler does not expose raw down/up events. The board reimplements `IMouseAwareControl` to normalize them. The inspected package automatically captures controls on a normal left press. Keep release-before-motion-before-press handling, including combined SGR flags and suppression of synthetic clicks after release.

Capture routes events within the application; it cannot recover releases that the terminal never reports. Escape clears the application's pending interaction. In the current SharpConsoleUI implementation, the framework's internal capture is cleared by a subsequently reported release. Rapid Unix presses represented only as multi-click flags have additional capture limitations, documented in the prototype README. Do not invent capture/release APIs or use a stationary-pointer timeout to infer button release.

## Planned milestones

These milestones record direction and acceptance goals. They do not authorize implementing all future integrations in an unrelated task.

| Milestone | State | Acceptance goal |
| --- | --- | --- |
| 0. Board interaction prototype | Implemented; real-terminal validation remains | Runnable SharpConsoleUI board, drag/drop, click/keyboard fallback, highlights, mapping/state tests, and documented terminal limitations |
| 1. Legal local gameplay | Implemented in TerminalChess; real-terminal validation remains | Gera integration, enforced turns/legal moves, special moves, promotion choice, check/outcomes, draw policy with documented dead-position limitation, and rejection/position-consistency tests |
| 2. Stockfish opponent + JSON persistence | Implemented; real-engine/terminal validation remains | UCI adapter, asynchronous searches, validated opponent moves, cancellation/stale-result protection, process cleanup, engine-failure recovery, and versioned JSON game journals |
| 3. Opponent strength configuration | Implemented; real-terminal validation remains | Centered four-level selector, skill/search-time presets, deferred startup, cancellation on reconfiguration, and saved difficulty; personality hooks remain deferred |
| 4. Optional LLM features | Planned after the rules/engine path works | Optional conversation/personality/commentary with provider failure fallback and no authority over game legality |
| 5. Cross-platform hardening | Ongoing as features mature | Validate intended terminals on Windows/Linux/macOS; improve packaging, engine discovery, diagnostics, cleanup, and automated platform coverage |

A web API, extra UI panels, and additional application frontends remain outside the agreed scope. Typed UCI move entry remains a requested future capability; internal UCI transport does not introduce a user-facing text input.

## Verification and continuity

Use the root [AGENTS.md](../AGENTS.md) for active-solution commands and coding instructions. The existing tests cover coordinate mapping, interaction transitions, SharpConsoleUI input adaptation, rendering, and headless window routing. Extend them around actual behavior changes rather than duplicating implementation details.

Rules tests cover legality, special moves, rejection without mutation, and outcome transitions. Engine tests launch `tests/FakeUciEngine`, a small cross-platform .NET UCI fixture, to exercise protocol handling, history, promotion, stderr draining, startup/exit/deadline failures, cancellation, and process cleanup. Session tests control queued dispatch and late results to verify input gating, legal move adaptation, reset/load staleness, retries/local fallback, and disposal. JSON tests verify replayed repetition/draw/outcome history, promotion/opponent mode, invalid files, and preserving the previous save after cancellation. Ordinary tests require no installed Stockfish or network credentials. A real-engine smoke test and real-terminal checks remain separate. For later LLM work, use deterministic test doubles and ensure failure does not prevent engine play.

Keep automated verification distinct from manual validation. Headless tests cannot establish mouse behavior or font appearance in a real emulator. Use the README checklist for drag/release outside the board and terminal, rapid clicks, resize, keyboard-only play, ASCII fallback, and terminal restoration on exit.

Strength verification adds centered/color/minimum-size rendering checks, mouse/keyboard/resize tests, headless startup-to-board focus routing, deferred-engine and changed-level stale-result tests, per-preset UCI transcripts, and version 1 migration/version 2 difficulty roundtrips. Native package smoke exercises the bundled engine separately from interactive-terminal validation.

Update this document when an architectural decision changes or a planned component becomes implemented. Keep package-specific implementation detail and running instructions in the active application README; keep concise repository instructions in `AGENTS.md`.
