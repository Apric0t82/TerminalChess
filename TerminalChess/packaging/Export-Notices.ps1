#requires -Version 7.2
param(
    [Parameter(Mandatory)][string] $AssetsFile,
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
function Find-Package([string] $relative) {
    foreach ($root in $assets.packageFolders.Keys) {
        $candidate = Join-Path $root $relative
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw "Cannot locate restored package: $relative"
}
$licenses = Join-Path $PackageDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
$inventory = @('# Resolved NuGet dependencies', '',
    'Original package metadata and supplied notices are retained in the adjacent package folders.',
    'Shared MIT and BSD-2-Clause terms are in TERMS.txt. Application rules notices are also in THIRD-PARTY-NOTICES.md.', '')
foreach ($entry in $assets.libraries.GetEnumerator()) {
    if ($entry.Value.type -ne 'package') { continue }
    $package = Find-Package $entry.Value.path
    $spec = Get-ChildItem -LiteralPath $package -Filter '*.nuspec' | Select-Object -First 1
    [xml] $xml = Get-Content -LiteralPath $spec.FullName -Raw
    $metadata = $xml.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    $destination = Join-Path $licenses $entry.Key
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath $spec.FullName -Destination $destination
    foreach ($file in $entry.Value.files) {
        if ($file -notmatch '(?i)(license|copying|notice)') { continue }
        $source = Join-Path $package $file
        $target = Join-Path $destination $file
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    $inventory += "## $($entry.Key)"
    foreach ($field in @('authors', 'copyright', 'license', 'licenseUrl', 'projectUrl', 'repository')) {
        $node = $metadata.SelectSingleNode("*[local-name()='$field']")
        if ($null -ne $node) { $inventory += $node.OuterXml }
    }
    $inventory += ''
}
$inventory | Set-Content -LiteralPath (Join-Path $licenses 'DEPENDENCIES.md') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TERMS.txt') -Destination $licenses
# The single-file bundle embeds native runtime files; retain the runtime's original notices externally.
$runtimeId = "Microsoft.NETCore.App.Runtime.$RuntimeIdentifier"
$download = @($assets.project.frameworks.Values | ForEach-Object { $_.downloadDependencies } |
    Where-Object { $_.name -eq $runtimeId }) | Select-Object -First 1
if ($null -eq $download) { throw "Cannot locate runtime license metadata for $runtimeId" }
$version = $download.version.Trim('[', ']').Split(',')[0].Trim()
$runtime = Find-Package "$($runtimeId.ToLowerInvariant())/$version"
$destination = Join-Path $licenses 'dotnet-runtime'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath (Join-Path $runtime 'LICENSE.TXT') -Destination $destination
Copy-Item -LiteralPath (Join-Path $runtime 'THIRD-PARTY-NOTICES.TXT') -Destination $destination

