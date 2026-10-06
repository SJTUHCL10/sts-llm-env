param(
    [string]$GameDir = $env:STS2_GAME_DIR,
    [string]$DotnetExe = 'dotnet',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDir) -or !(Test-Path -LiteralPath (Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'))) {
    throw 'Set STS2_GAME_DIR or pass -GameDir with a Slay the Spire 2 v0.111.0 installation.'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
function Invoke-Dotnet([string[]]$TaskArguments) {
    & $DotnetExe @TaskArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Push-Location $PSScriptRoot
try {
    if (!$SkipTests) {
        Invoke-Dotnet @('run', '--project', 'tests\Forge.Tests\Forge.Tests.csproj', '-c', 'Release', '--', (Join-Path $PSScriptRoot '.dotnet\test-tmp'))
        Invoke-Dotnet @('run', '--project', 'tests\Forge.GameSmoke\Forge.GameSmoke.csproj', '-c', 'Release', "/p:Sts2GameDir=$GameDir", '--', $GameDir)
    }
    Invoke-Dotnet @('build', 'src\Forge.Mod\Forge.Mod.csproj', '-c', 'Release', "/p:Sts2GameDir=$GameDir", '--nologo')
    Invoke-Dotnet @('run', '--project', 'src\Forge.Tool\Forge.Tool.csproj', '-c', 'Release', '--', 'config', (Join-Path $PSScriptRoot 'config.example.json'))
    $package = Join-Path $PSScriptRoot 'build\NeowsCompany'
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    Copy-Item -LiteralPath 'src\Forge.Mod\bin\Release\net9.0\NeowsCompany.dll' -Destination $package
    Copy-Item -LiteralPath 'src\Forge.Mod\mod_manifest.json' -Destination (Join-Path $package 'NeowsCompany.json')
    Copy-Item -LiteralPath 'config.example.json' -Destination $package
    Copy-Item -LiteralPath 'README.md' -Destination $package
    $packagedDocs = Join-Path $package 'docs'
    if (Test-Path -LiteralPath $packagedDocs) {
        $resolvedDocs = (Resolve-Path -LiteralPath $packagedDocs).Path
        $expectedDocs = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'build\NeowsCompany\docs'))
        if ($resolvedDocs -ne $expectedDocs) { throw 'Packaged docs resolved outside the expected workspace path.' }
        Remove-Item -LiteralPath $packagedDocs -Recurse -Force
    }
    Copy-Item -LiteralPath 'docs' -Destination $package -Recurse
    New-Item -ItemType Directory -Force -Path (Join-Path $package 'examples') | Out-Null
    Get-ChildItem -LiteralPath 'examples' -Filter '*.json' | Copy-Item -Destination (Join-Path $package 'examples')
    Compress-Archive -LiteralPath $package -DestinationPath (Join-Path $PSScriptRoot 'build\NeowsCompany-0.1.0.zip') -Force
    Write-Output "Built package: $package"
}
finally { Pop-Location }
