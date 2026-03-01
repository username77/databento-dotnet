# Build script for Databento.Native (Windows)
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$nativeDir = Join-Path $rootDir "src\Databento.Native"
$buildDir = Join-Path $rootDir "build\native"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Building Databento.Native" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Clean if requested
if ($Clean -and (Test-Path $buildDir)) {
    Write-Host "Cleaning build directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $buildDir
}

# Create build directory
if (!(Test-Path $buildDir)) {
    New-Item -ItemType Directory -Path $buildDir | Out-Null
}

# Check for CMake
$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if (!$cmake) {
    Write-Error "CMake not found. Please install CMake and add it to PATH."
    exit 1
}

# Find vcpkg
$vcpkgRoot = $null
if ($env:VCPKG_ROOT -and (Test-Path $env:VCPKG_ROOT)) {
    $vcpkgRoot = $env:VCPKG_ROOT
} elseif (Test-Path "C:\vcpkg") {
    $vcpkgRoot = "C:\vcpkg"
}

if (!$vcpkgRoot) {
    Write-Error @"
vcpkg not found. databento-cpp requires vcpkg to install its dependencies (OpenSSL, zstd).
Please install vcpkg and either:
  - Set the VCPKG_ROOT environment variable, or
  - Install vcpkg to C:\vcpkg
See: https://github.com/microsoft/vcpkg#getting-started
"@
    exit 1
}

$vcpkgToolchain = Join-Path $vcpkgRoot "scripts\buildsystems\vcpkg.cmake"
if (!(Test-Path $vcpkgToolchain)) {
    Write-Error "vcpkg toolchain file not found at: $vcpkgToolchain"
    exit 1
}

Write-Host "Using vcpkg root: $vcpkgRoot" -ForegroundColor Yellow
Write-Host "Using vcpkg toolchain: $vcpkgToolchain" -ForegroundColor Yellow

# Build CMake arguments with vcpkg integration
# VCPKG_MANIFEST_DIR must point to our project directory containing vcpkg.json
# so that vcpkg installs OpenSSL and zstd before databento-cpp's find_package() calls.
# Force x64-windows triplet (release) to avoid shipping debug DLLs that depend on
# VCRUNTIME140D.dll and ucrtbased.dll which are not available on end-user machines.
$cmakeArgs = @(
    "-S", $nativeDir,
    "-B", ".",
    "-DCMAKE_BUILD_TYPE=$Configuration",
    "-DCMAKE_TOOLCHAIN_FILE=$vcpkgToolchain",
    "-DVCPKG_MANIFEST_DIR=$nativeDir",
    "-DVCPKG_TARGET_TRIPLET=x64-windows"
)

# Configure
Write-Host "`nConfiguring CMake..." -ForegroundColor Green
Push-Location $buildDir
try {
    & cmake $cmakeArgs

    if ($LASTEXITCODE -ne 0) {
        throw "CMake configuration failed"
    }

    # Build
    Write-Host "`nBuilding..." -ForegroundColor Green
    cmake --build . --config $Configuration

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }

    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green

    # Validate that shipped DLLs are Release builds (no debug runtime dependencies)
    $runtimeDir = Join-Path $rootDir "src\Databento.Interop\runtimes\win-x64\native"
    if (Test-Path $runtimeDir) {
        Write-Host "`nValidating shipped DLLs..." -ForegroundColor Yellow
        $hasDebugDeps = $false
        $dumpbin = Get-Command dumpbin -ErrorAction SilentlyContinue
        if ($dumpbin) {
            Get-ChildItem $runtimeDir -Filter "*.dll" | ForEach-Object {
                $deps = & dumpbin /dependents $_.FullName 2>&1
                $debugDeps = $deps | Select-String -Pattern "VCRUNTIME140D\.dll|ucrtbased\.dll|MSVCP140D\.dll"
                if ($debugDeps) {
                    Write-Host "  WARNING: $($_.Name) depends on debug runtime DLLs!" -ForegroundColor Red
                    $debugDeps | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
                    $hasDebugDeps = $true
                } else {
                    Write-Host "  OK: $($_.Name)" -ForegroundColor Green
                }
            }
            if ($hasDebugDeps) {
                Write-Warning "Some DLLs have debug runtime dependencies. These will not work on machines without Visual Studio."
                Write-Warning "Ensure you build with Release configuration and use the 'x64-windows' vcpkg triplet (not 'x64-windows-dbg')."
            }
        } else {
            Write-Host "  Skipping validation (dumpbin not found)" -ForegroundColor Yellow
        }
    }
}
finally {
    Pop-Location
}
