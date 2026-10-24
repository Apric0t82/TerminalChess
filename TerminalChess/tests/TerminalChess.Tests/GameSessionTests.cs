using System.Threading.Channels;
using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;
using TerminalChess.Core.Interaction;
using TerminalChess.Infrastructure;
using TerminalChess.Rules;
using TerminalChess.Tui;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using SharpConsoleUI;

namespace TerminalChess.Tests;

public sealed class GameSessionTests
{
    private sealed class Opponent : IChessOpponent
    {
        public sealed record Request(OpponentPosition Position, CancellationToken Token, TaskCompletionSource<MoveIntent> Reply);
        public Channel<Request> Requests { get; } = Channel.CreateUnbounded<Request>();
        public async Task<MoveIntent> GetMoveAsync(OpponentPosition position, CancellationToken cancellationToken)
        {
            var reply = new TaskCompletionSource<MoveIntent>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Requests.Writer.WriteAsync(new(position, cancellationToken, reply));
            // Deliberately ignore cancellation to prove even late results cannot change the game.
            return await reply.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        public async Task<Request> Next() => await Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class Dispatcher
    {
        private readonly Channel<Action> _actions = Channel.CreateUnbounded<Action>();
        public Task Dispatch(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Writer.TryWrite(() =>
            {
                try { action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
            });
            return completion.Task;
        }
        public async Task<Action> Next() => await _actions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static GameSession Session(Opponent opponent, Dispatcher dispatcher, PieceSide? side = PieceSide.Black,
        string? fen = null) => new(fen is null ? new GeraGame() : new GeraGame(fen), opponent, dispatcher.Dispatch, f => new GeraGame(f), side);
    private static MoveIntent Move(string value) => UciMove.Parse(value);

    [Fact]
    public async Task HumanMoveStartsOneSearchAndOpponentMoveWaitsForUiDispatch()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher);
        Assert.Equal(MoveResult.Applied, session.TryMove(Move("e2e4")));
        var request = await opponent.Next();
        Assert.Equal(PieceSide.Black, request.Position.Snapshot.Turn);
        Assert.Equal(new[] { Move("e2e4") }, request.Position.Moves);
        Assert.Contains(" b ", request.Position.Snapshot.Fen);
        session.StartOpponent();
        Assert.False(opponent.Requests.Reader.TryRead(out _));
        Assert.Equal(MoveResult.Illegal, session.TryMove(Move("e7e5")));
        Assert.False(session.OfferDraw());
        session.Resign();
        Assert.Null(session.Outcome);
        var input = new ChessBoardInteraction(session);
        input.Pointer(PointerAction.Click, new(4, 6));
        input.Command(BoardCommand.Activate);
        Assert.Null(input.Selected);
        request.Reply.SetResult(Move("e7e5"));
        var apply = await dispatcher.Next();
        Assert.Null(session[new Square(4, 4)]);
        Assert.True(session.IsThinking);
        apply();
        await session.ActiveSearch;
        Assert.Equal(new Piece(PieceSide.Black, PieceKind.Pawn), session[new Square(4, 4)]);
        Assert.Null(session[new Square(4, 6)]);
        Assert.Equal(PieceSide.White, session.Turn);
        Assert.False(session.IsThinking);
        Assert.Equal(2, session.Export().Actions.Count);
    }

    [Fact]
    public async Task ResetDiscardsAnAlreadyQueuedEngineResult()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher);
        session.TryMove(Move("e2e4"));
        var request = await opponent.Next();
        request.Reply.SetResult(Move("e7e5"));
        var apply = await dispatcher.Next();
        var search = session.ActiveSearch;
        var oldRevision = session.Snapshot.Revision;
        session.Reset();
        var reset = session.Snapshot;
        apply();
        await search;
        Assert.True(reset.Revision > oldRevision);
        Assert.Same(reset, session.Snapshot);
        Assert.Equal(32, reset.Pieces.Count);
        Assert.Empty(session.Export().Actions);
    }

