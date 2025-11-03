using System;
using System.Collections.Generic;

namespace DualEngineRegimeBot.Tests.Data
{
    /// <summary>
    /// Represents a single price bar (OHLC).
    /// </summary>
    public class Bar
    {
        public DateTime Time { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
    }

    /// <summary>
    /// Timeframe enumeration for bar loading.
    /// </summary>
    public enum TimeFrame
    {
        Minute1,
        Minute5,
        Minute15,
        Hour1,
        Hour4,
        Daily
    }

    /// <summary>
    /// Interface for loading historical bar data.
    /// Implement this with your actual data source (CSV, database, broker API, etc.)
    /// </summary>
    public interface IBarLoader
    {
        /// <summary>
        /// Loads historical bars for a given symbol and timeframe.
        /// </summary>
        /// <param name="symbol">Symbol name (e.g., "XAUUSD")</param>
        /// <param name="timeFrame">Timeframe for bars</param>
        /// <param name="lastNBars">Number of most recent bars to load</param>
        /// <returns>List of bars ordered from oldest to newest</returns>
        IList<Bar> Load(string symbol, TimeFrame timeFrame, int lastNBars);
    }

    /// <summary>
    /// Stub implementation that generates synthetic bars for testing.
    /// Replace with real loader when data source is available.
    /// </summary>
    public class StubBarLoader : IBarLoader
    {
        public IList<Bar> Load(string symbol, TimeFrame timeFrame, int lastNBars)
        {
            var bars = new List<Bar>();
            var now = DateTime.UtcNow;
            var basePrice = symbol == "XAUUSD" ? 1900.0 : 1.0;
            var random = new Random(42); // Deterministic seed for reproducibility

            // Generate synthetic bars with random walk
            for (int i = 0; i < lastNBars; i++)
            {
                var time = now.AddMinutes(-15 * (lastNBars - i - 1));
                var change = (random.NextDouble() - 0.5) * 10.0; // ±5 volatility
                var open = basePrice + change;
                var high = open + Math.Abs(random.NextDouble() * 5.0);
                var low = open - Math.Abs(random.NextDouble() * 5.0);
                var close = low + (high - low) * random.NextDouble();

                bars.Add(new Bar
                {
                    Time = time,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = random.Next(1000, 10000)
                });

                basePrice = close; // Next bar starts from previous close
            }

            return bars;
        }
    }
}

