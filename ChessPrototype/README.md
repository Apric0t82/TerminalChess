# Chess TUI prototype

A small cross-platform C#/.NET terminal chessboard using **SharpConsoleUI 2.7.4** (latest stable in the live NuGet feed when checked on 2026-10-16). It renders one board with the original Unicode chess symbols, mouse drag feedback, click-to-move, and keyboard controls.

**This is an interaction prototype in arrangement mode.** Either color can move any piece to any empty square or capture an opposing piece. Friendly-occupied destinations are rejected. There are no turns, movement rules, check, castling, promotion, game-over detection, AI, or saving.

## Build and run

Install the .NET 10 SDK. All three projects target `net10.0`; running the application and tests requires the .NET 10 runtime (included with the SDK). NuGet.Config uses the public NuGet feed explicitly.

From this directory:

```sh
dotnet restore ChessPrototype.slnx
dotnet build ChessPrototype.slnx -c Release --no-restore
dotnet test ChessPrototype.slnx -c Release --no-build --no-restore
dotnet run --project src/Chess.Tui -c Release --no-build
```

Run inside an interactive ANSI terminal, at least **48 columns × 25 rows**; **80 × 49** gives larger squares. The board adapts between 4–8-column and 2–4-row squares, with White at the bottom. Squares are 5 × 2 cells at 80 × 25 and 8 × 4 cells at 80 × 49, with reduced padding around the original piece symbols. Shrinking below the minimum displays a resize message. Resizing the board geometry cancels an active selection to prevent an accidental drop using stale coordinates.

Pieces use the original single-character Unicode chess symbols, centered within the current square sizes. There is no piece-size toggle or pixel-art renderer. SharpConsoleUI's cell-based text drawing API does not provide per-symbol font sizing, so an exact 30% enlargement of these symbols while keeping square dimensions unchanged is not supported by this renderer. Increasing the terminal font size would also enlarge the board.

ASCII fallback:

```sh
dotnet run --project src/Chess.Tui -c Release -- --ascii
dotnet run --project src/Chess.Tui -- --help
```

ASCII uses uppercase `K Q R B N P` for White and lowercase for Black. `U` toggles ASCII during a session; toggling back restores the original Unicode symbols. Font coverage and actual glyph width cannot be reliably detected by the application; choose ASCII when symbols appear missing or misaligned. Unicode chess symbols are emitted without emoji variation selectors. Redirected input/output and `TERM=dumb` are refused with a short message.

## Controls

| Input | Action |
| --- | --- |
| Left press on a piece | Select it and begin a possible drag |
| Move while held | Preview the dragged piece and destination; the model stays unchanged |
| Release on a square | Attempt the drop; friendly-occupied destinations are rejected |
| Release outside the board | Cancel the operation and clear selection |
| Click piece, then destination | Move without dragging |
| Arrows or WASD | Move the bracketed keyboard cursor (initially e2) |
| Enter or Space | Select the cursor's piece, or attempt a move to the cursor |
| Escape | Cancel selection and any unfinished gesture; keep keyboard focus |
| R | Reset the starting position |
| U | Toggle ASCII fallback |
| Ctrl+Q | Quit through SharpConsoleUI's built-in shortcut |

Amber marks the selected square. Dots mark allowed **arrangement** destinations. Teal marks the active allowed target; red marks a friendly-occupied target. Brackets show the keyboard cursor. Selecting the same square keeps it selected. Mouse and keyboard share selection, so you can select with one and complete a move with the other.

## Structure

```text
src/Chess.Core/
  Domain/       Square, Piece, temporary GameState
  Interaction/  BoardCoordinateMapper, ChessBoardInteraction
src/Chess.Tui/
  Program.cs            Terminal startup and single-window composition
  ChessBoardControl.cs  Canvas rendering and framework input translation
tests/Chess.Tests/
  CoordinateMapperTests.cs
  InteractionTests.cs
  SharpConsoleAdapterTests.cs
  WindowRoutingTests.cs
```

`Chess.Core` has no SharpConsoleUI dependency. Its application interaction state machine accepts `PointerAction`, nullable `Square` coordinates, and `BoardCommand`; it never sees framework event types. The mapper handles terminal cells, exclusive edges, rank inversion, and out-of-bounds positions before integer division.

`ChessBoardControl` owns layout, glyphs, colors, and mouse flag normalization. Domain and application concerns are separated into folders/namespaces in one small core assembly. No empty infrastructure projects or service abstractions are added. A future rules library should replace `GameState` as the authority for legal moves; Stockfish/UCI and optional LLM personalities belong in later application/infrastructure work. Neither is included here.

## SharpConsoleUI mouse API investigation

The implementation was checked against the **v2.7.4** source, commit `d88b871535fc8beb2a24c030ff886ab050ec8304`, and compiled against that exact NuGet release:

