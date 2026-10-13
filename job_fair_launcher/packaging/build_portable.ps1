[CmdletBinding()]
param(
    [string]$Python = "python",
    [string]$OutputDirectory = "",
    [string]$BuildEnvironment = ""
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $projectRoot ('dist\job-fair-launcher-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Use a new output directory; existing builds are preserved.' }
if (-not $BuildEnvironment) { $BuildEnvironment = Join-Path $PSScriptRoot '.build-venv' }
$venvRoot = [IO.Path]::GetFullPath($BuildEnvironment)
$buildPython = Join-Path $venvRoot 'Scripts\python.exe'
& $Python -c 'import sys, struct; assert sys.platform == "win32" and sys.version_info[:2] == (3,13) and struct.calcsize("P") == 8, "Build with 64-bit Python 3.13 on Windows"'
if ($LASTEXITCODE -ne 0) { throw 'Unsupported build Python.' }
if (-not (Test-Path -LiteralPath $buildPython)) {
    & $Python -m venv $venvRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the dedicated build environment.' }
}
& $buildPython -c 'import sys, struct; assert sys.platform == "win32" and sys.version_info[:2] == (3,13) and struct.calcsize("P") == 8, "Build environment must use 64-bit Python 3.13 on Windows"'
if ($LASTEXITCODE -ne 0) { throw 'Unsupported build environment Python.' }
& $buildPython -m pip install --disable-pip-version-check --requirement (Join-Path $PSScriptRoot 'build_requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Build dependency installation failed.' }
& $buildPython (Join-Path $PSScriptRoot 'entrypoint.py') --self-check --check-ui
if ($LASTEXITCODE -ne 0) { throw 'Source self-check failed.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$inputReceipt = Join-Path $outputRoot 'build-inputs.json'
$receiptScript = Join-Path $PSScriptRoot 'write_build_receipt.py'
& $buildPython $receiptScript --inputs $inputReceipt --capture
if ($LASTEXITCODE -ne 0) { throw 'Build input capture failed.' }
& $buildPython -m PyInstaller --noconfirm --distpath $outputRoot --workpath (Join-Path $outputRoot '.work') (Join-Path $PSScriptRoot 'JobFairLauncher.spec')
if ($LASTEXITCODE -ne 0) { throw 'Portable launcher build failed.' }
$executable = Join-Path $outputRoot 'JobFairLauncher\JobFairLauncher.exe'
$report = Join-Path $outputRoot 'self-check.json'
$process = Start-Process -FilePath $executable -ArgumentList @('--self-check', '--check-ui', '--report-json', ('"' + $report + '"')) -WorkingDirectory $outputRoot -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw 'Frozen launcher self-check failed.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if ($result.status -ne 'passed' -or -not $result.frozen -or $result.ui_check.status -ne 'passed') { throw 'Frozen launcher did not confirm catalog/placement parity and hidden UI.' }
& $buildPython $receiptScript --inputs $inputReceipt --finish (Join-Path $outputRoot 'JobFairLauncher')
if ($LASTEXITCODE -ne 0) { throw 'Build input verification failed.' }
$zip = Join-Path $outputRoot 'JobFairLauncher-Windows-x64.zip'
Compress-Archive -LiteralPath (Join-Path $outputRoot 'JobFairLauncher') -DestinationPath $zip
& $buildPython $receiptScript --inputs $inputReceipt --archive $zip
if ($LASTEXITCODE -ne 0) { throw 'Package receipt generation failed.' }
Write-Output ('Portable folder: ' + (Join-Path $outputRoot 'JobFairLauncher'))
Write-Output ('Share ZIP: ' + $zip)
Write-Output 'Requires a separately installed compatible Job Fair 0.9.5 runtime. FFV was not launched.'
