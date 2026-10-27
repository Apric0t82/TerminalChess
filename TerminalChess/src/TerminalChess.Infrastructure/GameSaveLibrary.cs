using System.Globalization;
using TerminalChess.Core.Application;

namespace TerminalChess.Infrastructure;

public sealed record GameSaveEntry(string Path, DateTime Timestamp)
{
    public string Name => Timestamp.ToString("dd MMMM yyyy, HH:mm:ss", CultureInfo.InvariantCulture);
}

public sealed class GameSaveLibrary(string directory)
{
    public IReadOnlyList<GameSaveEntry> List()
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.json")
            .Select(path => new { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) })
            .Select(file => file.Name.StartsWith("game_", StringComparison.Ordinal)
                && DateTime.TryParseExact(file.Name[5..], "MMddyyyy_HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time)
                ? new GameSaveEntry(file.Path, time)
                : file.Name == "game" ? new GameSaveEntry(file.Path, File.GetLastWriteTime(file.Path)) : null)
            .OfType<GameSaveEntry>().OrderByDescending(entry => entry.Timestamp).ToArray();
    }

    public async Task<GameSaveEntry> SaveAsync(SavedGame game, DateTime timestamp, CancellationToken token = default)
    {
        Directory.CreateDirectory(directory);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var path = System.IO.Path.Combine(directory,
                "game_" + timestamp.ToString("MMddyyyy_HHmmss", CultureInfo.InvariantCulture) + ".json");
            try
            {
                await new JsonGameStore(path).SaveAsync(game, token, overwrite: false).ConfigureAwait(false);
                return new(path, timestamp);
            }
            // Reserve another second without replacing an existing save, including across processes.
            catch (IOException) when (File.Exists(path)) { timestamp = timestamp.AddSeconds(1); }
        }
    }
}
