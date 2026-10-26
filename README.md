# TerminalChess

A .NET 10 terminal chess game. **Legal local and Stockfish opponent play are implemented**
in [TerminalChess](TerminalChess/README.md), using SharpConsoleUI and Gera.Chess.
Self-contained release packages include Stockfish and the .NET runtime: unzip
and launch `TerminalChess.exe` (Windows) or `./TerminalChess` (Linux/macOS).
Games can be saved and resumed as JSON. Typed UCI move input remains planned
for a later iteration.

Startup offers Beginner, Intermediate, Advanced, and Expert opponents, with
live Stockfish skill/search-time details before you start playing.

See [architecture and milestones](docs/architecture.md) for direction and
[TerminalChess controls, build and test commands](TerminalChess/README.md).

`ChessPrototype/` and `ChessPrototypeTerminalGui/` are retained experiments.
