#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
$hostRuntime = "$platform-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())"
if (-not $RuntimeIdentifier) { $RuntimeIdentifier = $hostRuntime }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'stockfish.json') -Raw | ConvertFrom-Json -AsHashtable
$asset = $manifest.assets[$RuntimeIdentifier]
if ($null -eq $asset) { throw "Unsupported runtime: $RuntimeIdentifier" }
if ($IsWindows -and -not $RuntimeIdentifier.StartsWith('win-')) {
    throw 'Build Unix packages on Linux/macOS so executable permissions are preserved.'
}
$artifacts = Join-Path $repository 'artifacts'
$cache = Join-Path $artifacts 'stockfish-cache'
$build = Join-Path $artifacts "publish/$RuntimeIdentifier/$([Guid]::NewGuid().ToString('N'))"
$packageName = "TerminalChess-$RuntimeIdentifier"
$package = Join-Path $build $packageName
$packages = Join-Path $artifacts 'packages'
New-Item -ItemType Directory -Force -Path $cache, $package, $packages | Out-Null

$download = Join-Path $cache $asset.archive
if (-not (Test-Path -LiteralPath $download)) {
    $partial = "$download.$([Guid]::NewGuid().ToString('N')).partial"
    Write-Host "Downloading pinned Stockfish $($manifest.version) for $RuntimeIdentifier..."
    Invoke-WebRequest -Uri ($manifest.downloadBase + $asset.archive) -OutFile $partial
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $asset.sha256) {
        throw "Stockfish checksum mismatch. The untrusted download was not extracted: $partial"
    }
    Move-Item -LiteralPath $partial -Destination $download
}
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $asset.sha256) {
    throw "Cached Stockfish checksum mismatch: $download"
}

Write-Host "Publishing self-contained $RuntimeIdentifier game..."
$project = Join-Path $repository 'TerminalChess/src/TerminalChess.Tui/TerminalChess.Tui.csproj'
& dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true `
    -p:PublishProfile=Bundled -p:DebugType=None -o $package
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

if ($asset.archive.EndsWith('.zip')) {
    Expand-Archive -LiteralPath $download -DestinationPath $package
}
else {
    & tar -xzf $download -C $package
    if ($LASTEXITCODE -ne 0) { throw 'Stockfish archive extraction failed.' }
}
$engineDirectory = Join-Path $package 'stockfish'
$upstreamEngine = Join-Path $engineDirectory $asset.executable
$engineName = if ($RuntimeIdentifier.StartsWith('win-')) { 'stockfish.exe' } else { 'stockfish' }
$engine = Join-Path $engineDirectory $engineName
foreach ($required in @($upstreamEngine, (Join-Path $engineDirectory 'Copying.txt'),
    (Join-Path $engineDirectory 'AUTHORS'), (Join-Path $engineDirectory 'src/Makefile'),
    (Join-Path $engineDirectory 'scripts/net.sh'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Incomplete upstream distribution: $required" }
}
# Keep all upstream sources, scripts, wiki, notices, and embedded networks. Only the filename changes.
Move-Item -LiteralPath $upstreamEngine -Destination $engine
if (-not $IsWindows) {
    & chmod +x $engine (Join-Path $package 'TerminalChess')
    if ($LASTEXITCODE -ne 0) { throw 'Could not preserve executable permissions.' }
}
$provenance = [ordered]@{
    runtimeIdentifier = $RuntimeIdentifier
    stockfishVersion = $manifest.version
    releaseUrl = $manifest.releaseUrl
    archiveUrl = $manifest.downloadBase + $asset.archive
    archiveSha256 = $asset.sha256
    originalExecutable = $asset.executable
    executableSha256 = (Get-FileHash -LiteralPath $engine -Algorithm SHA256).Hash.ToLowerInvariant()
}
$provenance | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding utf8
@"
Stockfish $($manifest.version) is included as a separate, unmodified UCI subprocess.
Only its executable filename was changed to $engineName.
Copyright belongs to the Stockfish contributors listed in stockfish/AUTHORS.
License: GNU GPL version 3; see stockfish/Copying.txt.
The distributed source, build scripts and instructions are included in stockfish/src,
stockfish/scripts and stockfish/wiki. The NNUE networks are embedded in the engine;
their exact names and retrieval instructions are in src/evaluate.h and scripts/net.sh.
Official release and corresponding source: $($manifest.releaseUrl)
Full source tree: https://github.com/official-stockfish/Stockfish/tree/$($manifest.tag)
Checksums and the original download URL are recorded in package.json.
Keep these sources, licenses and notices when redistributing this package.
"@ | Set-Content -LiteralPath (Join-Path $package 'STOCKFISH-NOTICE.txt') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $package
Copy-Item -LiteralPath (Join-Path $repository 'TerminalChess/THIRD-PARTY-NOTICES.md') -Destination $package
& (Join-Path $PSScriptRoot 'Export-Notices.ps1') `
    -AssetsFile (Join-Path (Split-Path -Parent $project) 'obj/project.assets.json') `
    -PackageDirectory $package -RuntimeIdentifier $RuntimeIdentifier
@'
Start TerminalChess.exe (Windows) or ./TerminalChess (Linux/macOS) in an ANSI terminal.
No .NET or Stockfish installation is required. Keep the stockfish folder beside the game.
Default: human White versus bundled Stockfish. --human black changes side; --local is two-player.
Choose Beginner, Intermediate, Advanced or Expert on the centered startup screen, then Enter.
--help lists all controls. F5 saves, F9 loads, Ctrl+Q quits.
The game needs at least 48 columns by 25 rows. No downloads occur while playing.
'@ | Set-Content -LiteralPath (Join-Path $package 'START-HERE.txt') -Encoding utf8

if ($RuntimeIdentifier -eq $hostRuntime) {
    & (Join-Path $PSScriptRoot 'Test-Release.ps1') -PackageDirectory $package
}
else { Write-Host "Cross-published $RuntimeIdentifier; run Test-Release.ps1 on its target OS/architecture." }

if ($RuntimeIdentifier.StartsWith('win-')) {
    $archive = Join-Path $packages "$packageName.zip"
    Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
}
else {
    $archive = Join-Path $packages "$packageName.tar.gz"
    & tar -czf $archive -C $build $packageName
    if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }
}
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Host "Package: $archive"
Write-Host "Unpacked game: $package"
