param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$versionLine = Select-String -LiteralPath 'internal\bridge\constants.go' -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
if (-not $versionLine) { throw 'Could not determine release version.' }
$Version = $versionLine.Matches[0].Groups[1].Value
$Package = 'OpenTabletDisplay-' + $Version + '-windows-x64'
$DistRoot = Join-Path $Root 'dist'
$Out = Join-Path $DistRoot $Package
Remove-Item -LiteralPath $DistRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'payload\probes'), (Join-Path $Out 'installer') | Out-Null

Write-Host '[1/8] Validate PowerShell installer scripts'
$parseErrors = @()
Get-ChildItem -LiteralPath 'installer' -Filter '*.ps1' -File | Where-Object { $_.Name -ne 'desktop.ps1' } | ForEach-Object {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors)
    $parseErrors += $errors
}
if ($parseErrors.Count) { $parseErrors | Format-List | Out-String | Write-Error; throw 'PowerShell install script parse failed.' }

Write-Host '[2/8] Transport validation'
if (-not $SkipTests) {
    $unformatted = @(gofmt -l cmd internal)
    if ($unformatted.Count) { throw ('gofmt required: ' + ($unformatted -join ', ')) }
    go test ./...
    if ($LASTEXITCODE -ne 0) { throw 'Go tests failed' }
    go vet ./...
    if ($LASTEXITCODE -ne 0) { throw 'Go vet failed' }
    go test -race ./...
    if ($LASTEXITCODE -ne 0) { throw 'Race tests failed' }
    go run ./cmd/fsdl --selftest
    if ($LASTEXITCODE -ne 0) { throw 'Transport self-test failed' }
}

Write-Host '[3/8] Generate brand assets'
& (Join-Path $PSScriptRoot 'create-windows-icon.ps1')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path 'assets/OpenTabletDisplay.ico')) { throw 'Icon generation failed' }

Write-Host '[4/8] Compile USB transport and Android probes'
$env:GOOS='windows'; $env:GOARCH='amd64'; $env:CGO_ENABLED='0'
go build -trimpath -o (Join-Path $Out 'payload\FSDL.exe') ./cmd/fsdl
if ($LASTEXITCODE -ne 0) { throw 'USB transport compilation failed' }
$env:GOOS='android'; $env:GOARCH='arm64'; $env:CGO_ENABLED='0'
go build -trimpath -o (Join-Path $Out 'payload\probes\fsdl-probe-android-arm64') ./cmd/fsdl-probe
if ($LASTEXITCODE -ne 0) { throw 'Android probe compilation failed' }

Write-Host '[5/8] Publish native WPF app (.NET 10 self-contained)'
$publish = Join-Path $env:TEMP ('otd-wpf-' + [guid]::NewGuid().ToString('N'))
try {
    dotnet publish desktop/OpenTabletDisplay/OpenTabletDisplay.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output $publish
    if ($LASTEXITCODE -ne 0) { throw 'Native WPF desktop compilation failed' }
    $native = Join-Path $publish 'OpenTabletDisplay.exe'
    if (-not (Test-Path -LiteralPath $native)) { throw 'Native desktop EXE missing from publish output.' }
    Copy-Item -LiteralPath $native -Destination (Join-Path $Out 'OpenTabletDisplay.exe') -Force
} finally { Remove-Item -LiteralPath $publish -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host '[6/8] Assemble Windows package'
Get-ChildItem installer -Filter '*.ps1' -File | Where-Object { $_.Name -ne 'desktop.ps1' } | Copy-Item -Destination (Join-Path $Out 'installer') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'assets') | Out-Null
Copy-Item assets/OpenTabletDisplay.ico,assets/OpenTabletDisplay.png (Join-Path $Out 'assets')
Copy-Item install.cmd,status.cmd,reset-device.cmd,uninstall.cmd $Out
Copy-Item README.md,LICENSE,THIRD_PARTY_NOTICES.md $Out
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'docs') | Out-Null
Copy-Item docs/DESKTOP_APP.md,docs/COMPATIBILITY.md,docs/MOONLIGHT.md,docs/TROUBLESHOOTING.md (Join-Path $Out 'docs')

Write-Host '[7/8] Verify package file hashes'
$checksumPath = Join-Path $Out 'SHA256SUMS.txt'
$rows = Get-ChildItem -LiteralPath $Out -File -Recurse | Where-Object { $_.FullName -ne $checksumPath } | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($Out.Length + 1).Replace('\','/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $rel
}
$rows | Set-Content -LiteralPath $checksumPath -Encoding ASCII

Write-Host '[8/8] Write release archive'
$zip = Join-Path $DistRoot ($Package + '.zip')
Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($zipHash + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Host ('SHA256 ' + $zipHash)
Write-Host ('Built ' + $zip)
