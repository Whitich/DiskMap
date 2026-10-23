param(
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

try {
    Write-Host "Building DiskMap ($Configuration)..." -ForegroundColor Cyan
    dotnet build DiskMap.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    $publishDir = Join-Path $root "publish"
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    Write-Host "Publishing self-contained single-file executable..." -ForegroundColor Cyan
    dotnet publish DiskMap.csproj -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $exe = Join-Path $publishDir "DiskMap.exe"
    if (-not (Test-Path -LiteralPath $exe)) { throw "Published executable not found: $exe" }
    $sizeMb = [Math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
    Write-Host "Portable executable: $exe ($sizeMb MB)" -ForegroundColor Green

    if ($SkipInstaller) {
        Write-Host "Installer step skipped." -ForegroundColor Yellow
        return
    }

    $iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path -LiteralPath $iscc)) {
        $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    }
    if (-not (Test-Path -LiteralPath $iscc)) {
        Write-Warning "Inno Setup 6 not found. Installer was not built. Portable exe is ready."
        return
    }

    Write-Host "Compiling installer..." -ForegroundColor Cyan
    & $iscc (Join-Path $root "installer\DiskMap.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

    $setup = Get-ChildItem -LiteralPath (Join-Path $root "dist") -Filter "DiskMap-Setup-*.exe" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -ne $setup) {
        $setupMb = [Math]::Round($setup.Length / 1MB, 1)
        Write-Host "Installer: $($setup.FullName) ($setupMb MB)" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
