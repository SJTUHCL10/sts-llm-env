param(
    [string]$GameDir = $env:STS2_GAME_DIR,
    [string]$OutputDir = (Join-Path $PSScriptRoot 'build\native-audit'),
    [ValidateSet('zhs', 'eng')][string]$Language = 'zhs',
    [string]$Card,
    [string]$DotnetExe = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDir)) { throw 'Set STS2_GAME_DIR or pass -GameDir.' }
$auditArguments = @('run', '--project', (Join-Path $PSScriptRoot 'src\Forge.NativeAudit\Forge.NativeAudit.csproj'),
    '-c', 'Release', "/p:Sts2GameDir=$GameDir", '--', $GameDir, $OutputDir, $Language)
if ($Card) { $auditArguments += $Card }
& $DotnetExe @auditArguments
if ($LASTEXITCODE -ne 0) { throw "Native audit failed with exit code $LASTEXITCODE. Inspect the report if it was written." }
