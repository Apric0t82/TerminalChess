#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Read-UciUntil([string] $pattern) {
    while ($true) {
        $line = $process.StandardOutput.ReadLineAsync().WaitAsync($deadline.Token).GetAwaiter().GetResult()
        if ($null -eq $line) { throw 'Bundled Stockfish exited unexpectedly.' }
        if ($line -match $pattern) { return $line }
    }
}
$package = [IO.Path]::GetFullPath($PackageDirectory)
$metadata = Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json
$platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
$hostRuntime = "$platform-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())"
if ($metadata.runtimeIdentifier -ne $hostRuntime) {
    throw 'Run this smoke check on the package target OS/architecture.'
}
$executableName = if ($IsWindows) { 'TerminalChess.exe' } else { 'TerminalChess' }
$engineName = if ($IsWindows) { 'stockfish.exe' } else { 'stockfish' }
$engine = Join-Path $package "stockfish/$engineName"
if ((Get-FileHash -LiteralPath $engine -Algorithm SHA256).Hash -ne $metadata.executableSha256) {
    throw 'Packaged Stockfish binary checksum mismatch.'
}
foreach ($required in @('stockfish/Copying.txt', 'stockfish/AUTHORS', 'stockfish/src/Makefile', 'STOCKFISH-NOTICE.txt',
    'licenses/dotnet-runtime/LICENSE.TXT', 'licenses/dotnet-runtime/THIRD-PARTY-NOTICES.TXT', 'licenses/DEPENDENCIES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $required))) { throw "Missing package file: $required" }
}
$scratch = Join-Path ([IO.Path]::GetTempPath()) "terminalchess-release-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
# Launch outside the application directory, with a deliberately nonexistent .NET installation.
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $package $executableName))
$start.ArgumentList.Add('--help')
$start.WorkingDirectory = $scratch
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_ROOT'] = Join-Path $scratch 'no-runtime'
$start.Environment['DOTNET_ROOT_X64'] = Join-Path $scratch 'no-runtime'
$start.Environment['DOTNET_ROOT_ARM64'] = Join-Path $scratch 'no-runtime'
$start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $scratch 'bundle'
$app = [Diagnostics.Process]::new()
$app.StartInfo = $start
try {
    if (-not $app.Start()) { throw 'Could not start the published executable.' }
    $output = $app.StandardOutput.ReadToEndAsync()
    $errors = $app.StandardError.ReadToEndAsync()
    if (-not $app.WaitForExit(15000)) { throw 'Published executable startup timed out.' }
    if ($app.ExitCode -ne 0) { throw "Published executable failed: $($errors.GetAwaiter().GetResult())" }
    if ($output.GetAwaiter().GetResult() -notmatch 'Play bundled Stockfish by default') { throw 'Unexpected executable help.' }
}
finally {
    if ($app.Id -and -not $app.HasExited) { $app.Kill($true); $app.WaitForExit() }
    $app.Dispose()
}

$start = [Diagnostics.ProcessStartInfo]::new($engine)
$start.WorkingDirectory = $scratch
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
try {
    if (-not $process.Start()) { throw 'Could not start bundled Stockfish.' }
    $errors = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.AutoFlush = $true
    $process.StandardInput.WriteLine('uci')
    $null = Read-UciUntil '^uciok$'
    $process.StandardInput.WriteLine('setoption name Threads value 1')
    $process.StandardInput.WriteLine('isready')
    $null = Read-UciUntil '^readyok$'
    $process.StandardInput.WriteLine('position startpos')
    $process.StandardInput.WriteLine('go movetime 100')
    $move = Read-UciUntil '^bestmove [a-h][1-8][a-h][1-8][qrbn]?( |$)'
    Write-Host "Native package smoke passed: self-contained startup and bundled engine $move"
}
finally {
    try {
        if ($process.Id -and -not $process.HasExited) {
            $process.StandardInput.WriteLine("stop`nquit")
            if (-not $process.WaitForExit(2000)) { $process.Kill($true); $process.WaitForExit() }
        }
    }
    finally { $process.Dispose(); $deadline.Dispose() }
}

