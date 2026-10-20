using System.Text;
using Chess.Core.Domain;
using Chess.Core.Interaction;
using Chess.Tui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Chess.Tui (Terminal.Gui) [--ascii]\nDrag or click source then destination. Arrows/WASD + Enter/Space.\nEsc cancels; R resets; U toggles ASCII; Ctrl+Q quits.\nTemporary arrangement board: no chess rules, turns, or AI.\nUse an interactive ANSI terminal of at least 48 columns x 25 rows.");
    return 0;
}
if (args.Any(arg => arg != "--ascii"))
{
    Console.Error.WriteLine("Unknown argument. Use --help for usage.");
    return 2;
}
if (Console.IsInputRedirected || Console.IsOutputRedirected
    || Environment.GetEnvironmentVariable("TERM") == "dumb")
{
    Console.Error.WriteLine("Chess.Tui needs an interactive ANSI terminal. Run from a terminal, or use --help.");
    return 1;
}

Console.OutputEncoding = Encoding.UTF8;
using var app = Application.Create();
app.Init();
using var root = new Runnable { Title = "Chess prototype / Terminal.Gui" };
var board = new ChessBoardControl(new ChessBoardInteraction(new GameState()), args.Contains("--ascii"));
root.Add(board);
board.SetFocus();
app.Run(root);
return 0;
