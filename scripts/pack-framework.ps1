param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'Packages\com.game.json'
$version = (Get-Content -LiteralPath "$source\package.json" -Raw | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$') { throw 'Invalid package version.' }
$stage = Join-Path $root ('Artifacts\PackageStage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path "$stage\package", "$root\Distributions" | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination "$stage\package" -Recurse -Force
$archive = "$root\Distributions\com.game.json-$version.tgz"
tar -czf $archive -C $stage package
if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }
Get-FileHash -Algorithm SHA256 -LiteralPath $archive
