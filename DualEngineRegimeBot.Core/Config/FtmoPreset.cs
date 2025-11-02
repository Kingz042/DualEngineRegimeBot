using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DualEngineRegimeBot.Core.Config
{
    /// <summary>
    /// Immutable FTMO-compliant preset with strict risk parameters.
    /// </summary>
    public sealed record FtmoPreset
    {
        /// <summary>Preset name/version tag.</summary>
        public string VersionTag { get; init; } = "FTMO_Safe_v1.2";
        
        /// <summary>Maximum risk per trade as percentage of account.</summary>
        public double MaxRiskPercentPerTrade { get; init; } = 0.5;
        
        /// <summary>Maximum daily loss as percentage of account.</summary>
        public double MaxDailyLossPercent { get; init; } = 5.0;
        
        /// <summary>Maximum total drawdown as percentage of account.</summary>
        public double MaxDrawdownPercent { get; init; } = 10.0;
        
        /// <summary>Maximum number of concurrent open positions.</summary>
        public int MaxOpenPositions { get; init; } = 3;
        
        /// <summary>Maximum entry spread in points.</summary>
        public double MaxEntrySpreadPts { get; init; } = 2.0;
        
        /// <summary>Hedge cooldown period in minutes.</summary>
        public int HedgeCooldownMinutes { get; init; } = 15;
        
        /// <summary>Session start time (UTC hour).</summary>
        public int SessionStartHour { get; init; } = 7;
        
        /// <summary>Session end time (UTC hour) - EXCLUSIVE.</summary>
        public int SessionEndHour { get; init; } = 21;
        
        /// <summary>Broker UTC offset in hours for daily reset.</summary>
        public int BrokerUtcOffsetHours { get; init; } = 2;
        
        /// <summary>NewsGuard block phase duration in minutes.</summary>
        public int NewsBlockPhaseMinutes { get; init; } = 2;
        
        /// <summary>NewsGuard unwind-only phase duration in minutes.</summary>
        public int NewsUnwindPhaseMinutes { get; init; } = 3;
        
        /// <summary>NewsGuard restricted phase duration in minutes.</summary>
        public int NewsRestrictedPhaseMinutes { get; init; } = 9;
        
        /// <summary>Minimum ATR floor for gold in points.</summary>
        public double AtrFloorGoldPts { get; init; } = 5.0;
        
        /// <summary>Survival mode enabled.</summary>
        public bool SurvivalModeEnabled { get; init; } = true;
        
        /// <summary>Survival mode threshold as percentage of max drawdown.</summary>
        public double SurvivalModeThresholdPercent { get; init; } = 80.0;
        
        /// <summary>Label prefix for all positions.</summary>
        public string LabelPrefix { get; init; } = "FTMO_DER";
        
        /// <summary>
        /// Creates the default FTMO-safe preset.
        /// </summary>
        public static FtmoPreset CreateDefault()
        {
            return new FtmoPreset();
        }
        
        /// <summary>
        /// Computes SHA-256 hash of the configuration for audit trail.
        /// Uses canonical JSON serialization for deterministic hashing.
        /// </summary>
        /// <returns>Hex-encoded SHA-256 hash.</returns>
        public string ConfigHashSha256()
        {
            // Serialize to JSON with sorted properties for determinism
            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            string json = JsonSerializer.Serialize(this, options);
            
            // Compute SHA-256
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            
            // Convert to hex string
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        
        /// <summary>
        /// Validates the preset for consistency.
        /// </summary>
        /// <returns>True if valid, false otherwise.</returns>
        public bool Validate(out string? errorMessage)
        {
            if (MaxRiskPercentPerTrade <= 0 || MaxRiskPercentPerTrade > 2.0)
            {
                errorMessage = "MaxRiskPercentPerTrade must be between 0 and 2.0";
                return false;
            }
            
            if (MaxDailyLossPercent <= 0 || MaxDailyLossPercent > 10.0)
            {
                errorMessage = "MaxDailyLossPercent must be between 0 and 10.0";
                return false;
            }
            
            if (MaxDrawdownPercent <= 0 || MaxDrawdownPercent > 15.0)
            {
                errorMessage = "MaxDrawdownPercent must be between 0 and 15.0";
                return false;
            }
            
            if (MaxOpenPositions < 1 || MaxOpenPositions > 10)
            {
                errorMessage = "MaxOpenPositions must be between 1 and 10";
                return false;
            }
            
            if (MaxEntrySpreadPts <= 0)
            {
                errorMessage = "MaxEntrySpreadPts must be positive";
                return false;
            }
            
            if (SessionStartHour < 0 || SessionStartHour >= 24)
            {
                errorMessage = "SessionStartHour must be between 0 and 23";
                return false;
            }
            
            if (SessionEndHour < 0 || SessionEndHour >= 24)
            {
                errorMessage = "SessionEndHour must be between 0 and 23";
                return false;
            }
            
            if (SessionEndHour <= SessionStartHour)
            {
                errorMessage = "SessionEndHour must be after SessionStartHour";
                return false;
            }
            
            if (BrokerUtcOffsetHours < -12 || BrokerUtcOffsetHours > 14)
            {
                errorMessage = "BrokerUtcOffsetHours must be between -12 and 14";
                return false;
            }
            
            if (AtrFloorGoldPts <= 0)
            {
                errorMessage = "AtrFloorGoldPts must be positive";
                return false;
            }
            
            if (SurvivalModeThresholdPercent <= 0 || SurvivalModeThresholdPercent > 100)
            {
                errorMessage = "SurvivalModeThresholdPercent must be between 0 and 100";
                return false;
            }
            
            errorMessage = null;
            return true;
        }
        
        /// <summary>
        /// Checks if a given UTC time is within trading session.
        /// Session end is EXCLUSIVE (no new entries at end hour).
        /// </summary>
        /// <param name="utcTime">Time to check.</param>
        /// <returns>True if within session.</returns>
        public bool IsWithinSession(DateTime utcTime)
        {
            int hour = utcTime.Hour;
            
            if (SessionStartHour < SessionEndHour)
            {
                // Normal case: e.g., 7-21
                return hour >= SessionStartHour && hour < SessionEndHour;
            }
            else
            {
                // Crosses midnight: e.g., 22-6
                return hour >= SessionStartHour || hour < SessionEndHour;
            }
        }
        
        /// <summary>
        /// Gets broker local time from UTC.
        /// </summary>
        /// <param name="utcTime">UTC time.</param>
        /// <returns>Broker local time.</returns>
        public DateTime GetBrokerLocalTime(DateTime utcTime)
        {
            return utcTime.AddHours(BrokerUtcOffsetHours);
        }
        
        /// <summary>
        /// Checks if broker midnight has occurred between two times.
        /// Used for daily loss reset detection.
        /// </summary>
        /// <param name="previousUtc">Previous UTC time.</param>
        /// <param name="currentUtc">Current UTC time.</param>
        /// <returns>True if broker midnight crossed.</returns>
        public bool HasCrossedBrokerMidnight(DateTime previousUtc, DateTime currentUtc)
        {
            DateTime prevBroker = GetBrokerLocalTime(previousUtc);
            DateTime currBroker = GetBrokerLocalTime(currentUtc);
            
            return prevBroker.Date < currBroker.Date;
        }
    }
}