    [Fact]
    public async Task CancelAndRetryRejectsLateResultsEvenForTheSamePosition()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher);
        session.TryMove(Move("e2e4"));
        var first = await opponent.Next(); var firstTask = session.ActiveSearch;
        session.CancelOpponent();
        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(session.IsThinking);
        Assert.Contains("T: retry", session.Status);
        session.StartOpponent();
        var second = await opponent.Next();
        first.Reply.SetResult(Move("e7e5"));
        await firstTask;
        Assert.Null(session[new Square(4, 4)]);
        second.Reply.SetResult(Move("c7c5"));
        (await dispatcher.Next())();
        await session.ActiveSearch;
        Assert.Equal(new Piece(PieceSide.Black, PieceKind.Pawn), session[new Square(2, 4)]);
        Assert.Equal(new Piece(PieceSide.Black, PieceKind.Pawn), session[new Square(4, 6)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EngineFailurePreservesPositionAndAllowsLocalRecovery(bool illegalMove)
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher);
        session.TryMove(Move("e2e4"));
        var before = session.Snapshot;
        var request = await opponent.Next();
        if (illegalMove) request.Reply.SetResult(Move("e7e4"));
        else request.Reply.SetException(new IOException("engine exited"));
        (await dispatcher.Next())();
        await session.ActiveSearch;
        Assert.Same(before, session.Snapshot);
        Assert.False(session.IsThinking);
        Assert.Contains(illegalMove ? "illegal move" : "engine exited", session.Status);
        Assert.Contains("L: local", session.Status);
        Assert.False(opponent.Requests.Reader.TryRead(out _));
        session.UseLocalPlayers();
        Assert.Null(session.EngineSide);
        Assert.Equal(MoveResult.Applied, session.TryMove(Move("e7e5")));
    }

    [Fact]
    public async Task EngineCanPlayWhiteAndUnderpromoteThroughRules()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher, PieceSide.White, "7k/P7/8/8/8/8/8/7K w - - 0 1");
        session.StartOpponent();
        var request = await opponent.Next();
        request.Reply.SetResult(Move("a7a8n"));
        (await dispatcher.Next())();
        await session.ActiveSearch;
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Knight), session[new Square(0, 7)]);
        Assert.Equal(EndReason.InsufficientMaterial, session.Outcome?.Reason);
        session.StartOpponent();
        Assert.False(opponent.Requests.Reader.TryRead(out _));
    }

    [Fact]
    public async Task LoadSupersedesQueuedResultAndInvalidLoadKeepsLiveGame()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher);
        var initial = session.Export();
        session.TryMove(Move("e2e4"));
        var before = session.Snapshot;
        Assert.Throws<InvalidDataException>(() => session.Load(initial with
        {
            Actions = new[] { new GameAction(GameActionKind.Move, Move("e2e5")) }
        }));
        Assert.Same(before, session.Snapshot);
        Assert.True(session.IsThinking);
        var request = await opponent.Next();
        request.Reply.SetResult(Move("e7e5"));
        var apply = await dispatcher.Next(); var search = session.ActiveSearch;
        session.Load(initial);
        var loaded = session.Snapshot;
        apply(); await search;
        Assert.Same(loaded, session.Snapshot);
        Assert.Equal(initial.InitialFen, loaded.Fen);
        Assert.True(loaded.Revision > before.Revision);
    }

    [Fact]
    public async Task DisposalCancelsSearchAndRejectsItsLateMove()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        var session = Session(opponent, dispatcher);
        session.TryMove(Move("e2e4"));
        var request = await opponent.Next(); var before = session.Snapshot;
        var shutdown = session.DisposeAsync();
        Assert.True(request.Token.IsCancellationRequested);
        request.Reply.SetResult(Move("e7e5"));
        await shutdown;
        Assert.Same(before, session.Snapshot);
        Assert.Equal(MoveResult.Illegal, session.TryMove(Move("e7e5")));
    }

    [Fact]
    public async Task BoardKeysPauseRetryRecoverLocallyAndRequestPersistence()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher, PieceSide.White);
        using var board = new ChessBoardControl(new ChessBoardInteraction(session));
        var saves = 0; var loads = 0;
        board.SaveRequested += () => saves++;
        board.LoadRequested += () => loads++;
        void Key(ConsoleKey key) => board.HandleKey(new ConsoleKeyInfo('\0', key, false, false, false));
        session.StartOpponent();
        var first = await opponent.Next(); var firstTask = session.ActiveSearch;
        Key(ConsoleKey.Enter);
        Assert.Null(board.Interaction.Selected);
        Key(ConsoleKey.Escape);
        Assert.True(first.Token.IsCancellationRequested);
        Key(ConsoleKey.F5); Key(ConsoleKey.F9);
        Assert.Equal(1, saves); Assert.Equal(1, loads);
        Key(ConsoleKey.T);
        var second = await opponent.Next(); var secondTask = session.ActiveSearch;
        Key(ConsoleKey.L);
        first.Reply.SetResult(Move("e2e4")); second.Reply.SetResult(Move("d2d4"));
        await Task.WhenAll(firstTask, secondTask);
        Assert.Null(session.EngineSide);
        Assert.Equal(32, session.Snapshot.Pieces.Count);
        Key(ConsoleKey.Enter); Key(ConsoleKey.UpArrow); Key(ConsoleKey.UpArrow); Key(ConsoleKey.Enter);
        Assert.Equal(new Piece(PieceSide.White, PieceKind.Pawn), session[new Square(4, 3)]);
    }

    [Fact]
    public async Task MinimumBoardLayoutKeepsEngineFailureRecoveryVisible()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher, PieceSide.White);
        using var board = new ChessBoardControl(new ChessBoardInteraction(session));
        session.StartOpponent(); var request = await opponent.Next();
        request.Reply.SetException(new IOException("A long startup error about an unavailable executable"));
        (await dispatcher.Next())(); await session.ActiveSearch;
        var buffer = new CharacterBuffer(48, 25);
        var bounds = new LayoutRect(0, 0, 48, 25);
        board.PaintDOM(buffer, bounds, bounds, Color.White, Color.Black);
        string Row(int y) => string.Concat(Enumerable.Range(0, 48).Select(x => buffer.GetCell(x, y).Character.ToString()));
        Assert.Contains("Stockfish: White", Row(0));
        Assert.Contains("T:retry L:local", Row(21));
        Assert.Contains("F5:save F9:load", Row(23));
    }

    [Fact]
    public async Task LoadingEngineTurnStartsFreshSearchWithRestoredHistory()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher);
        session.TryMove(Move("e2e4"));
        var original = await opponent.Next(); var oldSearch = session.ActiveSearch;
        var saved = session.Export();
        session.Load(saved);
        Assert.True(original.Token.IsCancellationRequested);
        var resumed = await opponent.Next();
        Assert.Equal(new[] { Move("e2e4") }, resumed.Position.Moves);
        Assert.Equal(saved.InitialFen, resumed.Position.InitialFen);
        Assert.True(resumed.Position.Snapshot.Revision > original.Position.Snapshot.Revision);
        original.Reply.SetResult(Move("c7c5")); await oldSearch;
        resumed.Reply.SetResult(Move("e7e5"));
        (await dispatcher.Next())(); await session.ActiveSearch;
        Assert.Equal(PieceSide.White, session.Turn);
        Assert.Equal(new Piece(PieceSide.Black, PieceKind.Pawn), session[new Square(4, 4)]);
        Assert.Null(session[new Square(2, 4)]);
    }

    [Fact]
    public async Task EngineMoveDeclinesHumanDrawOfferAndJournalsTheResult()
    {
        var opponent = new Opponent(); var dispatcher = new Dispatcher();
        await using var session = Session(opponent, dispatcher);
        session.TryMove(Move("e2e4"));
        (await opponent.Next()).Reply.SetResult(Move("e7e5"));
        (await dispatcher.Next())(); await session.ActiveSearch;
        Assert.True(session.OfferDraw());
        session.TryMove(Move("g1f3"));
        Assert.Equal(PieceSide.White, session.DrawOffer);
        (await opponent.Next()).Reply.SetResult(Move("g8f6"));
        (await dispatcher.Next())(); await session.ActiveSearch;
        Assert.Null(session.DrawOffer);
        Assert.Null(session.Outcome);
        Assert.Equal(5, session.Export().Actions.Count);
        Assert.Equal(GameActionKind.OfferDraw, session.Export().Actions[2].Kind);
    }
}
