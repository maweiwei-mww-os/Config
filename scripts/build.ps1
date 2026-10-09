param([string]$Unity = 'D:\unityeditor2\2022.3.60f1c1\Editor\Unity.exe', [switch]$Performance)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet run --project Tests/CoreTests.csproj -c Release -- --evidence
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if (-not (Test-Path -LiteralPath $Unity)) { throw 'Pass the installed Unity.exe path using -Unity.' }
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'Artifacts') | Out-Null
    $method = if ($Performance) { 'DiceTown.TownBuild.BuildPerformance' } else { 'DiceTown.TownBuild.Build' }
    $folder = if ($Performance) { 'Performance' } else { 'Windows' }
    $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', "`"$root`"", '-executeMethod', $method, '-logFile', "`"$root\Artifacts\unity-build.log`"")
    $p = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "Unity failed with code $($p.ExitCode). See Artifacts/unity-build.log." }
    Write-Host "Game built: $root\Builds\$folder\DiceTown.exe"
} finally { Pop-Location }
