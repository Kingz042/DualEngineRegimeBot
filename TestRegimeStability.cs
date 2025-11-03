using System;
using DualEngineRegimeBot.Tests;
using DualEngineRegimeBot.Tests.Data;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Starting Regime Stability Test...");
        Console.WriteLine();
        
        try
        {
            // Parse command-line arguments
            string symbol = args.Length > 0 ? args[0] : "XAUUSD";
            int barCount = args.Length > 1 && int.TryParse(args[1], out int bc) ? bc : 200;
            double atrFloor = args.Length > 2 && double.TryParse(args[2], out double af) ? af : 1.0;

            // Use stub loader (generates synthetic data for testing)
            var barLoader = new StubBarLoader();

            // Run the test
            RegimeStabilityRunner.Run(barLoader, symbol, barCount, atrFloor);
            
            Console.WriteLine();
            Console.WriteLine("✓ Test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Environment.Exit(1);
        }
    }
}

