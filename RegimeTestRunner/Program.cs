using DualEngineRegimeBot.Tests;
using DualEngineRegimeBot.Tests.Data;

// Parse command-line arguments
bool noPause = args.Contains("--no-pause");
bool autoRunBoth = args.Contains("--both");
bool autoRunRegime = args.Contains("--regime");
bool autoRunSms = args.Contains("--sms");

Console.WriteLine("════════════════════════════════════════════════════════════");
Console.WriteLine("  DualEngineRegimeBot - Test Runner");
Console.WriteLine("════════════════════════════════════════════════════════════");
Console.WriteLine();

// Determine which tests to run
string? choice;
if (autoRunBoth)
{
    choice = "3";
}
else if (autoRunRegime)
{
    choice = "1";
}
else if (autoRunSms)
{
    choice = "2";
}
else
{
    Console.WriteLine("Available tests:");
    Console.WriteLine("  1. Regime Stability Test (M15)");
    Console.WriteLine("  2. SMS Sanity Test (M1)");
    Console.WriteLine("  3. Both tests");
    Console.WriteLine();
    Console.WriteLine("Command-line options:");
    Console.WriteLine("  --regime     Run regime test only");
    Console.WriteLine("  --sms        Run SMS test only");
    Console.WriteLine("  --both       Run both tests");
    Console.WriteLine("  --no-pause   Skip pauses (for CI/automation)");
    Console.WriteLine();
    Console.Write("Select test to run (1-3, or Enter for both): ");
    choice = Console.ReadLine();
}

bool runRegime = choice == "1" || choice == "3" || string.IsNullOrWhiteSpace(choice);
bool runSms = choice == "2" || choice == "3" || string.IsNullOrWhiteSpace(choice);

try
{
    if (runRegime)
    {
        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════");
        Console.WriteLine("  Running Regime Stability Test");
        Console.WriteLine("════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var barLoader = new StubBarLoader();
        RegimeStabilityRunner.Run(barLoader, "XAUUSD", 200, 1.0);
    }

    if (runSms)
    {
        if (runRegime)
        {
            Console.WriteLine();
            Console.WriteLine();
            SafePause("Press any key to continue to SMS test...", noPause);
        }

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════");
        Console.WriteLine("  Running SMS Sanity Test");
        Console.WriteLine("════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var smsEngine = new StubSmsEngine();
        var barLoader = new StubBarLoader();
        SMSSanityRunner.Run(smsEngine, barLoader, "XAUUSD", 1000, 0.5);
    }

    Console.WriteLine();
    Console.WriteLine("════════════════════════════════════════════════════════════");
    Console.WriteLine("✓ All tests completed successfully!");
    Console.WriteLine("════════════════════════════════════════════════════════════");
    Console.WriteLine();
    
    if (!noPause)
    {
        SafePause("Press any key to exit...", noPause);
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("════════════════════════════════════════════════════════════");
    Console.WriteLine($"❌ ERROR: {ex.Message}");
    Console.WriteLine("════════════════════════════════════════════════════════════");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    Environment.Exit(1);
}

/// <summary>
/// Safe pause helper that handles non-interactive consoles gracefully.
/// </summary>
/// <param name="message">Message to display before pausing</param>
/// <param name="skipPause">If true, skip the pause entirely</param>
static void SafePause(string message = "Press any key to continue...", bool skipPause = false)
{
    if (skipPause) return;

    try
    {
        // Skip pause if input is redirected or environment is non-interactive
        if (Console.IsInputRedirected)
        {
            Console.WriteLine($"[Skipped pause: {message.Replace("Press any key to ", "")}]");
            return;
        }

        // Check for container/CI environment
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
            Environment.GetEnvironmentVariable("CI") == "true" ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Console.WriteLine($"[Skipped pause in CI: {message.Replace("Press any key to ", "")}]");
            return;
        }

        Console.WriteLine(message);
        
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Console.ReadKey(true);
            }
            catch
            {
                // ReadKey failed, try ReadLine as fallback
                Console.ReadLine();
            }
        }
        else
        {
            // On some non-Windows hosts, ReadKey throws; use ReadLine
            Console.ReadLine();
        }
    }
    catch
    {
        // No-op: ignore pause errors in CI/redirected terminals
        // This ensures the program continues even if pause fails
    }
}
