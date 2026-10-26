using System.ComponentModel;
using System.Diagnostics;
using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;

namespace TerminalChess.Infrastructure;

/// <summary>A fresh, bounded UCI process per search prevents output from leaking into a later search.</summary>
public sealed class StockfishOpponent : IChessOpponent
{
    private readonly string _executable;
    private readonly string[] _arguments;
    private readonly TimeSpan? _timeout;

    public StockfishOpponent(string executable, TimeSpan? timeout = null,
        IEnumerable<string>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = executable;
        _arguments = arguments?.ToArray() ?? [];
        _timeout = timeout;
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<MoveIntent> GetMoveAsync(OpponentPosition position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var strength = OpponentStrength.For(position.Difficulty);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout ?? TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(strength.MoveTimeMs));
        var token = deadline.Token;
        var start = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in _arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        Task stderr = Task.CompletedTask;
        var started = false;
        try
        {
            try { started = process.Start(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            { throw new IOException($"Cannot start Stockfish '{_executable}'. Check its path and executable permissions.", ex); }
            if (!started) throw new IOException("Stockfish failed to start.");
            // Drain stderr continuously without retaining unbounded diagnostic output.
            stderr = DrainAsync(process.StandardError);
            await SendAsync("uci");
            await ReadUntilAsync(line => line == "uciok");
            await SendAsync("setoption name Threads value 1");
            await SendAsync("setoption name Hash value 16");
            await SendAsync("setoption name UCI_LimitStrength value false");
            await SendAsync($"setoption name Skill Level value {strength.SkillLevel}");
            await SendAsync("ucinewgame");
            await SendAsync("isready");
            await ReadUntilAsync(line => line == "readyok");
            if (position.InitialFen.Contains('\n') || position.InitialFen.Contains('\r'))
                throw new InvalidDataException("Invalid position.");
            // Sending the complete move history allows Stockfish to recognize repetition.
            var history = position.Moves.Count == 0 ? "" : " moves " + string.Join(' ', position.Moves.Select(UciMove.Format));
            await SendAsync($"position fen {position.InitialFen}{history}");
            await SendAsync($"go movetime {strength.MoveTimeMs}");
            var best = await ReadUntilAsync(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == "bestmove");
            var fields = best.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return UciMove.Parse(fields.Length >= 2 ? fields[1] : "");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Stockfish did not respond before the engine deadline."); }
        finally
        {
            if (started)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        // Give cooperative engines a short chance to stop and exit, then kill the tree.
                        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                        await process.StandardInput.WriteLineAsync("stop\nquit".AsMemory(), shutdown.Token).ConfigureAwait(false);
                        await process.StandardInput.FlushAsync(shutdown.Token).ConfigureAwait(false);
                        await process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException) { }
                finally
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception && process.HasExited) { }
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    await stderr.ConfigureAwait(false);
                }
            }
        }

        async Task SendAsync(string command)
        {
            await process.StandardInput.WriteLineAsync(command.AsMemory(), token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }

        async Task<string> ReadUntilAsync(Func<string, bool> predicate)
        {
            while (await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                if (predicate(line.Trim())) return line.Trim();
            throw new IOException("Stockfish exited before returning a result.");
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0) { }
    }
}
