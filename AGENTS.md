# Instructions for Codex

## Project direction

- The active application is **`ChessPrototype/`**, using **SharpConsoleUI**. Read [docs/architecture.md](docs/architecture.md) for decisions and milestones, and [the prototype README](ChessPrototype/README.md) for controls and terminal notes.
- `ChessPrototypeTerminalGui/` is a retained comparison experiment. Do not develop it, migrate to it, or delete it unless the user requests that work.
- Target Windows, Linux, and macOS. The active projects target `net10.0` and pin SharpConsoleUI `2.7.4`. Avoid incidental framework or package upgrades.

## Structure and boundaries

- `ChessPrototype/src/Chess.Core/Domain/`: board coordinates, pieces, and the temporary `GameState`.
- `ChessPrototype/src/Chess.Core/Interaction/`: framework-independent interaction state machine and terminal-cell coordinate mapper.
- `ChessPrototype/src/Chess.Tui/`: executable, SharpConsoleUI rendering, and translation of framework events into core actions.
- `ChessPrototype/tests/Chess.Tests/`: xUnit tests for mapping, interaction, UI event adaptation, rendering, and headless window routing.
- Keep UI framework types out of `Chess.Core`. Keep process, network, filesystem, and provider details outside domain logic. Introduce application/infrastructure components when a requested feature needs them; avoid empty projects and speculative abstractions.

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
dotnet restore ChessPrototype/ChessPrototype.slnx
dotnet build ChessPrototype/ChessPrototype.slnx -c Release --no-restore
dotnet test ChessPrototype/ChessPrototype.slnx -c Release --no-build --no-restore
dotnet run --project ChessPrototype/src/Chess.Tui -c Release --no-build --no-restore
```

Append `-- --ascii` for ASCII pieces or `-- --help` for usage. The solution's `NuGet.Config` uses nuget.org explicitly; do not change machine-wide NuGet sources.

For application changes, build and run the relevant existing tests. Add focused tests for new behavior and regressions. For documentation-only edits, check content, paths, and links; a full test run is unnecessary. Report automated verification separately from manual terminal testing.

## Important constraints

- The current board is **arrangement mode**, not legal chess: either side can move to any empty/enemy square. Do not describe it as validating chess moves.
- Preserve the original Unicode symbols, ASCII fallback, smaller adaptive squares, drag-and-drop, click-to-move, keyboard navigation, highlighting, outside-board cancellation, and Escape recovery. There is no piece-size toggle or pixel-art renderer.
- Mouse capture is application routing, not OS-wide capture. Releases outside the terminal may be lost. Preserve combined-flag handling and synthetic-click suppression; do not infer release from a stationary pointer timeout.
- A future C# rules library must own legality and game state. Stockfish is the planned opponent through UCI; an LLM is optional for personality/conversation and must not own legality or mutate the board directly.
- Stockfish, LLM integration, persistence, a web API, and extra panels/windows are outside the current prototype. Planned milestones describe direction, not permission to implement them without a corresponding task.
