# Chess prototype — Terminal.Gui

A standalone Terminal.Gui version of the neighboring `ChessPrototype` UI. It uses **Terminal.Gui 2.5.0**, the latest stable version in the live NuGet feed when checked on 2026-10-18, and targets **.NET 10** as required by that package.

The board matches the current SharpConsoleUI prototype: original Unicode pieces, smaller squares, the same colors and highlights, drag preview, click-to-move, and keyboard navigation. There is no piece-size toggle. Neither solution depends on the other.

**Arrangement mode only:** either color can move any piece to an empty square or capture an opposing piece. Friendly-occupied destinations are rejected. There are no chess movement rules, turns, check, special moves, game-over detection, Stockfish, LLM integration, persistence, or web API.

## Build and run

Install the .NET 10 SDK. From this directory:

```sh
dotnet restore ChessPrototypeTerminalGui.slnx
dotnet build ChessPrototypeTerminalGui.slnx -c Release --no-restore
dotnet test ChessPrototypeTerminalGui.slnx -c Release --no-build --no-restore
dotnet run --project src/Chess.Tui -c Release --no-build
```

Use an interactive ANSI terminal of at least **48 columns × 25 rows**. At 80 × 25 the squares are 5 × 2 cells; at 80 × 49 they are 8 × 4. White is at the bottom. A smaller terminal displays a resize message. Resizing the board geometry cancels the current selection/drag and releases the mouse grab.

```sh
dotnet run --project src/Chess.Tui -c Release -- --ascii
dotnet run --project src/Chess.Tui -- --help
```

The ASCII fallback uses uppercase `K Q R B N P` for White and lowercase for Black. Unicode symbols are single terminal characters, centered within their squares. Their actual appearance and size depend on the terminal font; this renderer does not support per-symbol font scaling. Redirected input/output and `TERM=dumb` are refused with a short message.

## Controls

| Input | Action |
| --- | --- |
| Left press on a piece | Select it and start a possible drag |
| Move while held | Preview the piece at the target; the board model stays unchanged |
| Release on a square | Attempt the move |
| Release outside the board | Cancel the operation |
| Click source, then destination | Move without dragging |
| Arrows or WASD | Move the keyboard cursor, initially on e2 |
| Enter or Space | Select a piece or attempt a move to the cursor |
| Escape | Cancel selection/drag, release mouse grab, and retain keyboard focus |
| R | Reset the starting position |
| U | Toggle Unicode/ASCII |
| Ctrl+Q | Quit |

Amber marks the selected square. Dots mark allowed **arrangement** destinations. Teal marks the active allowed target; red marks a friendly-occupied target. Brackets mark the keyboard cursor. Mouse and keyboard share the same selection state, so either can complete a selection started with the other.

## Structure

```text
src/Chess.Core/
  Domain/       Square, Piece, temporary GameState
  Interaction/  BoardCoordinateMapper, ChessBoardInteraction
src/Chess.Tui/
  Program.cs             Terminal.Gui application and single root view
  ChessBoardControl.cs   Drawing, framework input translation, mouse grab lifecycle
tests/Chess.Tests/
  CoordinateMapperTests.cs
  InteractionTests.cs
  TerminalGuiTests.cs
  TestEnvironment.cs
```

The core source and its tests were copied from the current SharpConsoleUI prototype. They remain independent of either framework. `ChessBoardInteraction` accepts application-level pointer actions, nullable board squares, and keyboard commands; Terminal.Gui event types stay inside `Chess.Tui`. The new solution contains its own core project, with no references to the sibling solution and no SharpConsoleUI package dependency.

## Terminal.Gui API findings

