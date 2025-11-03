# Quick PowerShell script to run the Regime Stability Test
# Usage: .\RunRegimeTest.ps1

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  DualEngineRegimeBot - Regime Stability Test Launcher" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path "DualEngineRegimeBot.sln")) {
    Write-Host "Error: DualEngineRegimeBot.sln not found." -ForegroundColor Red
    Write-Host "Please run this script from the project root directory." -ForegroundColor Red
    exit 1
}

Write-Host "Building test project..." -ForegroundColor Yellow
dotnet build DualEngineRegimeBot.Tests/DualEngineRegimeBot.Tests.csproj --configuration Release --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green
Write-Host ""

# Create a temporary test runner
$testCode = @'
using System;
using DualEngineRegimeBot.Tests;
using DualEngineRegimeBot.Tests.Data;

class Program
{
    static void Main(string[] args)
    {
        string symbol = args.Length > 0 ? args[0] : "XAUUSD";
        int barCount = args.Length > 1 && int.TryParse(args[1], out int bc) ? bc : 200;
        double atrFloor = args.Length > 2 && double.TryParse(args[2], out double af) ? af : 1.0;

        var barLoader = new StubBarLoader();
        RegimeStabilityRunner.Run(barLoader, symbol, barCount, atrFloor);
    }
}
'@

# Save temporary program
$tempDir = "DualEngineRegimeBot.Tests\bin\Release\net6.0"
$tempFile = "$tempDir\TestRunner_Temp.cs"
Set-Content -Path $tempFile -Value $testCode

Write-Host "Compiling test runner..." -ForegroundColor Yellow
csc /target:exe /out:"$tempDir\RegimeTest.exe" /reference:"$tempDir\DualEngineRegimeBot.Core.dll" /reference:"$tempDir\DualEngineRegimeBot.Tests.dll" /reference:"$tempDir\xunit.abstractions.dll" $tempFile 2>$null

if ($LASTEXITCODE -eq 0) {
    Write-Host "Running regime stability test..." -ForegroundColor Yellow
    Write-Host ""
    & "$tempDir\RegimeTest.exe" "XAUUSD" "200" "1.0"
    
    # Clean up
    Remove-Item $tempFile -ErrorAction SilentlyContinue
    Remove-Item "$tempDir\RegimeTest.exe" -ErrorAction SilentlyContinue
} else {
    Write-Host ""
    Write-Host "Could not compile standalone runner. Running via dotnet test instead..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To run manually, add this to your Program.cs:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "using DualEngineRegimeBot.Tests;" -ForegroundColor Gray
    Write-Host "using DualEngineRegimeBot.Tests.Data;" -ForegroundColor Gray
    Write-Host ""
    Write-Host "var barLoader = new StubBarLoader();" -ForegroundColor Gray
    Write-Host "RegimeStabilityRunner.Run(barLoader, `"XAUUSD`", 200, 1.0);" -ForegroundColor Gray
    Write-Host ""
    
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "For more information, see:" -ForegroundColor Cyan
Write-Host "  DualEngineRegimeBot.Tests\README_REGIME_STABILITY.md" -ForegroundColor White
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan

