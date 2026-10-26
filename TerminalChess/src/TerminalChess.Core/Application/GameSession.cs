using TerminalChess.Core.Domain;

namespace TerminalChess.Core.Application;

public sealed record OpponentPosition(PositionSnapshot Snapshot, string InitialFen, IReadOnlyList<MoveIntent> Moves,
    OpponentDifficulty Difficulty = OpponentDifficulty.Intermediate);

public interface IChessOpponent
{
    Task<MoveIntent> GetMoveAsync(OpponentPosition position, CancellationToken cancellationToken);
}

public enum GameActionKind { Move, ClaimDraw, OfferDraw, AcceptDraw, DeclineDraw, Resign }
public sealed record GameAction(GameActionKind Kind, MoveIntent? Move = null);
public sealed record SavedGame(int Version, string InitialFen, PieceSide? EngineSide, IReadOnlyList<GameAction> Actions,
    OpponentDifficulty Difficulty = OpponentDifficulty.Intermediate);

/// <summary>
/// Serializes human and opponent transitions on the caller's UI thread. Background work
/// only consumes immutable values; dispatch must return work to that same thread.
/// </summary>
public sealed class GameSession : IChessGame, IAsyncDisposable
{
    private IChessGame _game;
    private readonly IChessOpponent _opponent;
    private readonly Func<Action, Task> _dispatch;
    private readonly Func<string, IChessGame> _restorePosition;
    private readonly List<GameAction> _actions = [];
    private readonly List<Task> _searches = [];
    private CancellationTokenSource? _search;
    private string _initialFen;
    private long _revision;
    private bool _disposed;
    private bool _opponentReady;

    public GameSession(IChessGame game, IChessOpponent opponent, Func<Action, Task> dispatch,
        Func<string, IChessGame> restorePosition, PieceSide? engineSide = null,
        OpponentDifficulty difficulty = OpponentDifficulty.Intermediate, bool deferOpponent = false)
    {
        _game = game;
        _opponent = opponent;
        _dispatch = dispatch;
        _restorePosition = restorePosition;
        _initialFen = game.Snapshot.Fen;
        EngineSide = engineSide;
        _ = OpponentStrength.For(difficulty);
        Difficulty = difficulty;
        _opponentReady = !deferOpponent;
        Snapshot = game.Snapshot with { Revision = ++_revision };
    }

    public event Action? Changed;
    public PieceSide? EngineSide { get; private set; }
    public OpponentDifficulty Difficulty { get; private set; }
    public bool IsHumanTurn => EngineSide != Turn;
    public bool IsThinking => _search is not null;
    public string Status { get; private set; } = "";
    public Task ActiveSearch { get; private set; } = Task.CompletedTask;
    public Piece? this[Square square] => _game[square];
    public PieceSide Turn => _game.Turn;
    public bool IsCheck => _game.IsCheck;
    public GameOutcome? Outcome => _game.Outcome;
    public IReadOnlyList<MoveIntent> LegalMoves => _game.LegalMoves;
    public PositionSnapshot Snapshot { get; private set; }
    public bool CanClaimDraw => _game.CanClaimDraw;
    public PieceSide? DrawOffer => _game.DrawOffer;
    public bool CanMove(Square from, Square to) => IsHumanTurn && _game.CanMove(from, to);

    public MoveResult TryMove(MoveIntent move)
    {
        if (_disposed || !IsHumanTurn) return MoveResult.Illegal;
        var result = _game.TryMove(move);
        if (result == MoveResult.Applied) Commit(new(GameActionKind.Move, move));
        return result;
    }

    public bool ClaimDraw(MoveIntent? intendedMove = null) => Apply(new(GameActionKind.ClaimDraw, intendedMove));
    public bool OfferDraw() => Apply(new(GameActionKind.OfferDraw));
    public bool AcceptDraw() => Apply(new(GameActionKind.AcceptDraw));
    public void DeclineDraw() => Apply(new(GameActionKind.DeclineDraw));
    public void Resign() => Apply(new(GameActionKind.Resign));

    private bool Apply(GameAction action)
    {
        if (_disposed || !IsHumanTurn || !ReplayAction(_game, action)) return false;
        Commit(action);
        return true;
    }

    private void Commit(GameAction action)
    {
        _actions.Add(action);
        Publish();
        StartOpponent();
    }

    private void Publish()
    {
        Snapshot = _game.Snapshot with { Revision = ++_revision };
        Status = "";
        Changed?.Invoke();
    }

    public void Reset()
    {
        if (_disposed) return;
        CancelOpponent();
        _game.Reset();
        _initialFen = _game.Snapshot.Fen;
        _actions.Clear();
        Publish();
        StartOpponent();
    }

    public SavedGame Export() => new(2, _initialFen, EngineSide, Array.AsReadOnly(_actions.ToArray()), Difficulty);

