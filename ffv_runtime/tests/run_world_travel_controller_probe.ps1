param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [string]$Compiler='C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
 [string]$Dotnet='C:\Program Files\dotnet\dotnet.exe'
)
$ErrorActionPreference='Stop'
$warpTestRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$warpOutput=Join-Path $warpTestRoot 'build'
[void](New-Item -ItemType Directory -Path $warpOutput -Force)
$warpAssembly=Join-Path $warpOutput 'WorldTravelControllerProbe.dll'
$warpSources=@('SeedLogic.cs','RunMetadata.cs','BossCatalog.cs','NativeBossData.cs','WorldTravelState.cs','SaveSeedCodec.cs','NextRunConfig.cs','NativeWorldTravelData.cs','WorldTravelTransaction.cs','WorldTravelRuntime.cs') | ForEach-Object {[IO.Path]::GetFullPath((Join-Path $warpTestRoot ('../'+$_)))}
$warpSources+=Join-Path $warpTestRoot 'WorldTravelRuntimeProbe.cs'
$warpSources+=Join-Path $warpTestRoot 'WorldTravelControllerProbe.cs'
$warpSources+=@('WorldTravelControls.cs','WorldTravelController.cs','WorldTravelView.cs') | ForEach-Object {[IO.Path]::GetFullPath((Join-Path $warpTestRoot ('../'+$_)))}
$warpRefs=@('System.Private.CoreLib.dll','mscorlib.dll','System.Runtime.dll','System.Collections.dll','System.Security.Cryptography.Algorithms.dll','System.Security.Cryptography.Primitives.dll','System.Console.dll','System.Runtime.Extensions.dll','netstandard.dll','System.Text.Json.dll','System.Text.Encodings.Web.dll','System.Memory.dll','System.Buffers.dll','System.IO.dll','System.Reflection.dll') | ForEach-Object {(Resolve-Path -LiteralPath (Join-Path $GameRoot ('dotnet/'+$_))).Path}
$warpArgs=@('-nologo','-noconfig','-nostdlib+','-target:exe','-define:CONTROLLER_PROBE','-main:WorldTravelControllerProbe','-langversion:9','-optimize+','-deterministic+','-warnaserror+',('-out:'+$warpAssembly))
$warpArgs+=@($warpRefs|ForEach-Object {'-reference:'+$_});$warpArgs+=$warpSources
& $Compiler @warpArgs
if($LASTEXITCODE -ne 0){throw "Compiler exited $LASTEXITCODE"}
'{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.7"},"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $warpOutput 'WorldTravelControllerProbe.runtimeconfig.json') -Encoding UTF8
$warpModule=[IO.Path]::GetFullPath((Join-Path $warpTestRoot '../../ffv_randomizer'))
$warpPaths=@('travel_runtime_seed8.json','travel_runtime_seed90.json','playtest_runtime_seed2.json','playtest_runtime_seed8.json','playtest_runtime_seed90.json',
    'fixed_travel_runtime_seed2.json','fixed_travel_runtime_seed90.json')|ForEach-Object {Join-Path $warpOutput $_}
$warpCode=@'
import json,sys
from pathlib import Path
from ffv_randomizer.campaign import build_world_travel_slice,build_remixed_playtest,build_fixed_karnak,REMIXED
requests=[build_world_travel_slice(s) for s in ('8','90')]+[build_remixed_playtest(s) for s in ('2','8','90')]
requests += [build_fixed_karnak(s,REMIXED) for s in ('2','90')]
for request,path in zip(requests,sys.argv[1:],strict=True):
 Path(path).write_text(json.dumps(request,indent=2)+'\n',encoding='utf-8')
'@
Push-Location -LiteralPath $warpModule
try {& (Join-Path $warpModule '.venv/Scripts/python.exe') -c $warpCode @warpPaths;if($LASTEXITCODE -ne 0){throw 'Generator failed.'}}finally{Pop-Location}
$warpLines=@(& $Dotnet $warpAssembly $warpPaths[2])
if($LASTEXITCODE -ne 0){throw "Adapter fixture exited $LASTEXITCODE"}
[ordered]@{
 status='actual_managed_adapter_with_fake_native_objects_not_gameplay_verified'
 assembly_sha256=(Get-FileHash -LiteralPath $warpAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
 sources=@($warpSources|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
 requests=@($warpPaths|ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
 output=$warpLines
}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $warpOutput 'world_travel_controller_probe_results.json') -Encoding UTF8
$warpLines
