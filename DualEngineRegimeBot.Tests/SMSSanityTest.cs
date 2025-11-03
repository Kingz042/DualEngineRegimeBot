using System;
using System.Collections.Generic;
using System.Linq;
using DualEngineRegimeBot.Tests.Data;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// SMS (Spread Momentum Score) sanity test for M1 micro energy validation.
    /// 
    /// PURPOSE:
    /// Validates that the SMS engine produces meaningful, responsive energy metrics
    /// across different market conditions (quiet vs volatile periods).
    /// 
    /// HEALTHY METRICS (Guidance):
    /// - SMS range: ~0.2-3.0 (not stuck at extremes)
    /// - Vol/Quiet ratio: >1.3 (responsive to volatility changes)
    /// - ExecMult clamp: 0.5-1.5 (proper clamping observed)
    /// 
    /// SMS INTERPRETATION:
    /// - Low SMS (0-0.5): Market is slow, reduce position sizing
    /// - Medium SMS (0.5-1.5): Normal energy, standard sizing
    /// - High SMS (1.5-3.0+): Strong momentum, increase sizing (but capped)
    /// </summary>
    public static class SMSSanityTest
    {
        /// <summary>
        /// Runs the SMS sanity test on M1 bars.
        /// </summary>
        /// <param name="smsEngine">SMS engine to test</param>
        /// <param name="m1Bars">List of M1 bars (oldest to newest), typically ~1000 bars</param>
        /// <param name="atrFloor">Minimum ATR for normalization (default: 0.5)</param>
        public static void Run(
            ISmsEngine smsEngine,
            IList<Bar> m1Bars,
            double atrFloor = 0.5)
        {
            if (smsEngine == null)
                throw new ArgumentNullException(nameof(smsEngine));
            if (m1Bars == null || m1Bars.Count == 0)
                throw new ArgumentException("Bar list cannot be null or empty", nameof(m1Bars));
            if (atrFloor <= 0)
                throw new ArgumentException("ATR floor must be positive", nameof(atrFloor));

            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("            SMS SANITY TEST - M1 XAUUSD");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine($"Total bars to process: {m1Bars.Count}");
            Console.WriteLine($"ATR floor: {atrFloor:F2}");
            Console.WriteLine();

            // Split bars into two halves: first half (Quiet), second half (Volatile)
            // In real testing, you'd use actual market conditions
            int midPoint = m1Bars.Count / 2;
            
            // Track statistics for both periods
            var quietStats = new SmsStats();
            var volatileStats = new SmsStats();
            var overallStats = new SmsStats();

            // Process each bar
            for (int i = 0; i < m1Bars.Count; i++)
            {
                var bar = m1Bars[i];
                var result = smsEngine.Calculate(bar, atrFloor);

                // Defensive: Guard against NaN/Infinity
                if (double.IsNaN(result.Value) || double.IsInfinity(result.Value))
                {
                    Console.WriteLine($"⚠ WARNING: SMS returned NaN/Infinity at bar {i}, skipping");
                    continue;
                }

                // Track in appropriate period
                var targetStats = i < midPoint ? quietStats : volatileStats;
                targetStats.Add(result.Value, result.ExecMult);
                overallStats.Add(result.Value, result.ExecMult);

                // Progress indicator every 200 bars
                if ((i + 1) % 200 == 0)
                {
                    Console.Write($"\rProcessed: {i + 1}/{m1Bars.Count} bars...");
                }
            }

            Console.WriteLine();
            Console.WriteLine();

            // Calculate statistics
            quietStats.Finalize();
            volatileStats.Finalize();
            overallStats.Finalize();

            // Print results
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("                     SUMMARY");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            
            Console.WriteLine($"SMS range: {overallStats.SmsMin:F2}–{overallStats.SmsMax:F2}");
            
            // Calculate Vol/Quiet ratio (avoid division by zero)
            double volQuietRatio = quietStats.SmsMean > 0 
                ? volatileStats.SmsMean / quietStats.SmsMean 
                : 0;
            Console.WriteLine($"Vol/Quiet ratio: {volQuietRatio:F2}");
            
            Console.WriteLine($"ExecMult clamp observed: {overallStats.ExecMultMin:F2}–{overallStats.ExecMultMax:F2}");
            Console.WriteLine();
            
            Console.WriteLine("DETAILED BREAKDOWN:");
            Console.WriteLine($"  First Half (Quiet):  SMS {quietStats.SmsMin:F2}–{quietStats.SmsMax:F2}, Mean {quietStats.SmsMean:F2}");
            Console.WriteLine($"  Second Half (Vol):   SMS {volatileStats.SmsMin:F2}–{volatileStats.SmsMax:F2}, Mean {volatileStats.SmsMean:F2}");
            Console.WriteLine();
            
            // Health checks
            Console.WriteLine("HEALTH CHECK (Guidance):");
            
            // SMS range check (should be between 0.2-3.0 typically)
            if (overallStats.SmsMin >= 0.1 && overallStats.SmsMax <= 5.0)
                Console.WriteLine($"  ✓ SMS range {overallStats.SmsMin:F2}–{overallStats.SmsMax:F2} is REASONABLE (expect ~0.2-3.0)");
            else if (overallStats.SmsMax < 0.5)
                Console.WriteLine($"  ⚠ SMS range {overallStats.SmsMin:F2}–{overallStats.SmsMax:F2} is TOO LOW (stuck at minimum?)");
            else if (overallStats.SmsMin > 2.0)
                Console.WriteLine($"  ⚠ SMS range {overallStats.SmsMin:F2}–{overallStats.SmsMax:F2} is TOO HIGH (always saturated?)");
            else
                Console.WriteLine($"  ⚠ SMS range {overallStats.SmsMin:F2}–{overallStats.SmsMax:F2} is UNUSUAL (check normalization)");
            
            // Vol/Quiet ratio check (>1.3 indicates responsiveness)
            if (volQuietRatio > 1.3)
                Console.WriteLine($"  ✓ Vol/Quiet ratio {volQuietRatio:F2} is RESPONSIVE (>1.3 target)");
            else if (volQuietRatio > 1.0)
                Console.WriteLine($"  ⚠ Vol/Quiet ratio {volQuietRatio:F2} is MODERATE (target >1.3 for good sensitivity)");
            else
                Console.WriteLine($"  ⚠ Vol/Quiet ratio {volQuietRatio:F2} is LOW (SMS not responsive to volatility changes)");
            
            // ExecMult clamp check (should span 0.5-1.5 range)
            if (overallStats.ExecMultMin >= 0.49 && overallStats.ExecMultMax <= 1.51)
                Console.WriteLine($"  ✓ ExecMult clamp {overallStats.ExecMultMin:F2}–{overallStats.ExecMultMax:F2} is CORRECT ([0.5-1.5])");
            else
                Console.WriteLine($"  ⚠ ExecMult clamp {overallStats.ExecMultMin:F2}–{overallStats.ExecMultMax:F2} is OUT OF RANGE (expect [0.5-1.5])");
            
            Console.WriteLine("═══════════════════════════════════════════════════════════");
        }

        /// <summary>
        /// Helper class to track SMS statistics.
        /// </summary>
        private class SmsStats
        {
            private readonly List<double> _smsValues = new List<double>();
            private readonly List<double> _execMultValues = new List<double>();
            
            public double SmsMin { get; private set; } = double.MaxValue;
            public double SmsMax { get; private set; } = double.MinValue;
            public double SmsMean { get; private set; }
            
            public double ExecMultMin { get; private set; } = double.MaxValue;
            public double ExecMultMax { get; private set; } = double.MinValue;
            public double ExecMultMean { get; private set; }

            public void Add(double sms, double execMult)
            {
                _smsValues.Add(sms);
                _execMultValues.Add(execMult);
                
                SmsMin = Math.Min(SmsMin, sms);
                SmsMax = Math.Max(SmsMax, sms);
                
                ExecMultMin = Math.Min(ExecMultMin, execMult);
                ExecMultMax = Math.Max(ExecMultMax, execMult);
            }

            public void Finalize()
            {
                SmsMean = _smsValues.Count > 0 ? _smsValues.Average() : 0;
                ExecMultMean = _execMultValues.Count > 0 ? _execMultValues.Average() : 0;
            }
        }
    }
}

