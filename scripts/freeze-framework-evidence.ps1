param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'Packages\com.game.json'
$version = (Get-Content -LiteralPath "$source\package.json" -Raw | ConvertFrom-Json).version
$installed = @(Get-ChildItem -LiteralPath "$root\Tests\UnityConsumer\Library\PackageCache" -Directory -Filter 'com.game.json@*' | Where-Object {
    (Get-Content -LiteralPath (Join-Path $_.FullName 'package.json') -Raw | ConvertFrom-Json).version -eq $version
})
if ($installed.Count -ne 1) { throw 'Expected exactly one installed release package.' }
$code = foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Extension -in @('.cs','.asmdef','.meta','.bytes','.json','.csv') }) {
    $relative = $file.FullName.Substring($source.Length + 1)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $installed[0].FullName $relative) -Algorithm SHA256).Hash) { throw "Installed source differs: $relative" }
    [pscustomobject]@{Path=$relative;SHA256=$hash}
}
$testLog = Get-Content -LiteralPath "$root\Artifacts\framework-tests-final.log" -Raw
if ($testLog -notmatch 'FRAMEWORK RESULT 30 passed / 0 failed' -or $testLog -notmatch 'RESULT 28 passed / 0 failed' -or $testLog -match '(?m)^FAIL ') { throw 'Framework/consumer tests are incomplete.' }
if ($testLog -notmatch 'SOURCE RESULT 18 passed / 0 failed' -or $testLog -notmatch 'NO-JSON CONSUMER PASSED' -or $testLog -notmatch 'CSV CLI PASSED') { throw 'Source isolation/CLI evidence is incomplete.' }
$probe = Get-Content -LiteralPath "$root\Artifacts\framework-IL2CPP-result.json" -Raw | ConvertFrom-Json
if (-not $probe.Passed -or -not $probe.SourceEquivalent -or $probe.Backend -ne 'IL2CPP' -or $probe.ReadAllocationEvents -ne 0 -or $probe.PositiveControlEvents -lt 1) { throw 'IL2CPP probe did not pass.' }
$destination = Join-Path $root "Tests\Evidence\$version"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$reports = @('framework-tests-final.log','framework-IL2CPP-result.json','benchmark-IL2CPP.json','benchmark-dotnet.json','framework-IL2CPP.log')
foreach ($report in $reports) { Copy-Item -LiteralPath "$root\Artifacts\$report" -Destination $destination -Force }
foreach ($name in @('benchmark-IL2CPP.json','benchmark-dotnet.json')) {
    $bench = Get-Content -LiteralPath "$destination\$name" -Raw | ConvertFrom-Json
    if ($bench.Results.Count -ne 3 -or -not $bench.TimingExcludesAllocationProbes -or $bench.EmptyControl -ne 0 -or $bench.PositiveControl -lt 1) { throw 'Benchmark calibration/method incomplete.' }
    foreach ($scale in $bench.Results) {
        if ($scale.Backends.Count -ne 4) { throw 'Missing benchmark backend.' }
        foreach ($backend in $scale.Backends) {
            if ($backend.Queries.Count -ne 5 -or ($backend.Queries.AllocatedBytes | Where-Object { $_ -ne 0 })) { throw 'Incomplete or allocating benchmark.' }
        }
    }
}
if ((Get-FileHash -LiteralPath "$root\Tests\BenchmarkProgram.cs").Hash -ne (Get-FileHash -LiteralPath "$root\Tests\UnityConsumer\Assets\BenchmarkProgram.cs").Hash) { throw 'Unity benchmark source differs.' }
$archive = "$root\Distributions\com.game.json-$version.tgz"
$manifest = [ordered]@{
    Version=$version;CapturedUtc=[DateTime]::UtcNow.ToString('o')
    PackageSHA256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    Note='Final archive updates documentation after execution; packaged code, assembly definitions, binary fixtures, source data and existing metadata match the independently installed package byte-for-byte.'
    UnityConsumer='Tests/UnityConsumer';Backend='Windows x64 IL2CPP Development High stripping'
    GameAssemblySHA256=(Get-FileHash -LiteralPath "$root\Tests\UnityConsumer\Builds\IL2CPP\GameAssembly.dll" -Algorithm SHA256).Hash
    BenchmarkSourceSHA256=(Get-FileHash -LiteralPath "$root\Tests\BenchmarkProgram.cs" -Algorithm SHA256).Hash
    FixtureSHA256=(Get-FileHash -LiteralPath "$root\Tests\Generated\FixtureConfig.g.cs" -Algorithm SHA256).Hash
    PackageCode=$code
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$destination\manifest.json" -Encoding utf8
Write-Output "Release evidence verified: $destination"
