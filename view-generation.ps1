param(
    [string]$Directory,
    [string]$GameDir = $env:STS2_GAME_DIR,
    [string]$PythonExe,
    [int]$Port = 8765,
    [switch]$NoBrowser
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Directory)) {
    $Directory = if (![string]::IsNullOrWhiteSpace($GameDir)) { $GameDir } else { Join-Path $PSScriptRoot 'data\generation' }
}
if ([string]::IsNullOrWhiteSpace($PythonExe)) {
    $candidates = @('python', 'python3', 'py', (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'))
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -eq $command -or $command.Source -like '*\Microsoft\WindowsApps\*') { continue }
        $version = & $command.Source -c 'import sys; print(sys.version_info >= (3, 10))' 2>$null
        if ($LASTEXITCODE -eq 0 -and $version -eq 'True') { $PythonExe = $command.Source; break }
    }
    if ([string]::IsNullOrWhiteSpace($PythonExe)) { throw 'Python 3.10+ was not found. Install Python or pass -PythonExe with its executable path.' }
}
$viewerArguments = @((Join-Path $PSScriptRoot 'tools\view_generation.py'), $Directory, '--port', [string]$Port)
if ($NoBrowser) { $viewerArguments += '--no-browser' }
& $PythonExe @viewerArguments
if ($LASTEXITCODE -ne 0) { throw "Generation viewer exited with code $LASTEXITCODE." }
