# Chess in the terminal — architecture

Decision record for subsequent development sessions. Updated 2026-10-20.

## Chosen direction and current state

Build a cross-platform C#/.NET chess application for Windows, Linux, and macOS, with a terminal UI and an AI opponent. **SharpConsoleUI is the chosen UI framework.** Continue development in [ChessPrototype](../ChessPrototype/README.md). The independent [Terminal.Gui prototype](../ChessPrototypeTerminalGui/README.md) remains a comparison experiment; it is not the product's implementation path.

The active solution currently targets `net10.0` and uses SharpConsoleUI `2.7.4`. Its executable renders one board through `CanvasControl`, with original Unicode pieces, an ASCII fallback, smaller adaptive squares, selected/target highlighting, drag preview, click-to-move, and keyboard controls. There is no piece-size toggle. Detailed controls, package API findings, and terminal compatibility checks are in the active prototype's README.

**Only the UI interaction prototype is implemented.** `GameState` is a temporary arrangement model: any piece of either color can move to an empty square or capture an opposing piece. It rejects friendly-occupied targets but does not enforce movement rules, turns, check, special moves, or game completion. Stockfish, a chess rules library, LLM integration, persistence, and a web API are not integrated.

## Chess rules and game state

A **C# chess rules library will be the authoritative source of legal moves and position state**. Do not build a complete chess engine inside this application, and do not use Stockfish or an LLM to validate user moves.

The particular rules library has **not been selected**. Evaluate candidates when legal gameplay is requested, including .NET/platform compatibility, legal move generation, check handling, castling, en passant, promotion, game outcomes, position notation, maintenance, and suitability for automated tests. No earlier brainstorm should be interpreted as a confirmed package choice.

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
| Domain | Chess concepts and authoritative position/rule behavior | `Chess.Core/Domain`; temporary `GameState` will be replaced by the chosen rules integration |
| Application | Move orchestration, interaction state, opponent turns, cancellation, and position revisions | `Chess.Core/Interaction` currently holds UI-independent input state; add a game coordinator when gameplay requires it |
| TUI | Rendering, layout, focus, controls, glyphs, colors, and translation of framework events | `Chess.Tui/ChessBoardControl` and startup composition in `Program` |
| Infrastructure | Rules-package adaptation where needed, Stockfish process/UCI, and optional LLM provider access | Not yet present; add implementations when the corresponding milestone is requested |

`Chess.Core` must remain free of SharpConsoleUI, Terminal.Gui, UCI transport, and LLM SDK types. The application depends on domain concepts and the small service contracts it actually needs. Infrastructure implements those contracts and depends inward. The executable composes the implementations and views. A rules-package adapter may sit at the domain/application boundary; this does not create an alternate authority for legality.

The current core combines domain and interaction code in one assembly, separated by folders/namespaces. Splitting out `Chess.Application` or `Chess.Infrastructure` is appropriate once real feature dependencies justify it, not a prerequisite to every change. The view currently owns the temporary interaction/model object; introduce application-level game ownership when rules and asynchronous opponents arrive.

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
| 1. Legal local gameplay | Planned | Select/integrate a C# rules library; enforce turns and legal moves; support special moves, promotion choice, check/outcomes; test rejection and position consistency |
| 2. Stockfish opponent | Planned after legal gameplay | UCI adapter, asynchronous searches, validated opponent moves, cancellation/stale-result protection, process cleanup, and useful engine-failure behavior |
| 3. Opponent configuration | Planned after engine play | Small, tested difficulty/search settings and personality hooks while preserving deterministic, valid game state |
| 4. Optional LLM features | Planned after the rules/engine path works | Optional conversation/personality/commentary with provider failure fallback and no authority over game legality |
| 5. Cross-platform hardening | Ongoing as features mature | Validate intended terminals on Windows/Linux/macOS; improve packaging, engine discovery, diagnostics, cleanup, and automated platform coverage |

Persistence, a web API, extra UI panels, and additional application frontends are outside the agreed prototype scope. Their design remains deferred until requested.

## Verification and continuity

Use the root [AGENTS.md](../AGENTS.md) for active-solution commands and coding instructions. The existing tests cover coordinate mapping, interaction transitions, SharpConsoleUI input adaptation, rendering, and headless window routing. Extend them around actual behavior changes rather than duplicating implementation details.

For the rules milestone, test legality, special moves, rejection without mutation, and outcome transitions. For engine work, test protocol handling and lifecycle behavior with controlled processes/fixtures, including cancellation and stale results. For LLM work, use deterministic test doubles and ensure failure does not prevent engine play; ordinary tests should not require network credentials or paid calls.

Keep automated verification distinct from manual validation. Headless tests cannot establish mouse behavior or font appearance in a real emulator. Use the README checklist for drag/release outside the board and terminal, rapid clicks, resize, keyboard-only play, ASCII fallback, and terminal restoration on exit.

Update this document when an architectural decision changes or a planned component becomes implemented. Keep package-specific implementation detail and running instructions in the prototype README; keep concise repository instructions in `AGENTS.md`.
