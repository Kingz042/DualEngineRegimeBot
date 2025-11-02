using System;

namespace DualEngineRegimeBot.Core.Execution
{
    /// <summary>
    /// Signal side enumeration for engine decisions.
    /// </summary>
    public enum EngineSignalSide
    {
        /// <summary>No signal or neutral.</summary>
        None,
        
        /// <summary>Long signal.</summary>
        Long,
        
        /// <summary>Short signal.</summary>
        Short
    }
    
    /// <summary>
    /// Immutable record representing a signal from a trading engine.
    /// </summary>
    public sealed record EngineSignal(
        string EngineName,
        EngineSignalSide Side,
        double Score,           // 0..1 normalized
        double StopDistancePts, // in points
        double TakeDistancePts, // in points (optional)
        DateTime TimestampUtc);
    
    /// <summary>
    /// Configuration for execution router.
    /// </summary>
    public sealed class ExecutionRouterConfig
    {
        /// <summary>Maximum spread in points for clean entry.</summary>
        public double MaxEntrySpreadPts { get; set; } = 2.0;
        
        /// <summary>Penalty multiplier when spread exceeds threshold.</summary>
        public double SpreadPenaltyMultiplier { get; set; } = 0.8;
    }
    
    /// <summary>
    /// Interface for execution router that arbitrates engine signals.
    /// </summary>
    public interface IExecutionRouter
    {
        /// <summary>
        /// Chooses the best signal from available engines based on policy.
        /// </summary>
        /// <param name="mr">Macro regime engine signal (optional).</param>
        /// <param name="trend">Trend engine signal (optional).</param>
        /// <param name="macroConfidence">Current macro regime confidence (0..1).</param>
        /// <param name="macroAllowsLong">Whether macro regime allows long entries.</param>
        /// <param name="macroAllowsShort">Whether macro regime allows short entries.</param>
        /// <param name="spreadPts">Current spread in points.</param>
        /// <param name="newsBlocked">Whether news guard is blocking entries.</param>
        /// <param name="decisionReason">Human-readable reason for decision.</param>
        /// <returns>Chosen signal or null if no valid signal.</returns>
        EngineSignal? Choose(
            EngineSignal? mr,
            EngineSignal? trend,
            double macroConfidence,
            bool macroAllowsLong,
            bool macroAllowsShort,
            double spreadPts,
            bool newsBlocked,
            out string decisionReason);
    }
    
    /// <summary>
    /// Execution router that arbitrates signals from multiple engines.
    /// Applies macro gating, spread penalties, and news filters.
    /// </summary>
    public sealed class ExecutionRouter : IExecutionRouter
    {
        private readonly ExecutionRouterConfig _config;
        
        /// <summary>
        /// Initializes a new instance of ExecutionRouter.
        /// </summary>
        /// <param name="config">Router configuration.</param>
        public ExecutionRouter(ExecutionRouterConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        /// <inheritdoc/>
        public EngineSignal? Choose(
            EngineSignal? mr,
            EngineSignal? trend,
            double macroConfidence,
            bool macroAllowsLong,
            bool macroAllowsShort,
            double spreadPts,
            bool newsBlocked,
            out string decisionReason)
        {
            // Validate inputs
            if (macroConfidence < 0 || macroConfidence > 1)
            {
                throw new ArgumentException("Macro confidence must be between 0 and 1", nameof(macroConfidence));
            }
            
            if (spreadPts < 0)
            {
                throw new ArgumentException("Spread cannot be negative", nameof(spreadPts));
            }
            
            // News block trumps all
            if (newsBlocked)
            {
                decisionReason = "News guard blocking all entries";
                return null;
            }
            
            // Filter by macro gating
            var candidates = new System.Collections.Generic.List<(EngineSignal signal, double adjustedScore)>();
            
            if (mr != null && IsAllowedByMacro(mr.Side, macroAllowsLong, macroAllowsShort))
            {
                double score = CalculateAdjustedScore(mr, macroConfidence, spreadPts);
                candidates.Add((mr, score));
            }
            
            if (trend != null && IsAllowedByMacro(trend.Side, macroAllowsLong, macroAllowsShort))
            {
                double score = CalculateAdjustedScore(trend, macroConfidence, spreadPts);
                candidates.Add((trend, score));
            }
            
            // No valid candidates
            if (candidates.Count == 0)
            {
                decisionReason = BuildNoSignalReason(mr, trend, macroAllowsLong, macroAllowsShort);
                return null;
            }
            
            // Single candidate
            if (candidates.Count == 1)
            {
                var (signal, score) = candidates[0];
                decisionReason = $"Single valid signal: {signal.EngineName} {signal.Side} (score={score:F3})";
                return signal;
            }
            
            // Multiple candidates - choose best
            var (bestSignal, bestScore) = candidates[0];
            var (otherSignal, otherScore) = candidates[1];
            
            // Higher score wins
            if (bestScore > otherScore)
            {
                decisionReason = $"Chose {bestSignal.EngineName} (score={bestScore:F3}) over {otherSignal.EngineName} (score={otherScore:F3})";
                return bestSignal;
            }
            
            if (otherScore > bestScore)
            {
                decisionReason = $"Chose {otherSignal.EngineName} (score={otherScore:F3}) over {bestSignal.EngineName} (score={bestScore:F3})";
                return otherSignal;
            }
            
            // Tie - prefer tighter stop
            if (bestSignal.StopDistancePts < otherSignal.StopDistancePts)
            {
                decisionReason = $"Tie (score={bestScore:F3}), chose {bestSignal.EngineName} for tighter stop ({bestSignal.StopDistancePts:F1}pts vs {otherSignal.StopDistancePts:F1}pts)";
                return bestSignal;
            }
            
            decisionReason = $"Tie (score={bestScore:F3}), chose {otherSignal.EngineName} for tighter stop ({otherSignal.StopDistancePts:F1}pts vs {bestSignal.StopDistancePts:F1}pts)";
            return otherSignal;
        }
        
        private bool IsAllowedByMacro(EngineSignalSide side, bool allowsLong, bool allowsShort)
        {
            return side switch
            {
                EngineSignalSide.Long => allowsLong,
                EngineSignalSide.Short => allowsShort,
                EngineSignalSide.None => false,
                _ => false
            };
        }
        
        private double CalculateAdjustedScore(EngineSignal signal, double macroConfidence, double spreadPts)
        {
            double baseScore = signal.Score;
            
            // Apply macro confidence multiplier
            double score = baseScore * macroConfidence;
            
            // Apply spread penalty if spread exceeds threshold
            if (spreadPts > _config.MaxEntrySpreadPts)
            {
                score *= _config.SpreadPenaltyMultiplier;
            }
            
            return score;
        }
        
        private string BuildNoSignalReason(
            EngineSignal? mr,
            EngineSignal? trend,
            bool macroAllowsLong,
            bool macroAllowsShort)
        {
            if (mr == null && trend == null)
            {
                return "No signals from any engine";
            }
            
            var reasons = new System.Collections.Generic.List<string>();
            
            if (mr != null && !IsAllowedByMacro(mr.Side, macroAllowsLong, macroAllowsShort))
            {
                reasons.Add($"MR {mr.Side} blocked by macro");
            }
            
            if (trend != null && !IsAllowedByMacro(trend.Side, macroAllowsLong, macroAllowsShort))
            {
                reasons.Add($"Trend {trend.Side} blocked by macro");
            }
            
            return reasons.Count > 0 
                ? string.Join("; ", reasons)
                : "All signals filtered by policy";
        }
    }
}

