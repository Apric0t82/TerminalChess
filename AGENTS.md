# Instructions for Codex

## Project direction

- The active application is **`TerminalChess/`**, using **SharpConsoleUI** and **Gera.Chess**. Read [docs/architecture.md](docs/architecture.md) for decisions and milestones, and [the application README](TerminalChess/README.md) for controls and terminal notes.
- `ChessPrototype/` is the retained arrangement-mode interaction prototype. Do not develop it unless explicitly requested.
- `ChessPrototypeTerminalGui/` is a retained comparison experiment. Do not develop it, migrate to it, or delete it unless the user requests that work.
- Target Windows, Linux, and macOS. The active projects target `net10.0` and pin SharpConsoleUI `2.7.4` and Gera.Chess `1.2.0`. Avoid incidental framework or package upgrades.

## Structure and boundaries

- `TerminalChess/src/TerminalChess.Core/Domain/`: application-owned chess values, move intents, snapshots, and the game contract.
- `TerminalChess/src/TerminalChess.Core/Interaction/`: framework-independent interaction state machine and terminal-cell coordinate mapper.
- `TerminalChess/src/TerminalChess.Core/Application/`: serialized game session, opponent orchestration, stale-result protection, and replayable save contracts.
- `TerminalChess/src/TerminalChess.Infrastructure/`: local Stockfish UCI subprocess and JSON file persistence.
- `TerminalChess/src/TerminalChess.Rules/`: authoritative Gera position, legal-move adaptation, draw adjudication, and castling-right normalization.
- `TerminalChess/src/TerminalChess.Tui/`: executable, SharpConsoleUI rendering, and translation of framework events into core actions.
- `TerminalChess/tests/TerminalChess.Tests/`: xUnit tests for rules, draws, mapping, interaction, UI event adaptation, rendering, and headless window routing.
- Keep UI and rules-package types out of `TerminalChess.Core`. Keep process, network, filesystem, and provider details outside domain logic. Introduce application/infrastructure components when a requested feature needs them; avoid empty projects and speculative abstractions.

## Coding conventions

- Follow existing C#: four-space indentation, file-scoped namespaces, nullable reference types enabled, PascalCase types/methods/properties, and `_camelCase` private fields.
- Keep changes focused and dependencies minimal. Comment non-obvious behavior, especially terminal mouse flag handling and coordinate conversion.
- Keep rendering and input responsive. Future engine/network work must be asynchronous and cancellable; do not block the UI thread.
- Do not write to `Console` while the TUI is running. Console output is reserved for help and startup errors; use framework-supported diagnostics for runtime logging.
- Inspect the actual installed package API/source before relying on framework behavior. Do not invent capture methods or claim unsupported per-symbol font scaling.

## Build, test, run

Use `.slnx` for existing and new solutions in modern .NET development. Keep build/test commands and documentation pointed at the `.slnx` files.

Run these commands **from this root directory**, using the .NET 10 SDK and runtime:

```sh
dotnet restore TerminalChess/TerminalChess.slnx
dotnet build TerminalChess/TerminalChess.slnx -c Release --no-restore
dotnet test TerminalChess/TerminalChess.slnx -c Release --no-build --no-restore
dotnet run --project TerminalChess/src/TerminalChess.Tui -c Release --no-build --no-restore
```

Append `-- --ascii` for ASCII pieces or `-- --help` for usage. The solution's `NuGet.Config` uses nuget.org explicitly; do not change machine-wide NuGet sources.

For application changes, build and run the relevant existing tests. Add focused tests for new behavior and regressions. For documentation-only edits, check content, paths, and links; a full test run is unnecessary. Report automated verification separately from manual terminal testing.

## Important constraints

- The active application enforces legal local chess. The retained `ChessPrototype/` remains arrangement mode. Draw adjudication covers the documented material cases, not arbitrary dead positions.
- Preserve the original Unicode symbols, ASCII fallback, smaller adaptive squares, drag-and-drop, click-to-move, keyboard navigation, highlighting, outside-board cancellation, and Escape recovery. There is no piece-size toggle or pixel-art renderer.
- Mouse capture is application routing, not OS-wide capture. Releases outside the terminal may be lost. Preserve combined-flag handling and synthetic-click suppression; do not infer release from a stationary pointer timeout.
- Gera owns legality and position state through `IChessGame`; retain the tested adapter corrections for draws and captured-rook rights. Stockfish supplies opponent proposals through UCI; an LLM is optional for personality/conversation and must not own legality or mutate the board directly.
- Typed UCI move input is requested for a later iteration, not implemented now. Preserve input-independent source/destination/promotion intents.
- Milestone 2 includes Stockfish subprocess play and versioned JSON game journals. Keep opponent and file I/O asynchronous, validate all proposed moves through Gera, and replay saves in isolation before replacing the live game. Stockfish is user-supplied, not bundled.
- Opponent difficulty configuration, LLM integration, a web API, and extra panels/windows remain deferred. Planned milestones describe direction, not permission to implement them without a corresponding task.
