# TerminalChess

Local two-player chess in a cross-platform terminal, using **.NET 10**,
**SharpConsoleUI 2.7.4**, and **Gera.Chess 1.2.0**. Milestone 1 includes legal
turns/moves, castling, en passant, explicit promotion choice, check, checkmate,
stalemate, draw adjudication, resignation, and a new-game command.

## Build and run

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
contains the Gera adapter; the executable composes it with the view.

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
does not contain preceding positions. No user-facing loading/saving is provided.

Typed UCI moves such as `e2e4` and `e7e8n`, Stockfish, LLM features, persistence,
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

The [library evaluation](../docs/chess-library-evaluation.md) records the choice
and upstream findings. Rules-package notices are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
