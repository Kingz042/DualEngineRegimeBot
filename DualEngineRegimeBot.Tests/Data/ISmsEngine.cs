using System;
using DualEngineRegimeBot.Core.Engines.SMS;

namespace DualEngineRegimeBot.Tests.Data
{
    /// <summary>
    /// SMS (Spread Momentum Score) calculation result.
    /// Represents the "micro energy" of the market based on EMA spread slope normalized by ATR.
    /// </summary>
    public class SmsResult
    {
        /// <summary>
        /// Raw SMS value - normalized spread momentum.
        /// Healthy range: ~0.2-3.0
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Execution multiplier derived from SMS, clamped to [0.5, 1.5].
        /// Used to scale position sizing based on market energy.
        /// </summary>
        public double ExecMult { get; set; }

        /// <summary>
        /// Current ATR used for normalization.
        /// </summary>
        public double Atr { get; set; }
    }

    /// <summary>
    /// Interface for SMS (Spread Momentum Score) engine.
    /// Calculates market "micro energy" based on EMA(5,20) spread slope normalized by ATR.
    /// </summary>
    public interface ISmsEngine
    {
        /// <summary>
        /// Calculates SMS for a given bar.
        /// </summary>
        /// <param name="bar">Current bar to process</param>
        /// <param name="atrFloor">Minimum ATR value for normalization (prevents division by zero)</param>
        /// <returns>SMS calculation result</returns>
        SmsResult Calculate(Bar bar, double atrFloor = 0.5);
    }

    /// <summary>
    /// Real SMS engine wrapper for testing.
    /// Uses the production SmsEngine implementation.
    /// </summary>
    public class StubSmsEngine : ISmsEngine
    {
        private readonly SmsEngine _engine;

        public StubSmsEngine()
        {
            _engine = new SmsEngine(new SmsConfig
            {
                EmaFast = 5,
                EmaSlow = 20,
                AtrLen = 14,
                Window = 20,
                AtrFloorPips = 0.5,
                UseZScore = true,
                Baseline = 1.0,
                TanhK = 0.35,
                ClampMin = 0.5,
                ClampMax = 1.5
            });
        }

        public SmsResult Calculate(Bar bar, double atrFloor = 0.5)
        {
            // Update ATR floor if different from default
            if (Math.Abs(atrFloor - 0.5) > 0.001)
            {
                // For simplicity, we use the engine's default floor
                // In production, you'd pass this dynamically
            }

            var result = _engine.Calculate(bar.Close, bar.High, bar.Low);

            return new SmsResult
            {
                Value = result.Value,
                ExecMult = result.ExecMult,
                Atr = result.Atr
            };
        }
    }
}
