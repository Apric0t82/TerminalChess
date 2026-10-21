# Chess in the terminal — architecture

Decision record for subsequent development sessions. Updated 2026-10-21.

## Chosen direction and current state

Build a cross-platform C#/.NET chess application for Windows, Linux, and macOS, with a terminal UI and a future AI opponent. **SharpConsoleUI is the chosen UI framework.** The active application is the new [TerminalChess](../TerminalChess/README.md) solution, created at the user's request for milestone 1. [ChessPrototype](../ChessPrototype/README.md) is retained as the arrangement-mode interaction experiment. The independent [Terminal.Gui prototype](../ChessPrototypeTerminalGui/README.md) also remains a comparison experiment. Neither prototype is the active development target.

The active solution targets `net10.0` and pins SharpConsoleUI `2.7.4` and Gera.Chess `1.2.0`. Its executable renders one board through `CanvasControl`, with original Unicode pieces, an ASCII fallback, smaller adaptive squares, legal-destination highlighting, drag preview, click-to-move, and keyboard controls. There is no piece-size toggle. Detailed controls and terminal notes are in the application README; the prototype README retains the original framework API investigation.

**Milestone 1: legal local gameplay is implemented.** Two local players share the board, with enforced turns, legal moves, castling, en passant, explicit Q/R/B/N promotion, check, checkmate, stalemate, draw claims/agreement, resignation, and reset. Draw adjudication has the explicit dead-position limitation below. Stockfish, typed UCI move entry, LLM integration, persistence, and a web API are not integrated. Real-terminal validation across the target operating systems remains outstanding.

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

Use **Stockfish as a separate process through the UCI protocol**. Process startup, protocol messages, executable resolution, search options, timeouts, cancellation, and shutdown belong in infrastructure. The application decides when the opponent should move and how to apply the result; domain and TUI code should not parse engine output or launch processes.

The integration should:

- Search from a snapshot of the authoritative position and return a proposed move.
- Validate that proposal through the rules library before committing it.
- Associate each search with a game/position revision and discard results for positions superseded by reset, a new game, cancellation, or another accepted move.
- Keep search and process I/O off the UI thread. Support cancellation and predictable process cleanup on exit.
- Handle missing binaries, failed startup, invalid/no move results, and engine termination without corrupting the game state.
- Allow platform-specific executable resolution without assuming a Windows path. Binary bundling versus user-supplied installation is undecided.

Difficulty/search limits and personality-related engine settings are future application policy, not chess rules. Start with one engine opponent and a small, explicit configuration surface; do not add a framework of engine plugins in advance.

## Optional LLM integration

The application must remain playable with the rules library and Stockfish **without an LLM**. The intended LLM role is opponent personality, conversation, and optional commentary. No provider, model, SDK, prompt contract, or credential mechanism has been selected.

An LLM must never decide legality, rewrite the authoritative position, or bypass the normal move-application path. If personality later influences move selection, it may only select from engine-generated candidates already validated by the rules component; the selected move must still pass the rules boundary before application. This is a future option, not implemented behavior.

Provider access belongs in infrastructure behind the application capability that needs it. Use asynchronous calls, cancellation, bounded waiting, and a usable fallback if the provider is absent or fails. Keep provider credentials and transport types out of domain objects and source-controlled configuration. Conversational output must not block the board or the engine turn.

## Layers and dependency boundaries

Maintain a clean separation of responsibilities without creating projects solely to fill a diagram.

| Boundary | Responsibility | Current implementation / intended growth |
| --- | --- | --- |
| Domain / application contracts | Chess values, move intents, immutable snapshots, outcomes, and `IChessGame` | `TerminalChess.Core/Domain` |
| Application | Selection, drag/cursor state, promotion choice, move and draw commands | `TerminalChess.Core/Interaction`; add opponent orchestration when required |
| TUI | Rendering, layout, focus, controls, glyphs, colors, and translation of framework events | `TerminalChess.Tui/ChessBoardControl` and startup composition in `Program` |
| Rules adaptation | Single authoritative Gera position, cached legal moves, draw metadata, and package corrections | `TerminalChess.Rules/GeraGame` |
| Future infrastructure | Stockfish process/UCI and optional LLM provider access | Deferred until the corresponding milestone is requested |

`TerminalChess.Core` must remain free of UI-framework, rules-package, UCI transport, and LLM SDK types. The application depends on domain concepts and the small service contracts it actually needs. Adapters implement those contracts and depend inward. The executable composes the implementations and views. The rules-package adapter sits at the domain/application boundary; this does not create an alternate board or move generator.

The core combines application-owned chess concepts and interaction code in one assembly, separated by folders/namespaces. Rules adaptation is a separate assembly because it owns the third-party dependency. The executable creates one `GeraGame` and injects it into interaction and rendering; input commands serialize transitions on the UI thread. Introduce further application/infrastructure components only as real dependencies require them.

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
| 2. Stockfish opponent | Planned after legal gameplay | UCI adapter, asynchronous searches, validated opponent moves, cancellation/stale-result protection, process cleanup, and useful engine-failure behavior |
| 3. Opponent configuration | Planned after engine play | Small, tested difficulty/search settings and personality hooks while preserving deterministic, valid game state |
| 4. Optional LLM features | Planned after the rules/engine path works | Optional conversation/personality/commentary with provider failure fallback and no authority over game legality |
| 5. Cross-platform hardening | Ongoing as features mature | Validate intended terminals on Windows/Linux/macOS; improve packaging, engine discovery, diagnostics, cleanup, and automated platform coverage |

Persistence, a web API, extra UI panels, and additional application frontends remain outside the agreed scope. Typed UCI move entry is a requested future capability, explicitly excluded from the milestone 1 iteration.

## Verification and continuity

Use the root [AGENTS.md](../AGENTS.md) for active-solution commands and coding instructions. The existing tests cover coordinate mapping, interaction transitions, SharpConsoleUI input adaptation, rendering, and headless window routing. Extend them around actual behavior changes rather than duplicating implementation details.

For the rules milestone, test legality, special moves, rejection without mutation, and outcome transitions. For engine work, test protocol handling and lifecycle behavior with controlled processes/fixtures, including cancellation and stale results. For LLM work, use deterministic test doubles and ensure failure does not prevent engine play; ordinary tests should not require network credentials or paid calls.

Keep automated verification distinct from manual validation. Headless tests cannot establish mouse behavior or font appearance in a real emulator. Use the README checklist for drag/release outside the board and terminal, rapid clicks, resize, keyboard-only play, ASCII fallback, and terminal restoration on exit.

Update this document when an architectural decision changes or a planned component becomes implemented. Keep package-specific implementation detail and running instructions in the active application README; keep concise repository instructions in `AGENTS.md`.