The implementation was checked against the [v2.5.0 source](https://github.com/tui-cs/Terminal.Gui/tree/v2.5.0), commit `ece73d25c37df9bb17cb0c0d2171abd4d98eeaff`, and compiled against the published package:

- Application startup uses the modern instance API: `Application.Create()`, `Init()`, a single `Runnable` root, `Run()`, and deterministic disposal to restore the terminal.
- `ChessBoardControl` derives from `View` and overrides `OnDrawingContent(DrawContext?)`. It draws using `SetAttribute` and viewport-relative `AddStr` calls. There are no extra windows or panels.
- [View mouse handling](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/Terminal.Gui/ViewBase/Mouse/View.Mouse.cs) exposes raw `Mouse` events through `OnMouseEvent`. `MousePositionTracking = true` enables motion handling. `Mouse.Position` is viewport-relative, including when the view has a nonzero screen offset.
- [Mouse grab API](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/Terminal.Gui/App/Mouse/IMouseGrabHandler.cs) provides `App.Mouse.GrabMouse(view)`, `IsGrabbed(view)`, and `UngrabMouse()`. The board grabs on a raw left press and releases on mouse-up, completed click, Escape, keyboard board commands, geometry changes, and disposal. Explicit grab is necessary because handled low-level events return before the framework's automatic grab stage.
- Motion may carry `LeftButtonPressed | PositionReport`. The adapter processes release first, then motion, then fresh press. Repeated press reports continue the core gesture. Synthetic clicks following a raw release are suppressed, preventing a duplicate action. Click-only streams also work.
- The [ANSI driver](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/Terminal.Gui/Drivers/AnsiDriver/AnsiOutput.cs) manages terminal mouse reporting and cleanup. In this release its mouse enable sequence includes all-motion reporting (`1003`), urxvt extended reporting (`1015`), and SGR reporting (`1006`). The app does not emit its own escape sequences or change the user's terminal settings.

Grab is application routing, not OS-wide pointer capture. If a terminal intercepts a gesture or fails to report a release outside its window, the app cannot infer that release. Return to the terminal and press Escape to clear the pending interaction and explicitly release the grab. No timeout treats a stationary held button as a release.

## Verification

Validated on Windows with .NET SDK 10.0.401:

- Release build succeeds with zero warnings/errors.
- All **52 tests pass**: 34 core mapping/state tests and 18 Terminal.Gui cases.
- Framework tests use the real ANSI driver in its buffer-only mode (`DisableRealDriverIO=1` in the test process), real layout/drawing, application keyboard/mouse routing, and explicit grab APIs.
- Tests cover all 32 symbols at several terminal sizes, ASCII rendering/toggle, startup focus, SGR motion, combined release/click flags, duplicate events, click-only input, friendly-target rejection, outside-board and outside-view cancellation, viewport coordinate translation, resize cancellation, and lost-release recovery with Escape.
- A test runs the real blocking `app.Run` loop, performs a keyboard move, and exits with Ctrl+Q. CLI help and redirected-terminal refusal were also exercised.

Physical terminal mouse behavior and font rendering still require manual checks. This execution host exposes redirected input, so the automated results do not establish emulator compatibility.

## Manual terminal checks

Test Windows Terminal, Linux terminals such as GNOME Terminal/Konsole/Kitty, and macOS Terminal.app/iTerm2. Also check any intended VS Code terminal, SSH, or tmux/screen environment.

1. Drag e2 to e4. Confirm preview while held, one move on release, and no premature board mutation.
2. Release over each board edge, header, and footer; verify cancellation. Leave and reenter while held, then drop on the board.
3. Release outside the terminal. If the release is lost, press Escape and verify the next drag works.
4. Click source/destination slowly and rapidly; try an opposing-piece capture and a friendly-occupied target.
5. Use only arrows/WASD and Enter/Space, then mix keyboard and mouse selection.
6. Resize during a drag and below/above the minimum; verify no unintended move and that input still works afterward.
7. Toggle ASCII, reset, and quit. Verify terminal cursor visibility and normal selection behavior return.

Some terminals reserve Shift+drag for text selection or require mouse reporting in their preferences. Multiplexers may intercept mouse events. Test plain left-button gestures first; keyboard controls are available when mouse reporting is unavailable.
