# Quick PowerShell script to run the SMS Sanity Test
# Usage: .\RunSMSTest.ps1

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  DualEngineRegimeBot - SMS Sanity Test Launcher" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path "DualEngineRegimeBot.sln")) {
    Write-Host "Error: DualEngineRegimeBot.sln not found." -ForegroundColor Red
    Write-Host "Please run this script from the project root directory." -ForegroundColor Red
    exit 1
}

Write-Host "Building RegimeTestRunner..." -ForegroundColor Yellow
dotnet build RegimeTestRunner/RegimeTestRunner.csproj --configuration Release --verbosity quiet 2>&1 | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green
Write-Host ""
Write-Host "Running SMS Sanity Test..." -ForegroundColor Yellow
Write-Host ""

# Create a simple test program that just runs SMS test
$testCode = @'
using System;
using DualEngineRegimeBot.Tests;
using DualEngineRegimeBot.Tests.Data;

class SMSTestProgram
{
    static void Main()
    {
        var smsEngine = new StubSmsEngine();
        var barLoader = new StubBarLoader();
        SMSSanityRunner.Run(smsEngine, barLoader, "XAUUSD", 1000, 0.5);
    }
}
'@

$tempDir = "RegimeTestRunner\bin\Release\net6.0"
$tempFile = "$tempDir\SMSTest_Temp.cs"
Set-Content -Path $tempFile -Value $testCode

# Compile
$refs = @(
    "$tempDir\DualEngineRegimeBot.Core.dll",
    "$tempDir\DualEngineRegimeBot.Tests.dll"
)

$cscPath = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path $cscPath)) {
    $cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

if (Test-Path $cscPath) {
    & $cscPath /target:exe /out:"$tempDir\SMSTest.exe" /reference:$($refs -join ";") $tempFile 2>&1 | Out-Null
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path "$tempDir\SMSTest.exe")) {
        & "$tempDir\SMSTest.exe"
        Remove-Item $tempFile -ErrorAction SilentlyContinue
        Remove-Item "$tempDir\SMSTest.exe" -ErrorAction SilentlyContinue
    } else {
        Remove-Item $tempFile -ErrorAction SilentlyContinue
        Write-Host "Could not compile standalone test. Using dotnet to run..." -ForegroundColor Yellow
        Write-Host ""
        
        # Fallback: modify the RegimeTestRunner to auto-run SMS test
        cd RegimeTestRunner
        dotnet run --no-build --configuration Release
    }
} else {
    Write-Host "C# compiler not found. Running via project..." -ForegroundColor Yellow
    cd RegimeTestRunner
    
    # Create a flag file to signal we want SMS test only
    "2" | Out-File -FilePath "test_choice.txt" -Encoding ASCII
    
    Get-Content "test_choice.txt" | dotnet run --no-build --configuration Release
    
    Remove-Item "test_choice.txt" -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan

