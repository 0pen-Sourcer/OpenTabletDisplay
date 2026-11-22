param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$versionLine = Select-String -LiteralPath 'internal\bridge\constants.go' -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
if (-not $versionLine) { throw 'Could not determine FSDL runtime version.' }
$Version = $versionLine.Matches[0].Groups[1].Value
$Package = 'OpenTabletDisplay-' + $Version + '-windows-x64'
$DistRoot = Join-Path $Root 'dist'
$Out = Join-Path $DistRoot $Package
Remove-Item -LiteralPath $DistRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'payload\probes'), (Join-Path $Out 'installer') | Out-Null

Write-Host '[1/7] PowerShell parse'
$parseErrors = @()
Get-ChildItem -LiteralPath 'installer' -Filter '*.ps1' -File | ForEach-Object {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors)
    $parseErrors += $errors
}
if ($parseErrors.Count -gt 0) { $parseErrors | Format-List | Out-String | Write-Error; throw 'PowerShell parse failed.' }

Write-Host '[2/8] format + tests'
if (-not $SkipTests) {
$unformatted = @(gofmt -l cmd internal)
if ($unformatted.Count -gt 0) { throw ('gofmt required: ' + ($unformatted -join ', ')) }
go test ./...
if ($LASTEXITCODE -ne 0) { throw 'go test failed' }
go vet ./...
if ($LASTEXITCODE -ne 0) { throw 'go vet failed' }
go test -race ./...
if ($LASTEXITCODE -ne 0) { throw 'go test -race failed' }
go run ./cmd/fsdl --selftest
if ($LASTEXITCODE -ne 0) { throw 'selftest failed' }
} else {
    Write-Host 'Tests skipped in Windows packaging smoke; Ubuntu CI runs the full test suite.'
}

Write-Host '[3/9] Native Windows icon resource'
& (Join-Path $PSScriptRoot 'create-windows-icon.ps1')
if (-not (Test-Path 'cmd/desktop/rsrc_windows_amd64.syso')) { throw 'Branded Windows EXE icon resource missing' }

Write-Host '[4/9] Windows host binary'
$env:GOOS='windows'; $env:GOARCH='amd64'; $env:CGO_ENABLED='0'
# Keep the 3.x runtime binary name for installed-system compatibility.
go build -trimpath -ldflags '-s -w' -o (Join-Path $Out 'payload\FSDL.exe') ./cmd/fsdl
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }

Write-Host '[5/9] Desktop GUI launcher'
$env:GOOS='windows'; $env:GOARCH='amd64'; $env:CGO_ENABLED='0'
go build -trimpath -ldflags '-s -w -H=windowsgui' -o (Join-Path $Out 'OpenTabletDisplay.exe') ./cmd/desktop
if ($LASTEXITCODE -ne 0) { throw 'Desktop launcher build failed' }

Write-Host '[6/9] Android live-test probe'
$env:GOOS='android'; $env:GOARCH='arm64'; $env:CGO_ENABLED='0'
go build -trimpath -ldflags '-s -w' -o (Join-Path $Out 'payload\probes\fsdl-probe-android-arm64') ./cmd/fsdl-probe
if ($LASTEXITCODE -ne 0) { throw 'Android ARM64 probe build failed' }

Write-Host '[7/9] installer + docs'
Copy-Item installer\*.ps1 (Join-Path $Out 'installer')
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'assets') | Out-Null
Copy-Item assets\OpenTabletDisplay.ico,assets\OpenTabletDisplay.png (Join-Path $Out 'assets')
Copy-Item install.cmd,status.cmd,reset-device.cmd,uninstall.cmd $Out
Copy-Item README.md,LICENSE,THIRD_PARTY_NOTICES.md $Out
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'docs') | Out-Null
Copy-Item docs\DESKTOP_APP.md,docs\COMPATIBILITY.md,docs\MOONLIGHT.md,docs\TROUBLESHOOTING.md (Join-Path $Out 'docs')

Write-Host '[8/9] checksums'
$checksumPath = Join-Path $Out 'SHA256SUMS.txt'
$rows = Get-ChildItem -LiteralPath $Out -File -Recurse | Where-Object { $_.FullName -ne $checksumPath } | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($Out.Length + 1).Replace('\','/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $rel
}
$rows | Set-Content -LiteralPath $checksumPath -Encoding ASCII

Write-Host '[9/9] zip'
$zip = Join-Path $DistRoot ($Package + '.zip')
Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($zipHash + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Host ('SHA256 ' + $zipHash)
Write-Host ('Built: ' + $zip)
