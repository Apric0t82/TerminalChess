# TerminalChess

A .NET 10 terminal chess game. **Legal local and Stockfish opponent play are implemented**
in [TerminalChess](TerminalChess/README.md), using SharpConsoleUI and Gera.Chess.
Stockfish runs as a user-supplied local subprocess. Games can be saved and resumed
as JSON. Typed UCI move input remains planned for a later iteration.

See [architecture and milestones](docs/architecture.md) for direction and
[TerminalChess controls, build and test commands](TerminalChess/README.md).

`ChessPrototype/` and `ChessPrototypeTerminalGui/` are retained experiments.
