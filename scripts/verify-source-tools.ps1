$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $sample = 'Packages/com.game.json/Samples~/SourceIndependent'
    $output = 'Artifacts/CsvCliSample'
    dotnet run --project Tools/GameJson.Tool/GameJson.Tool.csproj -c Release -- --csv "$sample/source.json" $sample 1 $output GameConfigSamples SourceConfig
    if ($LASTEXITCODE -ne 0) { throw 'CSV CLI failed.' }
    foreach ($name in @('SourceConfig.bytes','SourceConfig.g.cs')) {
        if ((Get-FileHash -LiteralPath "$sample/$name").Hash -ne (Get-FileHash -LiteralPath "$output/$name").Hash) { throw "CLI output differs: $name" }
    }
    Write-Output 'CSV CLI PASSED: binary and generated API match the shipped sample.'
} finally { Pop-Location }
