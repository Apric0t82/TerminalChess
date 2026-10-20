using System.Text;
using Chess.Core.Domain;
using Chess.Core.Interaction;
using Chess.Tui;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Chess.Tui [--ascii]\nDrag or click source then destination. Arrows/WASD + Enter/Space.\nEsc cancels; R resets; U toggles ASCII; Ctrl+Q quits.\nTemporary arrangement board: no chess rules, turns, or AI.\nUse an interactive ANSI terminal of at least 48 columns x 25 rows.");
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
var system = new ConsoleWindowSystem(new NetConsoleDriver(RenderMode.Buffer),
    options: new ConsoleWindowSystemOptions(ShowTopPanel: false, ShowBottomPanel: false));
var board = new ChessBoardControl(new ChessBoardInteraction(new GameState()), args.Contains("--ascii"));
var window = new WindowBuilder(system).WithTitle("Chess prototype")
    .Maximized().Frameless().Resizable(false).AddControl(board).Build();
system.AddWindow(window);
window.FocusManager.SetFocus(board, FocusReason.Programmatic);
return system.Run();
