param([Parameter(Mandatory)][string]$GameDir)
$ErrorActionPreference = 'Stop'
if (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue) { throw 'Exit Slay the Spire 2 before installing.' }
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'))) { throw 'Invalid game directory.' }
$source = Join-Path $PSScriptRoot 'build\NeowsCompany'
if (!(Test-Path -LiteralPath (Join-Path $source 'NeowsCompany.dll'))) { throw 'Run build.ps1 first, or unpack the prebuilt package manually.' }
$destination = Join-Path ([IO.Path]::GetFullPath($GameDir)) 'mods\NeowsCompany'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($name in @('NeowsCompany.dll', 'NeowsCompany.json', 'config.example.json', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $destination $name)
}
foreach ($folder in @('docs', 'examples')) {
    Copy-Item -LiteralPath (Join-Path $source $folder) -Destination $destination -Recurse -Force
}
$configuration = Join-Path $destination 'config.json'
if (!(Test-Path -LiteralPath $configuration)) { Copy-Item -LiteralPath (Join-Path $source 'config.example.json') -Destination $configuration }
Write-Output "Installed to $destination; configure config.json and enable the mod in game. Existing config/data preserved."
