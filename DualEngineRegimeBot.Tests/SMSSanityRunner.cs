using System;
using DualEngineRegimeBot.Tests.Data;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// Standalone console runner for SMS sanity testing.
    /// Can be invoked from Main() or run as a standalone test harness.
    /// </summary>
    public class SMSSanityRunner
    {
        /// <summary>
        /// Runs the SMS sanity test with default or custom parameters.
        /// </summary>
        /// <param name="smsEngine">SMS engine implementation (use StubSmsEngine if no real implementation available)</param>
        /// <param name="barLoader">Bar loader implementation (use StubBarLoader if no real data available)</param>
        /// <param name="symbol">Symbol to test (default: XAUUSD)</param>
        /// <param name="barCount">Number of M1 bars to load (default: 1000)</param>
        /// <param name="atrFloor">Minimum ATR for normalization (default: 0.5)</param>
        public static void Run(
            ISmsEngine? smsEngine = null,
            IBarLoader? barLoader = null,
            string symbol = "XAUUSD",
            int barCount = 1000,
            double atrFloor = 0.5)
        {
            try
            {
                // Use stub implementations if none provided
                smsEngine ??= new StubSmsEngine();
                barLoader ??= new StubBarLoader();

                Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║       DualEngineRegimeBot - SMS Sanity Test Harness         ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                Console.WriteLine();
                Console.WriteLine($"Symbol:         {symbol}");
                Console.WriteLine($"Timeframe:      M1");
                Console.WriteLine($"Bar count:      {barCount}");
                Console.WriteLine($"ATR floor:      {atrFloor:F2}");
                Console.WriteLine();
                Console.Write("Loading historical bars... ");

                // Load bars
                var bars = barLoader.Load(symbol, TimeFrame.Minute1, barCount);
                Console.WriteLine($"✓ Loaded {bars.Count} bars");

                if (bars.Count == 0)
                {
                    Console.WriteLine("❌ ERROR: No bars loaded. Check your data source.");
                    return;
                }

                Console.WriteLine($"Date range: {bars[0].Time:yyyy-MM-dd HH:mm} to {bars[bars.Count - 1].Time:yyyy-MM-dd HH:mm}");
                Console.WriteLine();

                // Run the sanity test
                SMSSanityTest.Run(smsEngine, bars, atrFloor);

                Console.WriteLine();
                Console.WriteLine("✓ Test completed successfully!");
                Console.WriteLine();
                Console.WriteLine("NEXT STEPS:");
                Console.WriteLine("  1. If using synthetic data, replace StubSmsEngine with your real SMS engine");
                Console.WriteLine("  2. If using synthetic data, replace StubBarLoader with your real data source");
                Console.WriteLine("  3. If SMS is not responsive (Vol/Quiet <1.3), tune EMA periods or ATR normalization");
                Console.WriteLine("  4. If ExecMult clamp is out of range, check the mapping function");
                Console.WriteLine("  5. Run with different market conditions to validate responsiveness");
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
            int barCount = args.Length > 1 && int.TryParse(args[1], out int bc) ? bc : 1000;
            double atrFloor = args.Length > 2 && double.TryParse(args[2], out double af) ? af : 0.5;

            // Use stub implementations (replace with real implementations later)
            var smsEngine = new StubSmsEngine();
            var barLoader = new StubBarLoader();

            // Run the test
            Run(smsEngine, barLoader, symbol, barCount, atrFloor);

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        */
    }
}

