// Controlled subprocess used only by transport tests. It deliberately implements faulty UCI modes.
var mode = args[0];
var log = args[1];
File.WriteAllText(log + ".pid", Environment.ProcessId.ToString());
using var transcript = new StreamWriter(new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
while (Console.ReadLine() is { } command)
{
    transcript.WriteLine(command);
    if (command == "uci" && mode != "handshake-timeout")
    {
        Console.WriteLine("id name Controlled test engine");
        Console.WriteLine("uciok");
    }
    if (command == "isready" && mode != "ready-timeout") Console.WriteLine("readyok");
    if (command.StartsWith("go "))
    {
        if (mode == "exit") return;
        if (mode is "search-timeout" or "ignore-quit") continue;
        if (mode == "stderr")
            for (var i = 0; i < 1024; i++) Console.Error.WriteLine(new string('x', 256));
        Console.WriteLine("info depth 1 score cp 12 pv e2e4");
        Console.WriteLine(mode switch
        {
            "invalid" => "bestmove banana",
            "none" => "bestmove (none)",
            "null" => "bestmove 0000",
            "empty" => "bestmove",
            "promotion" => "bestmove a7a8n",
            _ => "bestmove e2e4 ponder e7e5"
        });
    }
    if (command == "quit" && mode != "ignore-quit") return;
}

namespace FakeUciEngine { public sealed class Marker; }
