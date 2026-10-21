# TerminalChess rules library proposal

Evaluated 2026-10-21. **Confirmed by the user; integrated in TerminalChess.**

The user requested milestone 1 (legal local gameplay) in a new solution,
`TerminalChess/TerminalChess.slnx`, rather than further development in
`ChessPrototype/`. Keep .NET 10 and SharpConsoleUI 2.7.4. Preserve the existing
board interaction behavior when bringing the UI into the new application.

## Confirmed selection

Use **Gera.Chess 1.2.0** for legal moves and authoritative position state, behind
an application-owned adapter. This is a qualified recommendation: its optional
automatic draw rules need to be disabled and replaced with tested adjudication
at that boundary. Do not claim complete FIDE draw handling from the package.

The package offers a relatively small board-oriented API, castling, en passant,
check/checkmate/stalemate, FEN/SAN/PGN, and all four promotion choices. It targets
.NET 8 and is consumable by the application's .NET 10 projects. Promotion choice
must be collected before committing a move; the UI must not silently accept the
library's default queen promotion.

Evidence: [NuGet package](https://www.nuget.org/packages/Gera.Chess/1.2.0),
[promotion implementation](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/Types/MoveParameter.cs),
[move validation](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/ChessBoard/ChessValidations.cs).

## Candidate comparison

| Candidate | Draw handling and promotion | Maintenance / license | Assessment |
| --- | --- | --- | --- |
| Gera.Chess 1.2.0 | Checkmate/stalemate and Q/R/B/N promotion. Optional repetition, 50-move and material rules have limitations described below. | NuGet release 2025-02-10; repository commits through 2026-09-15. MIT. | Best fit if the draw-adapter work is accepted. |
| Rudzoft.ChessLib 0.0.3 | Legal move generation, promotions, repetition and halfmove-clock primitives. Game outcome handling still needs care; its draw helpers are oriented toward engine use. | Latest default-branch commit inspected: 2024-06-29. MIT. Published package targets .NET 6; repository has moved to .NET 8. | More integration machinery and older maintenance; no clear draw-handling advantage. |
| Chess.NET / ChessDotNet | Move validation, special moves, promotion and draw APIs. | Maintainer explicitly says the project is no longer maintained. MIT. | Reject for a new application on maintenance grounds. |
| Veggerby.Boards.Chess | Its published limitations state that 50-move/repetition draws are not enforced and the public promotion path defaults to queen. | Prerelease packages; MIT. | Requires extending required gameplay features. |
| pax.chess 0.7.0 | Q/R/B/N promotion and position hashing. Inspected outcome evaluator checks 75 against a halfmove clock, checks automatic draws before checkmate, and does not provide complete material/claim handling. | NuGet release 2026-06-08; repository commit 2026-06-07. AGPL-3.0-or-later. | Additional draw corrections and a materially different license choice. |

Sources for alternatives:

- Rudzoft: [package](https://www.nuget.org/packages/Rudzoft.ChessLib/0.0.3),
  [game outcome helpers](https://github.com/rudzen/ChessLib/blob/f43af73ed6e5fd528ea340c5d00142346b5a1c1d/src/chess-lib/Rudzoft.ChessLib/Game.cs),
  [position primitives](https://github.com/rudzen/ChessLib/blob/f43af73ed6e5fd528ea340c5d00142346b5a1c1d/src/chess-lib/Rudzoft.ChessLib/Position.cs),
  [license](https://github.com/rudzen/ChessLib/blob/f43af73ed6e5fd528ea340c5d00142346b5a1c1d/LICENSE),
  [latest inspected commit](https://github.com/rudzen/ChessLib/commit/f43af73ed6e5fd528ea340c5d00142346b5a1c1d).
- Chess.NET: [maintenance statement and features](https://github.com/thomas-daniels/Chess.NET).
- Veggerby: [package limitations](https://www.nuget.org/packages/Veggerby.Boards.Chess/0.1.0-prerelease0206).
- pax.chess: [package and license designation](https://www.nuget.org/packages/pax.chess/0.7.0),
  [outcome evaluator](https://github.com/ipax77/pax.chess/blob/c559e6f84065b2e164928814a69bb54228fd9421/src/pax.chess/GameOutcomeEvaluator.cs),
  [game implementation](https://github.com/ipax77/pax.chess/blob/c559e6f84065b2e164928814a69bb54228fd9421/src/pax.chess/ChessGame.cs).

## Draw findings and proposed policy

Gera's [repetition implementation](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/ChessBoard/EndGameTypes/RepetitionRule.cs)
only compares the current position with positions four and eight plies earlier.
This misses other repetition intervals. Its comparer includes castling rights
and a raw en passant target; repetition identity must instead account for whether
en passant is actually a legal possibility.

Its [material implementation](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/ChessBoard/EndGameTypes/InsufficientMaterialRule.cs)
declares king and two knights versus king drawn. Inability to force mate is not
the same as impossibility of mate under any legal continuation. Its material
patterns also do not constitute general dead-position detection.

Its [50-move rule](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/ChessBoard/EndGameTypes/FiftyMoveRule.cs)
automatically ends play at 100 halfmoves when enabled. The inspected
[provider](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/ChessBoard/EndGameTypes/EndGameProvider.cs)
has no separate fivefold/75-move handling.

The intended application policy is to distinguish claimable threefold/50-move
draws from automatic fivefold/75-move draws (150 halfmoves), with checkmate taking
precedence. Include claims based on an intended legal move, draw agreement, and
conservative, correct material detection. Broader dead-position recognition must
remain an explicit limitation unless it is actually implemented and verified.
These distinctions follow [FIDE Laws, articles 5 and 9](https://handbook.fide.com/chapter/E012023).

The adapter may keep repetition identity/count metadata derived from accepted
library positions. It must not maintain a second mutable board or implement a
second legal-move generator. Keep package types out of input and rendering code.

## Maintenance and licensing details

The [live NuGet version index](https://api.nuget.org/v3-flatcontainer/gera.chess/index.json)
was queried directly and ends at **1.2.0**. The latest inspected repository
[commit](https://github.com/Geras1mleo/Chess/commit/702107da49d9aab9187c918decd54c3c62448422)
is dated 2026-09-15 and its project declares 1.3.0/net10.0. That is repository
activity, not evidence of a published 1.3.0 package. Pin the published version;
do not silently consume the default branch. Recent activity does not establish
a support commitment, and the older published package carries integration risk.

The [MIT license](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/LICENSE.md)
requires preserving its copyright and permission notice in distributed copies
or substantial portions. The 1.2.0 package also depends on Ardalis.SmartEnum;
check the resolved dependency's notice when preparing third-party notices.

## UCI and implementation after confirmation

The user explicitly wants future typed UCI move notation (`e2e4`, with an optional
promotion suffix such as `e7e8n`), but **does not want that input feature in this
iteration**. Treat native notation conversion as a selection advantage, separate
from implementing the full UCI engine protocol.

Gera does not offer complete direct UCI move-string parsing; its documented
promotion-string limitation remains a disadvantage:
[upstream issue](https://github.com/Geras1mleo/Chess/issues/4). Its public
[move API](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/Types/Move.cs)
accepts source and destination separately, and its
[promotion event](https://github.com/Geras1mleo/Chess/blob/0d326e0de4cb07852d24baab1929689808f0348e/ChessLibrary/Types/Events.cs)
allows selection of Q/R/B/N. A later parser can translate a UCI move token into
the application's source/destination/promotion intent, which must then pass
through normal library validation. Castling and en passant must be resolved in
the current position; notation parsing alone never establishes legality.

Keep milestone 1's move intent independent of mouse, keyboard, notation, and
package types. That supports later typed input without implementing its parser
or text-entry control now. The future Stockfish adapter can submit the same
intent. Native UCI conversion does not by itself outweigh maintenance, licensing,
and draw correctness. The user confirmed the qualified Gera recommendation.

Milestone 1 is implemented in `TerminalChess/TerminalChess.slnx`, with legal turns
and destinations, special moves, promotion selection, check/outcomes and tested
rejection consistency. The existing SharpConsoleUI behavior was brought over
without editing either prototype. Root instructions and architecture identify
the new application. See its README for commands and the draw limitations.

## Verification performed

The initial selection inspected upstream source, licenses, package pages, commit
dates and the live Gera.Chess version index. Integration additionally inspected
the installed 1.2.0 package API/license and added automated regressions for
repetition intervals, legal en passant identity, material draws, move-count
boundaries, promotion and position consistency. Real-terminal gameplay has not
been manually validated across the target operating systems.

A further integration regression reproduced a captured-rook castling-right bug:
the right can reappear when another rook replaces the captured one. The adapter
rebases the library through FEN when rights are lost, preserving turn, en passant
and counters while making the loss permanent. `ReplacingACapturedRookDoesNotRestoreCastlingRights`
protects this correction.
