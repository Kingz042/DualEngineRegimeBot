using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DualEngineRegimeBot.Core.Config
{
    /// <summary>
    /// Configuration schema with versioning and hash tracking for institutional auditability.
    /// </summary>
    public class ConfigSchema
    {
        /// <summary>Bot instance name for identification in logs.</summary>
        public string BotName { get; set; } = "FracMeanDualEngine_V12";
        
        /// <summary>Configuration version tag (YYYY-MM-DD-vN format).</summary>
        public string ConfigVersion { get; set; } = "2025-11-01-v2";
        
        /// <summary>Schema version for compatibility checks.</summary>
        public string SchemaVersion { get; set; } = "1.2";
        
        /// <summary>Deployment timestamp for tracking.</summary>
        public DateTime DeployedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>SHA256 hash of serialized config for tamper detection.</summary>
        public string ConfigHash { get; set; } = "";
        
        /// <summary>Survival mode configuration for extreme drawdown scenarios.</summary>
        public SurvivalModeConfig SurvivalMode { get; set; } = new SurvivalModeConfig();
        
        /// <summary>Parameter bundles for reduced dimensionality.</summary>
        public ParameterBundles Bundles { get; set; } = new ParameterBundles();
        
        /// <summary>Hedge controller configuration.</summary>
        public HedgeConfig Hedge { get; set; } = new HedgeConfig();
        
        /// <summary>News guard configuration for volatility spike handling.</summary>
        public NewsGuardConfig NewsGuard { get; set; } = new NewsGuardConfig();
        
        /// <summary>Drawdown scaling configuration.</summary>
        public DrawdownScalingConfig DrawdownScaling { get; set; } = new DrawdownScalingConfig();
        
        /// <summary>Execution QoS configuration.</summary>
        public ExecutionQoSConfig ExecutionQoS { get; set; } = new ExecutionQoSConfig();
        
        /// <summary>Feature bus configuration.</summary>
        public FeatureBusConfig FeatureBus { get; set; } = new FeatureBusConfig();
        
        /// <summary>Computes SHA256 hash of config for tamper detection.</summary>
        public string ComputeHash()
        {
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var json = JsonSerializer.Serialize(this, options);
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(hashBytes);
        }
    }
    
    /// <summary>
    /// Survival mode for extreme drawdown - caps risk to preserve capital.
    /// </summary>
    public class SurvivalModeConfig
    {
        /// <summary>Enable survival mode (default: false).</summary>
        public bool Enabled { get; set; } = false;
        
        /// <summary>Risk cap multiplier when in survival mode (e.g., 0.10 = 10% of normal).</summary>
        public double RiskCap { get; set; } = 0.10;
        
        /// <summary>Drawdown threshold to trigger survival mode (default: 10%).</summary>
        public double TriggerThresholdPct { get; set; } = 10.0;
    }
    
    /// <summary>
    /// Parameter bundles for reduced dimensionality and easier optimization.
    /// </summary>
    public class ParameterBundles
    {
        /// <summary>EMA center period for M1 (derive 5,8,13,20 from center ± span).</summary>
        public int EmaCenter { get; set; } = 10;
        
        /// <summary>EMA span for deriving periods (span=5 → [5,8,10,13,20]).</summary>
        public int EmaSpan { get; set; } = 5;
        
        /// <summary>Volatility band half-width (VolHi=1.0+band, VolLo=1.0-band).</summary>
        public double VolBand { get; set; } = 0.10;
        
        /// <summary>Stop-loss base multiplier.</summary>
        public double SLmult { get; set; } = 2.0;
        
        /// <summary>Stop-loss delta for regime adjustment.</summary>
        public double SLdelta { get; set; } = 0.5;
        
        /// <summary>SMS z-score clip level (fixed).</summary>
        public double SMSzClip { get; set; } = 3.0;
        
        /// <summary>SMS confirmation threshold (default 0.30, consider fixing).</summary>
        public double SMSConfirmThreshold { get; set; } = 0.30;
        
        /// <summary>Max concurrent hedges (default 1).</summary>
        public int MaxHedges { get; set; } = 1;
        
        /// <summary>Hedge multiplier for trigger distance.</summary>
        public double Hmult { get; set; } = 1.2;
        
        /// <summary>Directional threshold for regime classification.</summary>
        public double DirThreshold { get; set; } = 0.05;
        
        /// <summary>Directional hysteresis band.</summary>
        public double DirHysteresis { get; set; } = 0.10;
        
        /// <summary>Derives EMA periods from center and span.</summary>
        public int[] GetEmaPeriods()
        {
            int half = EmaSpan / 2;
            return new[] 
            { 
                EmaCenter - EmaSpan,
                EmaCenter - half,
                EmaCenter,
                EmaCenter + half,
                EmaCenter + EmaSpan
            };
        }
        
        /// <summary>Gets high volatility threshold.</summary>
        public double GetVolHi() => 1.0 + VolBand;
        
        /// <summary>Gets low volatility threshold.</summary>
        public double GetVolLo() => 1.0 - VolBand;
        
        /// <summary>Gets stop-loss multiplier for high volatility.</summary>
        public double GetSLHighVol() => SLmult + SLdelta;
        
        /// <summary>Gets stop-loss multiplier for low volatility.</summary>
        public double GetSLLowVol() => SLmult - SLdelta;
    }
    
    /// <summary>
    /// Hedge controller configuration for defense-only lifecycle.
    /// </summary>
    public class HedgeConfig
    {
        /// <summary>Enable hedge controller (default: true).</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>Hedge trigger multiplier (Hmult × ATR_M1).</summary>
        public double TriggerMultiplier { get; set; } = 1.2;
        
        /// <summary>Hedge volume cap as fraction of PH (default: 1.0 = 100%).</summary>
        public double VolumeCap { get; set; } = 1.0;
        
        /// <summary>Cooldown between hedges (milliseconds).</summary>
        public int CooldownMs { get; set; } = 2000;
        
        /// <summary>Spread guard multiplier for hedge entries.</summary>
        public double SpreadGuardMultiplier { get; set; } = 1.5;
        
        /// <summary>Recovery target multiplier for unwind (0.6× ATR).</summary>
        public double RecoveryTargetMultiplier { get; set; } = 0.6;
        
        /// <summary>Micro revival SMS threshold for unwind.</summary>
        public double MicroRevivalSMS { get; set; } = 1.1;
        
        /// <summary>Macro alignment confidence threshold for unwind.</summary>
        public double MacroAlignmentConfidence { get; set; } = 0.55;
        
        /// <summary>Time decay threshold (minutes) before forced unwind.</summary>
        public int TimeDecayMinutes { get; set; } = 15;
        
        /// <summary>Time decay unwind fraction (50%).</summary>
        public double TimeDecayUnwindFraction { get; set; } = 0.5;
        
        /// <summary>Hedge stop multiplier (0.8× ATR).</summary>
        public double HedgeStopMultiplier { get; set; } = 0.8;
        
        /// <summary>Required margin buffer multiplier.</summary>
        public double MarginBufferMultiplier { get; set; } = 2.0;
    }
    
    /// <summary>
    /// News guard configuration for phased volatility spike handling.
    /// </summary>
    public class NewsGuardConfig
    {
        /// <summary>Enable news guard (default: true).</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>SMS delta threshold for spike detection (2σ/5min).</summary>
        public double SMSDeltaThreshold { get; set; } = 2.0;
        
        /// <summary>Spread blowout multiplier for spike detection.</summary>
        public double SpreadBlowoutMultiplier { get; set; } = 3.0;
        
        /// <summary>Phase 1: Block duration (minutes) - block all entries & hedges.</summary>
        public int BlockPhaseMinutes { get; set; } = 2;
        
        /// <summary>Phase 2: Unwind-only duration (minutes).</summary>
        public int UnwindOnlyPhaseMinutes { get; set; } = 3;
        
        /// <summary>Phase 3: Restricted hedge duration (minutes) - require 2× Hmult.</summary>
        public int RestrictedPhaseMinutes { get; set; } = 10;
        
        /// <summary>Restricted phase Hmult multiplier (2×).</summary>
        public double RestrictedPhaseHmultMultiplier { get; set; } = 2.0;
    }
    
    /// <summary>
    /// Graduated drawdown scaling configuration.
    /// </summary>
    public class DrawdownScalingConfig
    {
        /// <summary>Enable graduated scaling (default: true).</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>Drawdown threshold levels (%) - [2, 5, 10].</summary>
        public double[] ThresholdLevels { get; set; } = new[] { 2.0, 5.0, 10.0 };
        
        /// <summary>Damper values for each level - [1.0, 0.7, 0.4, 0.0].</summary>
        public double[] DamperValues { get; set; } = new[] { 1.0, 0.7, 0.4, 0.0 };
        
        /// <summary>Use hybrid peak reference (max of all-time high and 95% of rolling 30d high).</summary>
        public bool UseHybridPeak { get; set; } = true;
        
        /// <summary>Rolling peak window (days).</summary>
        public int RollingPeakWindowDays { get; set; } = 30;
        
        /// <summary>Rolling peak multiplier (0.95).</summary>
        public double RollingPeakMultiplier { get; set; } = 0.95;
    }
    
    /// <summary>
    /// Execution quality-of-service configuration.
    /// </summary>
    public class ExecutionQoSConfig
    {
        /// <summary>Enable execution QoS logging (default: true).</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>Base slippage multiplier (0.1× ATR).</summary>
        public double BaseSlippageMultiplier { get; set; } = 0.1;
        
        /// <summary>Latency slippage coefficient.</summary>
        public double LatencySlippageCoefficient { get; set; } = 1.0;
        
        /// <summary>Impact slippage coefficient (0.5× spread).</summary>
        public double ImpactSlippageCoefficient { get; set; } = 0.5;
        
        /// <summary>Target reject rate threshold (2%).</summary>
        public double TargetRejectRatePct { get; set; } = 2.0;
        
        /// <summary>Target average slippage (0.25× ATR).</summary>
        public double TargetAvgSlippageMultiplier { get; set; } = 0.25;
    }
    
    /// <summary>
    /// Feature bus configuration for event distribution.
    /// </summary>
    public class FeatureBusConfig
    {
        /// <summary>Enable feature bus (default: true).</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>Enable dead letter queue (default: true).</summary>
        public bool EnableDLQ { get; set; } = true;
        
        /// <summary>DLQ rate limit (errors per hour).</summary>
        public int DLQRateLimitPerHour { get; set; } = 10;
        
        /// <summary>Halt entries on DLQ rate limit breach.</summary>
        public bool HaltEntriesOnDLQBreach { get; set; } = true;
        
        /// <summary>Event version for compatibility tracking.</summary>
        public string EventVersion { get; set; } = "1.0";
    }
}

