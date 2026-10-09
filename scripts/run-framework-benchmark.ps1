param([string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'Tests\UnityConsumer\Builds\IL2CPP\FrameworkProbe.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build first with scripts/verify-framework-unity.ps1 -Target IL2CPP -Benchmark.' }
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $root 'Artifacts' }
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$probePath = Join-Path $ReportDirectory 'framework-IL2CPP-result.json'
$benchPath = Join-Path $ReportDirectory 'benchmark-IL2CPP.json'
$logPath = Join-Path $ReportDirectory 'framework-IL2CPP-player.log'
$started = [DateTime]::UtcNow
$p = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-nographics',
    '--result',"`"$probePath`"",'--benchmark',"`"$benchPath`"",'-logFile',"`"$logPath`"")
if (-not $p.WaitForExit(300000)) { $p.Kill(); throw 'Benchmark timed out.' }
if ($p.ExitCode -ne 0) { throw "Benchmark failed. See $logPath" }
foreach ($path in @($probePath,$benchPath)) { if ((Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $started) { throw 'Stale report.' } }
$probe = Get-Content -LiteralPath $probePath -Raw | ConvertFrom-Json
$bench = Get-Content -LiteralPath $benchPath -Raw | ConvertFrom-Json
if (-not $probe.Passed -or -not $probe.SourceEquivalent -or $probe.Backend -ne 'IL2CPP' -or $probe.ReadAllocationEvents -ne 0 -or $bench.PositiveControl -lt 1 -or $bench.EmptyControl -ne 0) { throw 'Probe failed.' }
$rows = foreach ($scale in $bench.Results) {
    foreach ($backend in $scale.Backends) {
        if ($backend.Queries.Count -ne 5 -or ($backend.Queries.AllocatedBytes | Where-Object { $_ -ne 0 })) { throw 'Incomplete/allocating workload.' }
        [pscustomobject]@{
            Rows = $scale.TotalRows; Backend = $backend.Backend
            ManagedMiB = [math]::Round($backend.RetainedManagedBytes / 1MB, 2)
            LoadMs = [math]::Round($backend.LoadMedianMs, 2)
            MixedNs = [math]::Round($backend.Queries[0].MedianNsPerOperation, 2)
            SparseNs = [math]::Round($backend.Queries[1].MedianNsPerOperation, 2)
            RowScanNs = [math]::Round($backend.Queries[3].MedianNsPerOperation, 2)
            ColumnScanNs = [math]::Round($backend.Queries[4].MedianNsPerOperation, 2)
            ReadAllocationBytes = 0
        }
    }
}
Write-Output "Unity $($probe.Unity), $($probe.Backend); allocation controls: empty=$($bench.EmptyControl), positive=$($bench.PositiveControl) events."
$rows | Format-Table -AutoSize | Out-String -Width 220 | Write-Output
Write-Output "Managed memory excludes input buffers; timings exclude allocation instrumentation. Full report: $benchPath"
