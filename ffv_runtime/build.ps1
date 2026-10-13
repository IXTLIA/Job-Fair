param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$Compiler = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
)
$ErrorActionPreference = 'Stop'
$pbRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$pbGame = (Resolve-Path -LiteralPath $GameRoot).Path
$pbCompiler = (Resolve-Path -LiteralPath $Compiler).Path
$pbBuild = Join-Path $pbRoot 'build'
[void](New-Item -ItemType Directory -Path $pbBuild -Force)
$pbAssembly = Join-Path $pbBuild 'JobFair.Runtime.dll'
$pbNames = @(
 'dotnet\System.Private.CoreLib.dll','dotnet\mscorlib.dll','dotnet\System.Runtime.dll',
 'dotnet\System.Collections.dll','dotnet\System.Reflection.dll','dotnet\System.Runtime.Extensions.dll',
 'dotnet\netstandard.dll','dotnet\System.Security.Cryptography.Algorithms.dll',
 'dotnet\System.Security.Cryptography.Primitives.dll','dotnet\System.Text.Json.dll',
 'dotnet\System.Memory.dll','dotnet\System.Text.Encodings.Web.dll','dotnet\System.Linq.dll',
 'dotnet\System.Buffers.dll','dotnet\System.Collections.Concurrent.dll','dotnet\System.IO.dll','dotnet\System.Threading.dll',
 'BepInEx\core\BepInEx.Core.dll','BepInEx\core\BepInEx.Unity.IL2CPP.dll',
 'BepInEx\core\Il2CppInterop.Runtime.dll','BepInEx\core\0Harmony.dll',
 'BepInEx\interop\Il2Cppmscorlib.dll','BepInEx\interop\Il2CppSystem.Core.dll','BepInEx\interop\Assembly-CSharp.dll',
 'BepInEx\interop\UniTask.dll','BepInEx\interop\UnityEngine.CoreModule.dll',
 'BepInEx\interop\UnityEngine.IMGUIModule.dll','BepInEx\interop\UnityEngine.TextRenderingModule.dll',
 'BepInEx\interop\UnityEngine.InputLegacyModule.dll','BepInEx\interop\Unity.InputSystem.dll'
)
$pbRefs = @($pbNames | ForEach-Object { (Resolve-Path -LiteralPath (Join-Path $pbGame $_)).Path })
$pbSources = @('Plugin.cs','BossCatalog.cs','BossScripts.cs','BossRuntime.cs','BossMechanics.cs','BossRewards.cs','NativeBossData.cs','SetupView.cs','WorldTravelView.cs','WorldTravelRuntime.cs','WorldTravelTransaction.cs','SeedLogic.cs','RunMetadata.cs','WorldTravelState.cs','WorldTravelCatalogValidation.cs','SaveSeedCodec.cs','LoadBridge.cs','OpeningScript.cs','NextRunConfig.cs','TuleNpcDefinition.cs','RenameSession.cs','RenameInput.cs','EquipBellsData.cs','EquipBellsLearning.cs','EquipBellsEquipment.cs','ModeOpening.cs','RemixedOpening.cs','RemixedMenuRuntime.cs','OpeningSelection.cs','NativeProgram.cs','NativeRewardData.cs','NativeWorldRewardData.cs','NativeRemixedJobData.cs','RemixedJobCatalog.cs','RemixedJobRuntime.cs','NativeShopData.cs','ShopCatalog.cs','ShopRuntime.cs','CampaignCatalog.cs','CampaignRuntime.cs','WorldTravelScripts.cs','NativeWorldTravelData.cs' | ForEach-Object { Join-Path $pbRoot $_ })
$pbSources += @('FiestaRuntime.cs','EnemyLootCatalog.cs','EnemyLootRuntime.cs','EnemyLootExperience.cs','NativeEnemyLootData.cs','EventRewardCatalog.cs','EventRewardRuntime.cs','NativeEventRewardData.cs' | ForEach-Object { Join-Path $pbRoot $_ })
$pbSources += @('KickData.cs','KickLearning.cs' | ForEach-Object { Join-Path $pbRoot $_ })
$pbSources += @('WorldTravelControls.cs','WorldTravelController.cs' | ForEach-Object { Join-Path $pbRoot $_ })
$pbSources += Join-Path $pbRoot 'WorldMapOverlayRuntime.cs'
$pbArgs = @('-nologo','-noconfig','-nostdlib+','-target:library','-langversion:9','-optimize+','-deterministic+','-warnaserror+',('-out:' + $pbAssembly))
$pbArgs += @($pbRefs | ForEach-Object { '-reference:' + $_ })
$pbArgs += $pbSources
& $pbCompiler @pbArgs
if ($LASTEXITCODE -ne 0) { throw "Compiler exited $LASTEXITCODE" }
[ordered]@{
 status='compiled_not_installed'; assembly=$pbAssembly;
 assembly_sha256=(Get-FileHash -LiteralPath $pbAssembly -Algorithm SHA256).Hash.ToLowerInvariant();
 compiler=$pbCompiler; compiler_sha256=(Get-FileHash -LiteralPath $pbCompiler -Algorithm SHA256).Hash.ToLowerInvariant();
 sources=@($pbSources | ForEach-Object { [ordered]@{path=$_; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()} });
 references=@($pbRefs | ForEach-Object { [ordered]@{path=$_; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()} })
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $pbBuild 'build_evidence.json') -Encoding UTF8
Write-Output ('Compiled runtime without installation: ' + $pbAssembly)
