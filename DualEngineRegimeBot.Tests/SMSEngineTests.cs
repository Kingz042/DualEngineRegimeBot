using System;
using System.Collections.Generic;
using Xunit;
using DualEngineRegimeBot.Core.Engines.SMS;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// Unit tests for SmsEngine - validates SMS calculation correctness,
    /// responsiveness to volatility, and ExecMult mapping.
    /// </summary>
    public class SmsEngineTests
    {
        [Fact]
        public void SmsEngine_ShouldInitialize_WithDefaultConfig()
        {
            var config = new SmsConfig();
            var engine = new SmsEngine(config);

            var result = engine.GetLastResult();
            Assert.Equal(0, result.Value);
            Assert.Equal(1.0, result.ExecMult);
            Assert.False(result.IsValid); // Not enough bars yet
        }

        [Fact]
        public void SmsEngine_ShouldValidateConfig_AndThrowOnInvalid()
        {
            // Invalid: EmaSlow <= EmaFast
            var config1 = new SmsConfig { EmaFast = 20, EmaSlow = 10 };
            Assert.Throws<ArgumentException>(() => config1.Validate());

            // Invalid: Negative ATR floor
            var config2 = new SmsConfig { AtrFloorPips = -0.5 };
            Assert.Throws<ArgumentException>(() => config2.Validate());

            // Invalid: ClampMin >= ClampMax
            var config3 = new SmsConfig { ClampMin = 1.5, ClampMax = 0.5 };
            Assert.Throws<ArgumentException>(() => config3.Validate());

            // Valid config should not throw
            var validConfig = new SmsConfig();
            validConfig.Validate(); // Should not throw
        }

        [Fact]
        public void SmsEngine_ShouldApplyAtrFloor_WhenAtrBelowFloor()
        {
            var config = new SmsConfig
            {
                AtrFloorPips = 1.0,
                Window = 5,
                EmaFast = 2,
                EmaSlow = 5,
                AtrLen = 3
            };
            var engine = new SmsEngine(config);

            // Generate bars with tiny ranges (below floor)
            for (int i = 0; i < 20; i++)
            {
                double close = 1900.0 + i * 0.01;
                engine.Calculate(close, close + 0.001, close - 0.001); // Range = 0.002 << 1.0 floor
            }

            var telemetry = engine.GetTelemetry();
            var result = engine.GetLastResult();

            // ATR floor should have been applied
            Assert.True(telemetry.AtrFloorHits > 0, "ATR floor should have been hit with tiny ranges");
            Assert.True(result.Atr >= config.AtrFloorPips, "ATR used should be >= floor");
        }

        [Fact]
        public void SmsEngine_ShouldNotReturnNaN_WhenAtrIsZero()
        {
            var config = new SmsConfig
            {
                AtrFloorPips = 0.1,
                Window = 5,
                EmaFast = 2,
                EmaSlow = 5
            };
            var engine = new SmsEngine(config);

            // Generate flat bars (zero range)
            for (int i = 0; i < 30; i++)
            {
                double close = 1900.0;
                engine.Calculate(close, close, close); // Flat bar
            }

            var result = engine.GetLastResult();

            // Should not be NaN or Infinity
            Assert.False(double.IsNaN(result.Value), "SMS should not be NaN");
            Assert.False(double.IsInfinity(result.Value), "SMS should not be Infinity");
            Assert.False(double.IsNaN(result.ExecMult), "ExecMult should not be NaN");
            Assert.False(double.IsInfinity(result.ExecMult), "ExecMult should not be Infinity");
        }

        [Fact]
        public void SmsEngine_QuietMarket_ShouldHaveLowerSMS_ThanVolatileMarket()
        {
            var config = new SmsConfig
            {
                Window = 20,
                EmaFast = 5,
                EmaSlow = 20,
                AtrLen = 14,
                UseZScore = false // Use raw SMS for clearer comparison
            };

            // Test quiet market
            var quietEngine = new SmsEngine(config);
            for (int i = 0; i < 100; i++)
            {
                double close = 1900.0 + i * 0.1; // Slow drift
                double high = close + 0.5;
                double low = close - 0.5;
                quietEngine.Calculate(close, high, low);
            }
            double quietSms = quietEngine.GetLastResult().Value;

            // Test volatile market
            var volatileEngine = new SmsEngine(config);
            var random = new Random(42);
            for (int i = 0; i < 100; i++)
            {
                double close = 1900.0 + (random.NextDouble() - 0.5) * 20; // Large swings
                double high = close + random.NextDouble() * 5;
                double low = close - random.NextDouble() * 5;
                volatileEngine.Calculate(close, high, low);
            }
            double volatileSms = volatileEngine.GetLastResult().Value;

            // Volatile market should have higher SMS
            Assert.True(volatileSms > quietSms,
                $"Volatile SMS ({volatileSms:F3}) should be > Quiet SMS ({quietSms:F3})");

            // Quiet SMS should be in reasonable range
            Assert.InRange(quietSms, 0.0, 2.0);

            // Volatile SMS should be higher but still reasonable
            Assert.InRange(volatileSms, quietSms, 6.0);
        }

        [Fact]
        public void SmsEngine_ExecMult_ShouldIncrease_WhenSMSAboveBaseline()
        {
            var config = new SmsConfig
            {
                Baseline = 1.0,
                TanhK = 0.35,
                ClampMin = 0.5,
                ClampMax = 1.5,
                Window = 10,
                UseZScore = false
            };
            var engine = new SmsEngine(config);

            // Generate bars that create high SMS (volatile moves)
            var random = new Random(42);
            for (int i = 0; i < 50; i++)
            {
                double close = 1900.0 + (random.NextDouble() - 0.5) * 30;
                double high = close + random.NextDouble() * 8;
                double low = close - random.NextDouble() * 8;
                engine.Calculate(close, high, low);
            }

            var result = engine.GetLastResult();

            // High volatility should push SMS above baseline
            // ExecMult should be > 1.0 (boosting)
            if (result.Value > config.Baseline)
            {
                Assert.True(result.ExecMult > 1.0,
                    $"ExecMult ({result.ExecMult:F3}) should be > 1.0 when SMS ({result.Value:F3}) > baseline ({config.Baseline})");
            }

            // ExecMult should be within clamp range
            Assert.InRange(result.ExecMult, config.ClampMin, config.ClampMax);
        }

        [Fact]
        public void SmsEngine_ExecMult_ShouldDecrease_WhenSMSBelowBaseline()
        {
            var config = new SmsConfig
            {
                Baseline = 1.0,
                TanhK = 0.35,
                ClampMin = 0.5,
                ClampMax = 1.5,
                Window = 10,
                UseZScore = false
            };
            var engine = new SmsEngine(config);

            // Generate bars that create low SMS (quiet market)
            for (int i = 0; i < 50; i++)
            {
                double close = 1900.0 + i * 0.05; // Very slow drift
                double high = close + 0.1;
                double low = close - 0.1;
                engine.Calculate(close, high, low);
            }

            var result = engine.GetLastResult();

            // Low volatility should keep SMS below baseline
            // ExecMult should be < 1.0 (throttling)
            if (result.Value < config.Baseline)
            {
                Assert.True(result.ExecMult < 1.0,
                    $"ExecMult ({result.ExecMult:F3}) should be < 1.0 when SMS ({result.Value:F3}) < baseline ({config.Baseline})");
            }

            // ExecMult should be within clamp range
            Assert.InRange(result.ExecMult, config.ClampMin, config.ClampMax);
        }

        [Fact]
        public void SmsEngine_ExecMult_ShouldBeClamped_ToConfiguredRange()
        {
            var config = new SmsConfig
            {
                ClampMin = 0.6,
                ClampMax = 1.4,
                Window = 10
            };
            var engine = new SmsEngine(config);

            // Generate extreme volatility
            var random = new Random(123);
            for (int i = 0; i < 100; i++)
            {
                double close = 1900.0 + (random.NextDouble() - 0.5) * 100;
                double high = close + random.NextDouble() * 50;
                double low = close - random.NextDouble() * 50;
                engine.Calculate(close, high, low);
            }

            var result = engine.GetLastResult();

            // ExecMult must be within custom clamp range
            Assert.InRange(result.ExecMult, config.ClampMin, config.ClampMax);
            Assert.True(result.ExecMult >= config.ClampMin, $"ExecMult {result.ExecMult} should be >= {config.ClampMin}");
            Assert.True(result.ExecMult <= config.ClampMax, $"ExecMult {result.ExecMult} should be <= {config.ClampMax}");
        }

        [Fact]
        public void SmsEngine_ShouldBecomeValid_AfterWindowBars()
        {
            var config = new SmsConfig { Window = 20 };
            var engine = new SmsEngine(config);

            // Process bars
            for (int i = 0; i < 19; i++)
            {
                double close = 1900.0 + i;
                engine.Calculate(close, close + 1, close - 1);
                Assert.False(engine.GetLastResult().IsValid, $"SMS should not be valid at bar {i + 1}");
            }

            // At window size, should become valid
            engine.Calculate(1920.0, 1921.0, 1919.0);
            Assert.True(engine.GetLastResult().IsValid, "SMS should be valid after Window bars");
        }

        [Fact]
        public void SmsEngine_Reset_ShouldClearAllState()
        {
            var config = new SmsConfig { Window = 10 };
            var engine = new SmsEngine(config);

            // Process some bars
            for (int i = 0; i < 50; i++)
            {
                double close = 1900.0 + i;
                engine.Calculate(close, close + 1, close - 1);
            }

            var telemetryBefore = engine.GetTelemetry();
            Assert.True(telemetryBefore.TotalBars > 0, "Should have processed bars");

            // Reset
            engine.Reset();

            var telemetryAfter = engine.GetTelemetry();
            var resultAfter = engine.GetLastResult();

            Assert.Equal(0, telemetryAfter.TotalBars);
            Assert.Equal(0, telemetryAfter.AtrFloorHits);
            Assert.False(resultAfter.IsValid);
        }

        [Fact]
        public void SmsEngine_ShouldHandleInvalidInputs_Gracefully()
        {
            var config = new SmsConfig();
            var engine = new SmsEngine(config);

            // Process some valid bars first
            for (int i = 0; i < 10; i++)
            {
                double close = 1900.0 + i;
                engine.Calculate(close, close + 1, close - 1);
            }

            var lastValidResult = engine.GetLastResult();

            // Feed invalid inputs
            var result1 = engine.Calculate(double.NaN, 1900, 1899);
            Assert.Equal(lastValidResult.Value, result1.Value); // Should return last valid

            var result2 = engine.Calculate(1900, double.PositiveInfinity, 1899);
            Assert.Equal(lastValidResult.Value, result2.Value);

            var result3 = engine.Calculate(1900, 1899, 1900); // High < Low
            Assert.Equal(lastValidResult.Value, result3.Value);

            var result4 = engine.Calculate(-100, -99, -101); // Negative prices
            Assert.Equal(lastValidResult.Value, result4.Value);
        }

        [Fact]
        public void SmsEngine_WithZScore_ShouldNormalize_SMS()
        {
            var configWithZScore = new SmsConfig { UseZScore = true, Window = 20 };
            var configWithoutZScore = new SmsConfig { UseZScore = false, Window = 20 };

            var engineWithZ = new SmsEngine(configWithZScore);
            var engineWithoutZ = new SmsEngine(configWithoutZScore);

            // Generate same data for both
            var random = new Random(456);
            for (int i = 0; i < 100; i++)
            {
                double close = 1900.0 + (random.NextDouble() - 0.5) * 10;
                double high = close + random.NextDouble() * 2;
                double low = close - random.NextDouble() * 2;

                engineWithZ.Calculate(close, high, low);
                engineWithoutZ.Calculate(close, high, low);
            }

            var resultWithZ = engineWithZ.GetLastResult();
            var resultWithoutZ = engineWithoutZ.GetLastResult();

            // Both should be valid
            Assert.True(resultWithZ.IsValid);
            Assert.True(resultWithoutZ.IsValid);

            // Z-scored SMS may differ from raw but both should be non-negative
            Assert.True(resultWithZ.Value >= 0);
            Assert.True(resultWithoutZ.Value >= 0);

            // Both should produce valid ExecMult
            Assert.InRange(resultWithZ.ExecMult, 0.5, 1.5);
            Assert.InRange(resultWithoutZ.ExecMult, 0.5, 1.5);
        }

        [Fact]
        public void SmsEngine_Telemetry_ShouldTrack_AtrFloorHitRate()
        {
            var config = new SmsConfig
            {
                AtrFloorPips = 2.0, // Relatively high floor
                Window = 10
            };
            var engine = new SmsEngine(config);

            // Generate bars with varying ranges
            for (int i = 0; i < 100; i++)
            {
                double close = 1900.0 + i * 0.1;
                // Half the bars have range < floor, half above
                double range = i % 2 == 0 ? 0.5 : 3.0;
                engine.Calculate(close, close + range / 2, close - range / 2);
            }

            var telemetry = engine.GetTelemetry();

            Assert.Equal(100, telemetry.TotalBars);
            Assert.True(telemetry.AtrFloorHits > 0, "Should have some ATR floor hits");
            Assert.InRange(telemetry.AtrFloorHitRate, 0.0, 1.0);
            Assert.True(telemetry.LastAtr >= config.AtrFloorPips, "Last ATR should respect floor");
        }
    }
}

