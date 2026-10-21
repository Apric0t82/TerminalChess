using TerminalChess.Rules;
using System.Text;
using TerminalChess.Core.Domain;
using TerminalChess.Core.Interaction;
using TerminalChess.Tui;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("TerminalChess.Tui [--ascii]\nLocal two-player chess. Drag/click, or Arrows/WASD + Enter/Space.\nPromotion: Q/R/B/N or click a choice; Esc cancels before committing.\nC claims a draw (current position, or selected intended move).\nF offers a draw before moving; opponent answers Y/N. X resigns.\nEsc cancels; R starts a new game; U toggles ASCII; Ctrl+Q quits.\nNo typed UCI moves or engine opponent yet.\nUse an interactive ANSI terminal of at least 48 columns x 25 rows.");
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
    Console.Error.WriteLine("TerminalChess.Tui needs an interactive ANSI terminal. Run from a terminal, or use --help.");
    return 1;
}

Console.OutputEncoding = Encoding.UTF8;
var system = new ConsoleWindowSystem(new NetConsoleDriver(RenderMode.Buffer),
    options: new ConsoleWindowSystemOptions(ShowTopPanel: false, ShowBottomPanel: false));
var board = new ChessBoardControl(new ChessBoardInteraction(new GeraGame()), args.Contains("--ascii"));
var window = new WindowBuilder(system).WithTitle("TerminalChess")
    .Maximized().Frameless().Resizable(false).AddControl(board).Build();
system.AddWindow(window);
window.FocusManager.SetFocus(board, FocusReason.Programmatic);
return system.Run();
