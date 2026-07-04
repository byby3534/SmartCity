$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot  = Split-Path -Parent $ScriptDir
$PluginDir = "$RepoRoot\Unity\ElninoEnergyRiskSimulation\Assets\Plugins\WebGL"
$Source    = "$ScriptDir\SeoulBuildingProcessor.cpp"
$Object    = "$PluginDir\SeoulBuildingProcessor.o"
$Archive   = "$PluginDir\SeoulBuildingProcessor.a"

$UnityVersion = if ($env:UNITY_VERSION) { $env:UNITY_VERSION } else { "6000.3.18f1" }
$EmsdkRoot = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\Emscripten"
$Emcc = "$EmsdkRoot\emscripten\emcc.bat"
$Emar = "$EmsdkRoot\emscripten\emar.bat"

$env:EM_CONFIG = "$EmsdkRoot\.emscripten"

if (-not (Test-Path $Emcc)) {
    Write-Error "Unity Emscripten을 찾을 수 없습니다: $Emcc`nUnity Hub에서 WebGL Build Support를 설치하거나 UNITY_VERSION 환경변수를 설정하세요."
    exit 1
}

New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null

# Unity Player Settings → WebGL → Publishing: Multithreading ON이면 1, OFF면 0
$UsePthreads = if ($env:USE_PTHREADS) { $env:USE_PTHREADS } else { "1" }

$EmccFlags = @("-std=c++17", "-O2", "-I", $ScriptDir)
if ($UsePthreads -eq "1") {
    $EmccFlags += @("-s", "USE_PTHREADS=1", "-s", "SHARED_MEMORY=1")
    Write-Host "Building with pthreads (Unity Multithreading ON)"
} else {
    Write-Host "Building without pthreads (Unity Multithreading OFF)"
}

Write-Host "Compiling..."
& $Emcc @EmccFlags -c $Source -o $Object
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Archiving..."
& $Emar rcs $Archive $Object
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done."
Write-Host "  $Object"
Write-Host "  $Archive"
