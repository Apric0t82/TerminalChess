using System.Text.Json;
using System.Text.Json.Serialization;
using TerminalChess.Core.Application;
using TerminalChess.Core.Domain;

namespace TerminalChess.Infrastructure;

public sealed class JsonGameStore(string path)
{
    private sealed record StoredAction(GameActionKind Kind, string? Move);
    private sealed record Document(int Version, string InitialFen, PieceSide? EngineSide, StoredAction[] Actions);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public async Task SaveAsync(SavedGame game, CancellationToken cancellationToken = default)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var document = new Document(game.Version, game.InitialFen, game.EngineSide,
                game.Actions.Select(a => new StoredAction(a.Kind, a.Move is { } move ? UciMove.Format(move) : null)).ToArray());
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, Path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<SavedGame> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Saved game is too large.");
            var document = await JsonSerializer.DeserializeAsync<Document>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (document is null || document.Version != 1 || string.IsNullOrWhiteSpace(document.InitialFen)
                || document.Actions is null || document.Actions.Any(a => a is null))
                throw new InvalidDataException("Unsupported or incomplete saved game.");
            return new(document.Version, document.InitialFen, document.EngineSide,
                document.Actions.Select(a => new GameAction(a.Kind, a.Move is null ? null : UciMove.Parse(a.Move))).ToArray());
        }
        catch (JsonException ex) { throw new InvalidDataException("Saved game is not valid TerminalChess JSON.", ex); }
    }
}
