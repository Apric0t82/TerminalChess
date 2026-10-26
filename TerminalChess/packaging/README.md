# TerminalChess

Extract the whole package and open an ANSI terminal in this folder.
Run `TerminalChess.exe` on Windows or `./TerminalChess` on Linux/macOS.
No .NET or Stockfish installation is required, and play works offline.
Keep the `stockfish` folder beside the game executable.

The default game is human White against Stockfish. Add `--human black` to play
Black, `--local` for two local players, or `--ascii` for ASCII pieces.
The terminal needs at least 48 columns by 25 rows. Use `--help` for usage.

On the centered startup screen, choose a level with Up/Down (or W/S), then
Enter/Space. With a mouse, select a level and click Start game. The displayed
Stockfish Skill Level and search time update as you choose:

| Level | Description | Skill Level | Search time |
| --- | --- | --- | --- |
| Beginner | Forgiving opponent, shallow tactics | 0 | 100 ms |
| Intermediate | Reasonable tactics, occasional mistakes | 8 | 500 ms |
| Advanced | Strong positional and tactical play | 15 | 1500 ms |
| Expert | Full-strength Stockfish | 20 | 3000 ms |

Intermediate is the initial selection for a new game; a startup save's difficulty
is highlighted when resuming. No engine search starts until confirmation. Expert
uses no artificial weakening within its search budget. Local mode skips selection.
The chosen difficulty survives new games and is retained in JSON saves; F9
restores it. Older version 1 saves load with Intermediate as their default.

| Input | Action |
| --- | --- |
| Drag, or click source then destination | Play a legal move |
| Arrows / WASD, then Enter / Space | Move cursor, select, and play |
| Q / R / B / N during promotion | Choose queen, rook, bishop, or knight |
| Escape | Cancel selection or pause an engine search |
| T / L | Retry the engine / continue with two local players |
| C / F / Y / N | Claim draw / offer draw / accept / decline |
| X / R / U | Resign / new game / toggle ASCII |
| F5 / F9 | Save / load the game |
| Ctrl+Q | Quit |

Saving is explicit; quitting does not save automatically. The default JSON file
is `TerminalChess/game.json` in your per-user application-data folder. Use
`--save ./game.json` to choose a file and `--load` to resume it at startup.
Saved games retain moves, draw history, outcomes, and opponent mode. Startup
`--local` or `--human` overrides a loaded game's mode/side; F9 restores its mode.

The board stays White at the bottom. Gera validates all human and engine moves.
Stockfish declines draw offers by moving; automatic draws still apply. Known
draw handling covers repetition, move counters, and conservative material
cases; arbitrary blocked dead positions are not exhaustively recognized.

If the engine fails, the board remains intact; T retries and L enables local
play. A custom engine can be selected with `--stockfish <path>` or
`STOCKFISH_PATH`. Otherwise the bundled engine is used, with PATH as a fallback.
If a mouse release outside the terminal is lost, Escape cancels the gesture.

Stockfish 19 runs locally as a separate UCI subprocess. Its license, authors,
source, and build instructions are in `stockfish/`; see `STOCKFISH-NOTICE.txt`
and `package.json` for the exact upstream release and checksums. Additional
dependency/runtime notices are in `licenses/` and `THIRD-PARTY-NOTICES.md`.
Preserve the included sources, licenses, and notices when redistributing.
