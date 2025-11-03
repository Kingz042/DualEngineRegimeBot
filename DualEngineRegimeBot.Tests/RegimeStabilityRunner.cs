using System;
using DualEngineRegimeBot.Core.Macro;
using DualEngineRegimeBot.Tests.Data;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// Standalone console runner for regime stability testing.
    /// Can be invoked from Main() or run as a standalone test harness.
    /// </summary>
    public class RegimeStabilityRunner
    {
        /// <summary>
        /// Runs the regime stability test with default or custom parameters.
        /// </summary>
        /// <param name="barLoader">Bar loader implementation (use StubBarLoader if no real data available)</param>
        /// <param name="symbol">Symbol to test (default: XAUUSD)</param>
        /// <param name="barCount">Number of M15 bars to load (default: 200)</param>
        /// <param name="atrFloor">Minimum ATR for normalization (default: 1.0)</param>
        public static void Run(
            IBarLoader barLoader = null,
            string symbol = "XAUUSD",
            int barCount = 200,
            double atrFloor = 1.0)
        {
            try
            {
                // Use stub loader if none provided
                barLoader ??= new StubBarLoader();

                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║    DualEngineRegimeBot - Regime Stability Test Harness      ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                Console.WriteLine();
                Console.WriteLine($"Symbol:         {symbol}");
                Console.WriteLine($"Timeframe:      M15");
                Console.WriteLine($"Bar count:      {barCount}");
                Console.WriteLine($"ATR floor:      {atrFloor:F2}");
                Console.WriteLine();
                Console.Write("Loading historical bars... ");

                // Load bars
                var bars = barLoader.Load(symbol, TimeFrame.Minute15, barCount);
                Console.WriteLine($"✓ Loaded {bars.Count} bars");

                if (bars.Count == 0)
                {
                    Console.WriteLine("❌ ERROR: No bars loaded. Check your data source.");
                    return;
                }

                Console.WriteLine($"Date range: {bars[0].Time:yyyy-MM-dd HH:mm} to {bars[bars.Count - 1].Time:yyyy-MM-dd HH:mm}");
                Console.WriteLine();

                // Create RegimeSupervisor instance
                var supervisor = new RegimeSupervisor();

                // Run the stability test
                RegimeStabilityTest.Run(supervisor, bars, atrFloor);

                Console.WriteLine();
                Console.WriteLine("✓ Test completed successfully!");
                Console.WriteLine();
                Console.WriteLine("NEXT STEPS:");
                Console.WriteLine("  1. Review regime_test.csv for regime classification over time");
                Console.WriteLine("  2. If using synthetic data, replace StubBarLoader with your real data source");
                Console.WriteLine("  3. If regime is too noisy/sticky, tune your RegimeModule parameters");
                Console.WriteLine("  4. Check that confidence levels align with market conditions");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"❌ ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Example Main() method to run as a standalone console app.
        /// Uncomment this if you want to run the test independently.
        /// </summary>
        /*
        public static void Main(string[] args)
        {
            // Parse command-line arguments if needed
            string symbol = args.Length > 0 ? args[0] : "XAUUSD";
            int barCount = args.Length > 1 && int.TryParse(args[1], out int bc) ? bc : 200;
            double atrFloor = args.Length > 2 && double.TryParse(args[2], out double af) ? af : 1.0;

            // Use stub loader (replace with real implementation later)
            var barLoader = new StubBarLoader();

            // Run the test
            Run(barLoader, symbol, barCount, atrFloor);

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        */
    }
}

