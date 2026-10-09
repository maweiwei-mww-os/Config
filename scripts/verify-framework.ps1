param([switch]$IncludeGameRegression)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet run --project Tools/GameJson.Tool/GameJson.Tool.csproj -c Release -- Tests/Fixtures/framework.json Tests/Generated FrameworkFixture FixtureConfig
    if ($LASTEXITCODE -ne 0) { throw 'Configuration generation failed.' }
    dotnet run --project Tests/FrameworkTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Independent framework tests failed.' }
    dotnet run --project Tests/SourceTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Source adapter tests failed.' }
    dotnet run --project Tests/RuntimeOnlyTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'No-JSON consumer failed.' }
    & "$PSScriptRoot/verify-source-tools.ps1"
    if ($IncludeGameRegression) {
        dotnet run --project Tests/CoreTests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Game compatibility regression failed.' }
    }
} finally { Pop-Location }
