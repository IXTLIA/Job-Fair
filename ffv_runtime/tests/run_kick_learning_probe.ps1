param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$Compiler = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    [string]$Dotnet = 'C:\Program Files\dotnet\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$probeRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$probeBuild = Join-Path $probeRoot 'build'
[void](New-Item -ItemType Directory -Path $probeBuild -Force)
$probeAssembly = Join-Path $probeBuild 'KickLearningProbe.dll'
$probeSources = @('../KickLearning.cs', '../EquipBellsLearning.cs', 'KickLearningProbe.cs') | ForEach-Object {
    [IO.Path]::GetFullPath((Join-Path $probeRoot $_))
}
$referenceNames = @('System.Private.CoreLib.dll', 'mscorlib.dll', 'System.Runtime.dll',
    'System.Collections.dll', 'System.Console.dll', 'System.Runtime.Extensions.dll', 'netstandard.dll',
    'System.Linq.dll', 'System.Reflection.dll')
$references = @($referenceNames | ForEach-Object {
    (Resolve-Path -LiteralPath (Join-Path $GameRoot ('dotnet/' + $_))).Path
})
$arguments = @('-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-langversion:9',
    '-optimize+', '-deterministic+', '-warnaserror+', ('-out:' + $probeAssembly))
$arguments += @($references | ForEach-Object { '-reference:' + $_ })
$arguments += $probeSources
& $Compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Kick learning fixture compiler exited $LASTEXITCODE" }
'{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.7"},"rollForward":"LatestPatch"}}' |
    Set-Content -LiteralPath (Join-Path $probeBuild 'KickLearningProbe.runtimeconfig.json') -Encoding utf8
$lines = @(& $Dotnet $probeAssembly)
if ($LASTEXITCODE -ne 0) { throw "Kick learning fixture exited $LASTEXITCODE" }
[ordered]@{
    status = 'compiled_and_executed_kick_and_equip_bells_learning_adapters_not_installed'
    native_execution_tested = $false
    assembly_sha256 = (Get-FileHash -LiteralPath $probeAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    sources = @($probeSources | ForEach-Object { [ordered]@{
        path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    } })
    references = @($references | ForEach-Object { [ordered]@{
        path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    } })
    compiler = $Compiler
    compiler_sha256 = (Get-FileHash -LiteralPath $Compiler -Algorithm SHA256).Hash.ToLowerInvariant()
    arguments = $arguments
    output = $lines
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $probeBuild 'kick_learning_probe_results.json') -Encoding utf8
$lines | ForEach-Object { Write-Output $_ }
Write-Output 'Kick/Equip Bells adapter checks passed; no native game execution, installation, configuration or save writes.'
