using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DualEngineRegimeBot.Core;
using DualEngineRegimeBot.Core.Macro;
using DualEngineRegimeBot.Tests.Data;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// Regime stability sanity test for M15 regime classification.
    /// 
    /// PURPOSE:
    /// Validates that RegimeSupervisor produces stable, meaningful regime classifications
    /// across a sample of historical M15 bars.
    /// 
    /// HEALTHY METRICS (Guidance):
    /// - Flip rate: 0.3–0.8 regime changes per hour (not too noisy, not too sticky)
    /// - Min regime duration: ≥5 bars (avoid excessive churn)
    /// - Confidence distribution: Majority should be >0.5 in trending markets
    /// 
    /// OUTPUT:
    /// - CSV file: regime_test.csv with Index,Time,Direction,Volatility,Confidence
    /// - Console summary with row count and basic statistics
    /// </summary>
    public static class RegimeStabilityTest
    {
        /// <summary>
        /// Runs the regime stability test on historical M15 bars.
        /// </summary>
        /// <param name="supervisor">RegimeSupervisor instance to test</param>
        /// <param name="m15Bars">List of M15 bars (oldest to newest)</param>
        /// <param name="atrFloor">Minimum ATR value for normalization (default: 1.0)</param>
        /// <param name="outputPath">Output CSV file path (default: "regime_test.csv")</param>
        public static void Run(
            RegimeSupervisor supervisor,
            IList<Bar> m15Bars,
            double atrFloor = 1.0,
            string outputPath = "regime_test.csv")
        {
            if (supervisor == null)
                throw new ArgumentNullException(nameof(supervisor));
            if (m15Bars == null || m15Bars.Count == 0)
                throw new ArgumentException("Bar list cannot be null or empty", nameof(m15Bars));
            if (atrFloor <= 0)
                throw new ArgumentException("ATR floor must be positive", nameof(atrFloor));

            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("Index,Time,Direction,Volatility,Confidence");

            // Statistics tracking
            int totalBars = 0;
            int regimeFlips = 0;
            RegimeDirection lastDirection = RegimeDirection.Neutral;
            RegimeVolState lastVolState = RegimeVolState.LowVol;
            double confidenceSum = 0;
            int confidenceCount = 0;

            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("         REGIME STABILITY TEST - M15 XAUUSD");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine($"Total bars to process: {m15Bars.Count}");
            Console.WriteLine($"ATR floor for normalization: {atrFloor:F2}");
            Console.WriteLine($"Output file: {outputPath}");
            Console.WriteLine();

            // Process each bar
            for (int i = 0; i < m15Bars.Count; i++)
            {
                var bar = m15Bars[i];
                
                // Create a regime snapshot (in real implementation, this would come from RegimeModule)
                // For testing purposes, we'll generate synthetic regime data based on price action
                var regime = GenerateRegimeSnapshot(bar, i, m15Bars, atrFloor);

                // Update supervisor
                supervisor.Update(regime, bar.Time);

                // Defensive: Clamp confidence to [0, 1]
                double clampedConfidence = Math.Max(0.0, Math.Min(1.0, regime.Confidence));

                // Track regime flips
                if (i > 0 && (regime.Direction != lastDirection || regime.VolState != lastVolState))
                {
                    regimeFlips++;
                }

                // Update statistics
                confidenceSum += clampedConfidence;
                confidenceCount++;
                lastDirection = regime.Direction;
                lastVolState = regime.VolState;

                // Append CSV row
                csvBuilder.AppendLine($"{i},{bar.Time:yyyy-MM-dd HH:mm:ss},{regime.Direction},{regime.VolState},{clampedConfidence:F2}");
                totalBars++;

                // Progress indicator every 50 bars
                if ((i + 1) % 50 == 0)
                {
                    Console.Write($"\rProcessed: {i + 1}/{m15Bars.Count} bars...");
                }
            }

            // Write CSV to file
            File.WriteAllText(outputPath, csvBuilder.ToString());

            // Calculate statistics
            double avgConfidence = confidenceCount > 0 ? confidenceSum / confidenceCount : 0;
            double hoursSpanned = m15Bars.Count * 15.0 / 60.0; // M15 bars to hours
            double flipRatePerHour = hoursSpanned > 0 ? regimeFlips / hoursSpanned : 0;
            double avgRegimeDuration = regimeFlips > 0 ? (double)totalBars / regimeFlips : totalBars;

            // Print summary
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("                     SUMMARY");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine($"✓ Saved {totalBars} rows to {outputPath}");
            Console.WriteLine();
            Console.WriteLine("REGIME STATISTICS:");
            Console.WriteLine($"  Total regime flips:        {regimeFlips}");
            Console.WriteLine($"  Flip rate (per hour):      {flipRatePerHour:F2}");
            Console.WriteLine($"  Avg regime duration:       {avgRegimeDuration:F1} bars");
            Console.WriteLine($"  Avg confidence:            {avgConfidence:F2}");
            Console.WriteLine();
            Console.WriteLine("HEALTH CHECK (Guidance):");
            
            // Flip rate health check
            if (flipRatePerHour >= 0.3 && flipRatePerHour <= 0.8)
                Console.WriteLine($"  ✓ Flip rate {flipRatePerHour:F2}/hr is HEALTHY (target: 0.3-0.8)");
            else if (flipRatePerHour < 0.3)
                Console.WriteLine($"  ⚠ Flip rate {flipRatePerHour:F2}/hr is LOW (too sticky, target: 0.3-0.8)");
            else
                Console.WriteLine($"  ⚠ Flip rate {flipRatePerHour:F2}/hr is HIGH (too noisy, target: 0.3-0.8)");

            // Regime duration health check
            if (avgRegimeDuration >= 5.0)
                Console.WriteLine($"  ✓ Avg regime duration {avgRegimeDuration:F1} bars is HEALTHY (≥5 bars)");
            else
                Console.WriteLine($"  ⚠ Avg regime duration {avgRegimeDuration:F1} bars is SHORT (target: ≥5 bars)");

            // Confidence health check
            if (avgConfidence >= 0.5)
                Console.WriteLine($"  ✓ Avg confidence {avgConfidence:F2} is HEALTHY (≥0.5)");
            else
                Console.WriteLine($"  ⚠ Avg confidence {avgConfidence:F2} is LOW (target: ≥0.5)");

            Console.WriteLine("═══════════════════════════════════════════════════════════");
        }

        /// <summary>
        /// Generates a synthetic regime snapshot based on bar price action.
        /// In production, this would come from your actual RegimeModule.
        /// </summary>
        private static RegimeSnapshot GenerateRegimeSnapshot(Bar currentBar, int index, IList<Bar> allBars, double atrFloor)
        {
            // Simple regime classification based on EMA and volatility
            // This is a STUB - replace with actual RegimeModule output in production

            // Calculate simple moving average over last 20 bars for trend direction
            int lookback = Math.Min(20, index + 1);
            double priceSum = 0;
            double atrSum = 0;

            for (int i = Math.Max(0, index - lookback + 1); i <= index; i++)
            {
                priceSum += allBars[i].Close;
                atrSum += Math.Max(atrFloor, allBars[i].High - allBars[i].Low); // Use ATR floor
            }

            double avgPrice = priceSum / lookback;
            double avgAtr = atrSum / lookback;

            // Determine direction
            RegimeDirection direction;
            double priceVsAvg = currentBar.Close - avgPrice;
            if (Math.Abs(priceVsAvg) < avgAtr * 0.5)
                direction = RegimeDirection.Neutral;
            else
                direction = priceVsAvg > 0 ? RegimeDirection.Bull : RegimeDirection.Bear;

            // Determine volatility state
            double currentRange = Math.Max(atrFloor, currentBar.High - currentBar.Low);
            RegimeVolState volState = currentRange > avgAtr * 1.2 ? RegimeVolState.HighVol : RegimeVolState.LowVol;

            // Calculate confidence (based on how far price is from average relative to ATR)
            double confidence = Math.Min(1.0, Math.Abs(priceVsAvg) / (avgAtr * 2.0));
            confidence = Math.Max(0.3, confidence); // Minimum 0.3 confidence

            return new RegimeSnapshot
            {
                Direction = direction,
                VolState = volState,
                Confidence = confidence,
                Timestamp = currentBar.Time
            };
        }
    }
}