    public void ConfigureDifficulty(OpponentDifficulty difficulty)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = OpponentStrength.For(difficulty);
        CancelOpponent();
        Difficulty = difficulty;
        _opponentReady = true;
        Publish();
        StartOpponent();
    }

    public sealed class PreparedGame
    {
        private IChessGame? _game;
        internal SavedGame Saved { get; }
        internal PreparedGame(IChessGame game, SavedGame saved) { _game = game; Saved = saved; }
        internal IChessGame TakeGame() => Interlocked.Exchange(ref _game, null)
            ?? throw new InvalidOperationException("Prepared game has already been loaded.");
    }

    /// <summary>Replays in isolation; safe to run on a worker so a long journal cannot stall input.</summary>
    public PreparedGame PrepareLoad(SavedGame saved, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (saved.Version is not (1 or 2) || !Enum.IsDefined(saved.Difficulty)
            || string.IsNullOrWhiteSpace(saved.InitialFen) || saved.Actions is null
            || saved.EngineSide is { } side && !Enum.IsDefined(side))
            throw new InvalidDataException("Unsupported or invalid saved game.");
        saved = saved with { Actions = Array.AsReadOnly(saved.Actions.ToArray()) };
        // Validate the complete journal in isolation before replacing the current game.
        var restored = _restorePosition(saved.InitialFen);
        foreach (var action in saved.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action is null || !ReplayAction(restored, action))
                throw new InvalidDataException("Saved game contains an invalid action.");
        }
        return new(restored, saved);
    }

    public void Load(SavedGame saved) => Load(PrepareLoad(saved));

    /// <summary>Transfers a prepared position to the serialized UI owner.</summary>
    public void Load(PreparedGame prepared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var saved = prepared.Saved;
        var restored = prepared.TakeGame();
        CancelOpponent();
        _game = restored;
        _initialFen = saved.InitialFen;
        EngineSide = saved.EngineSide;
        Difficulty = saved.Difficulty;
        _actions.Clear();
        _actions.AddRange(saved.Actions);
        Publish();
        StartOpponent();
    }

    private static bool ReplayAction(IChessGame game, GameAction action)
    {
        if (action.Kind != GameActionKind.Move && action.Kind != GameActionKind.ClaimDraw && action.Move is not null)
            return false;
        switch (action.Kind)
        {
            case GameActionKind.Move: return action.Move is { } move && game.TryMove(move) == MoveResult.Applied;
            case GameActionKind.ClaimDraw: return game.ClaimDraw(action.Move);
            case GameActionKind.OfferDraw: return game.OfferDraw();
            case GameActionKind.AcceptDraw: return game.AcceptDraw();
            case GameActionKind.DeclineDraw:
                if (game.DrawOffer is null || game.DrawOffer == game.Turn || game.Outcome is not null) return false;
                game.DeclineDraw(); return true;
            case GameActionKind.Resign:
                if (game.Outcome is not null) return false;
                game.Resign(); return true;
            default: return false;
        }
    }

    public void CancelOpponent()
    {
        var search = _search;
        _search = null;
        search?.Cancel();
        Status = EngineSide == Turn && Outcome is null ? "Engine paused. T: retry / L: local play" : "";
        Changed?.Invoke();
    }

    public void UseLocalPlayers()
    {
        CancelOpponent();
        EngineSide = null;
        Status = "Local two-player game.";
        Changed?.Invoke();
    }

    public void StartOpponent()
    {
        if (_disposed || !_opponentReady || IsHumanTurn || Outcome is not null || IsThinking) return;
        var search = new CancellationTokenSource();
        _search = search;
        var position = new OpponentPosition(Snapshot, _initialFen,
            Array.AsReadOnly(_actions.Where(a => a.Kind == GameActionKind.Move).Select(a => a.Move!.Value).ToArray()), Difficulty);
        Status = "Stockfish thinking... Esc: pause";
        Changed?.Invoke();
        _searches.RemoveAll(task => task.IsCompleted);
        ActiveSearch = SearchAsync(position, search);
        _searches.Add(ActiveSearch);
    }

    private async Task SearchAsync(OpponentPosition position, CancellationTokenSource search)
    {
        try
        {
            // Process startup and all opponent I/O run off the UI thread, even before the first await.
            var move = await Task.Run(() => _opponent.GetMoveAsync(position, search.Token), search.Token).ConfigureAwait(false);
            search.Token.ThrowIfCancellationRequested();
            await _dispatch(() =>
            {
                if (!IsCurrent(position, search)) return;
                _search = null;
                if (_game.TryMove(move) == MoveResult.Applied)
                {
                    _actions.Add(new(GameActionKind.Move, move));
                    Publish();
                }
                else Fail("Engine proposed an illegal move.");
            }).WaitAsync(search.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (search.IsCancellationRequested) return;
            try
            {
                await _dispatch(() =>
                {
                    if (!IsCurrent(position, search)) return;
                    _search = null;
                    Fail($"Engine unavailable: {ex.Message}");
                }).WaitAsync(search.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (search.IsCancellationRequested) { }
        }
        finally { search.Dispose(); }
    }

    private bool IsCurrent(OpponentPosition position, CancellationTokenSource search) =>
        !_disposed && !search.IsCancellationRequested && ReferenceEquals(_search, search)
        && Snapshot.Revision == position.Snapshot.Revision && !IsHumanTurn && Outcome is null;

    private void Fail(string message)
    {
        Status = $"{message} T: retry / L: local play";
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelOpponent();
        await Task.WhenAll(_searches).ConfigureAwait(false);
    }
}
