param(
    [string]$Unity = 'D:\unityeditor2\2022.3.60f1c1\Editor\Unity.exe',
    [ValidateSet('Editor', 'Mono', 'IL2CPP')][string]$Target = 'Editor',
    [switch]$Benchmark
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Tests\UnityConsumer'
Push-Location $root
try {
    if (-not (Test-Path -LiteralPath $Unity)) { throw 'Pass an installed Unity.exe path using -Unity.' }
    dotnet run --project Tools/GameJson.Tool/GameJson.Tool.csproj -c Release -- Tests/Fixtures/framework.json Tests/UnityConsumer/Assets/Resources FrameworkFixture FixtureConfig
    if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' }
    Copy-Item -LiteralPath "$root\Tests\BenchmarkProgram.cs" -Destination "$project\Assets\BenchmarkProgram.cs" -Force
    $sample = "$project\Assets\SourceIndependent"
    New-Item -ItemType Directory -Path $sample -Force | Out-Null
    Get-ChildItem -LiteralPath "$root\Packages\com.game.json\Samples~\SourceIndependent" -Force | Copy-Item -Destination $sample -Recurse -Force
    New-Item -ItemType Directory -Force -Path "$root\Artifacts" | Out-Null
    $method = if ($Target -eq 'Editor') { 'PackageSmoke.VerifyFramework' } else { "PackageSmoke.Build$Target" }
    $log = "$root\Artifacts\framework-$Target.log"
    $started = [DateTime]::UtcNow
    $p = Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',"`"$project`"",'-executeMethod',$method,'-logFile',"`"$log`"") -WindowStyle Hidden -PassThru
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "Unity failed with code $($p.ExitCode). See $log" }
    if ($Target -ne 'Editor') {
        $result = "$root\Artifacts\framework-$Target-result.json"
        $playerArgs = @('-batchmode','-nographics','--result',"`"$result`"",'-logFile',"`"$root\Artifacts\framework-$Target-player.log`"")
        if ($Benchmark) { $playerArgs += @('--benchmark',"`"$root\Artifacts\benchmark-$Target.json`"") }
        $p = Start-Process -FilePath "$project\Builds\$Target\FrameworkProbe.exe" -ArgumentList $playerArgs -WindowStyle Hidden -PassThru
        if (-not $p.WaitForExit(300000)) { $p.Kill(); throw 'Framework player timed out.' }
        if ($p.ExitCode -ne 0) { throw "Framework player failed with code $($p.ExitCode)." }
    } else { $result = "$project\framework-editor-result.json" }
    if ((Get-Item -LiteralPath $result).LastWriteTimeUtc -lt $started) { throw 'Result is stale.' }
    $report = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if (-not $report.Passed -or -not $report.SourceEquivalent -or $report.ReadAllocationEvents -ne 0 -or $report.PositiveControlEvents -lt 1) { throw 'Framework verification failed.' }
    if ($Target -eq 'IL2CPP' -and $report.Backend -ne 'IL2CPP') { throw 'Wrong scripting backend.' }
    if ($Benchmark -and $Target -ne 'Editor') {
        $benchPath = "$root\Artifacts\benchmark-$Target.json"
        if ((Get-Item -LiteralPath $benchPath).LastWriteTimeUtc -lt $started) { throw 'Benchmark is stale.' }
        $bench = Get-Content -LiteralPath $benchPath -Raw | ConvertFrom-Json
        if ($bench.Results.Count -ne 3 -or $bench.PositiveControl -lt 1 -or -not $bench.TimingExcludesAllocationProbes) { throw 'Incomplete benchmark.' }
    }
    Write-Output $report
} finally { Pop-Location }