- [CanvasControl mouse implementation](https://github.com/nickprotop/ConsoleEx/blob/v2.7.4/SharpConsoleUI/Controls/CanvasControl/CanvasControl.Mouse.cs): built-in canvas events expose clicks/movement, not raw button-down/up. Its `ProcessMouseEvent` method is non-virtual. The derived board explicitly reimplements the public `IMouseAwareControl` interface to receive raw flags. It uses `CanvasControl.Paint`, `CanvasGraphics.FillRect`/`WriteString`, and `CanvasKeyPressed` for rendering and keyboard input.
- [WindowEventDispatcher](https://github.com/nickprotop/ConsoleEx/blob/v2.7.4/SharpConsoleUI/Windows/WindowEventDispatcher.cs): automatically captures the control on `Button1Pressed`, routes subsequent press/drag/release events to it, translates positions to control coordinates (including negative positions), and clears capture on release. [InputCoordinator](https://github.com/nickprotop/ConsoleEx/blob/v2.7.4/SharpConsoleUI/Input/InputCoordinator.cs) routes captured gestures back to the owning window, even outside that window. No invented `CaptureMouse`/`ReleaseMouse` methods are used.
- [NetConsoleDriver](https://github.com/nickprotop/ConsoleEx/blob/v2.7.4/SharpConsoleUI/Drivers/NetConsoleDriver.cs): enables basic mouse reporting (`1000`), SGR coordinates (`1006`), button-motion reporting (`1002`), and all-motion reporting (`1003`), and disables them during cleanup. On Windows it also configures console mouse/VT input. The app lets the driver manage these modes.
- [ANSI parser](https://github.com/nickprotop/ConsoleEx/blob/v2.7.4/SharpConsoleUI/Drivers/Input/AnsiInputParser.cs): motion with a held left button can carry `Button1Pressed`, `Button1Dragged`, and `ReportMousePosition` together. The adapter prioritizes release, then motion, then press so a motion report cannot restart a gesture. Repeated plain presses also continue the existing gesture.
- [Unix input reader](https://github.com/nickprotop/ConsoleEx/blob/v2.7.4/SharpConsoleUI/Drivers/Input/UnixStdinReader.cs): synthesizes click/multi-click flags. Combined release+click is handled once; synthetic clicks following a raw release are suppressed until the next raw press. Click-only event streams are also supported. Double/triple-click flags that replace a rapid press are treated as a new press for click-to-move.

### Capture limitations

Framework capture is routing **within the application**, not OS-wide pointer capture. It cannot recover a mouse release the terminal never reports, such as a release outside the terminal window or an intercepted gesture. Escape clears the application interaction state. The framework's internal capture is released on a later reported release; this prototype has one control, so keyboard navigation and the next normal gesture remain usable.

The framework establishes capture specifically on `Button1Pressed`. A Unix rapid press represented solely by a double/triple-click flag does not establish that capture, although click-to-move inside the board works. For a drag that begins immediately after another click, release routing outside the control must be checked manually. Use a fresh ordinary press after the terminal's multi-click interval for this case.

No timing heuristic assumes that a stationary held button has been released. Leaving and reentering the board while still held can complete a normal captured drag. Releasing within the terminal but outside the board cancels, including in the header/footer. The preview follows terminal cells/squares, not pixel coordinates. Escape is the recovery path if reporting stops.

## Verification and terminal compatibility

Automated verification on Windows on 2026-10-20 with .NET SDK 10.0.401, targeting `net10.0`:

- Release rebuild: succeeded with zero errors and two NU1900 warnings because the NuGet vulnerability feed was unreachable. The same feed warnings occurred in the .NET 8 baseline; dependency vulnerability auditing could not complete.
- Full test run: 54 passed, 0 failed, 0 skipped.
- Unit tests cover mapping across every cell, board edges, rank orientation, click/drag transitions, no-motion drops, outside cancellation, leave/reenter, repeated presses, friendly rejection, captures, keyboard navigation, and reset.
- Adapter tests dispatch through `IMouseAwareControl`, check separate/combined SGR flags, duplicate events, click-only events, multi-click presses, actual canvas rendering of all 32 original symbols at several terminal sizes, drag previews in the smaller squares, ASCII fallback, and resize cancellation.
- Headless framework tests run real window layout, focus, mouse routing, automatic capture outside the window, keyboard dispatch, and rendering using the package's `HeadlessConsoleDriver` and `BeginHosted` loop.
- `--help` and redirected-terminal refusal were executed. The execution host exposes redirected stdin even in its pseudo-terminal, so physical terminal mouse input and font appearance have **not** been validated here.

These are manual test targets, not claims of verified emulator compatibility:

| Platform / terminal | Checks still required |
| --- | --- |
| Windows: Windows Terminal (PowerShell/cmd) | Press/hold/move/release, outside-board cancellation, Unicode font, Ctrl+Q cleanup |
| Linux: GNOME Terminal, Konsole, Kitty | SGR drag events, rapid clicks, outside release, font width |
| macOS: Terminal.app and iTerm2 | Mouse reporting settings, drag/release delivery, glyph width |
| VS Code integrated terminal | Host key/mouse interception and focus changes |
| SSH, tmux, screen | Mouse reporting forwarding and multiplexer interception |

For each target:

1. Drag e2 to e4; verify live preview, exactly one move, and no source deletion until release.
2. Drag outside each board edge, release over terminal padding/header/footer, and verify cancellation. Leave/reenter while held and drop on the board.
3. Drag outside the terminal and release; if the release is lost, return and use Escape, then verify the next gesture works.
4. Click source and destination slowly and rapidly, including captures; try a rapid subsequent drag.
5. Select a piece, try a friendly-occupied target, cancel, and reset.
6. Finish e2-e4 using only arrows and Enter/Space. Mix mouse selection with keyboard completion.
7. Resize during a drag and below/above the minimum size; verify no unintended move.
8. Toggle Unicode/ASCII with U, then quit. Verify the shell cursor and normal terminal selection behavior return.

Some terminals reserve Shift+drag for text selection or require mouse reporting to be enabled in preferences. Multiplexers may intercept mouse events. Use plain left-button gestures for testing; keyboard controls remain available when the host does not forward mouse input.
