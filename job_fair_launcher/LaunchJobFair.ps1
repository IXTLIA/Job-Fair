$ErrorActionPreference = 'Stop'
$launcherRoot = $PSScriptRoot
# A downloaded launcher package includes its own Python/Tk runtime.
$packagedLauncher = Join-Path $launcherRoot 'JobFairLauncher.exe'
if (Test-Path -LiteralPath $packagedLauncher -PathType Leaf) {
    Start-Process -FilePath $packagedLauncher -WorkingDirectory $launcherRoot -WindowStyle Normal
    return
}
$pythonWindowed = Join-Path (Split-Path -Parent $launcherRoot) 'ffv_randomizer\.venv\Scripts\pythonw.exe'
$launcherScript = Join-Path $launcherRoot 'launcher.py'
if (-not (Test-Path -LiteralPath $pythonWindowed -PathType Leaf)) {
    $pythonCommand = Get-Command pythonw.exe -ErrorAction SilentlyContinue
    if ($pythonCommand) {
        $pythonWindowed = $pythonCommand.Source
    } else {
        $pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
        if (-not $pythonCommand) {
            throw 'Install Python 3.11 or newer with Tcl/Tk, or use the packaged JobFairLauncher.exe. See README.md.'
        }
        $pythonWindowed = Join-Path (Split-Path -Parent $pythonCommand.Source) 'pythonw.exe'
        if (-not (Test-Path -LiteralPath $pythonWindowed -PathType Leaf)) {
            throw 'Python with pythonw.exe and Tcl/Tk is required. Use the packaged launcher or see README.md.'
        }
    }
}
# pythonw creates no console. This is the user's visible interactive launcher;
# do not hide the process's first Tk window along with a nonexistent console.
Start-Process -FilePath $pythonWindowed -ArgumentList ('"' + $launcherScript + '"') -WorkingDirectory $launcherRoot -WindowStyle Normal
