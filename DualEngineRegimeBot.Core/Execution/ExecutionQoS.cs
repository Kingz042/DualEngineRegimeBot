using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Execution
{
    /// <summary>
    /// Execution Quality-of-Service tracker with slippage modeling and telemetry.
    /// Logs signal/fill prices, latency, spread, and decomposed slippage components.
    /// </summary>
    public class ExecutionQoS
    {
        private readonly ExecutionQoSConfig _config;
        private readonly string _logFilePath;
        private readonly List<ExecutionRecord> _recentExecutions = new List<ExecutionRecord>();
        private readonly Queue<string> _logBuffer = new Queue<string>();
        
        public ExecutionQoS(ExecutionQoSConfig config, string outputDirectory)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            Directory.CreateDirectory(outputDirectory);
            _logFilePath = Path.Combine(outputDirectory, "execution_qos.csv");
            
            // Write header if new file
            if (!File.Exists(_logFilePath))
            {
                string header = "Timestamp,OrderId,Side,SignalPrice,FillPrice,SlippagePips,SlipBase,SlipLatency,SlipImpact," +
                               "LatencyMs,Spread,AtrM1,OrderSize,AvgDepth,WasRejected,WasPartial,RejectReason";
                File.WriteAllText(_logFilePath, header + "\n");
            }
        }
        
        /// <summary>
        /// Records an execution with QoS metrics.
        /// </summary>
        public void RecordExecution(ExecutionContext context)
        {
            if (!_config.Enabled)
                return;
            
            // Calculate slippage components
            var slippage = CalculateSlippage(context);
            
            var record = new ExecutionRecord
            {
                Timestamp = context.Timestamp,
                OrderId = context.OrderId,
                Side = context.Side,
                SignalPrice = context.SignalPrice,
                FillPrice = context.FillPrice,
                SlippagePips = slippage.TotalPips,
                SlipBase = slippage.BasePips,
                SlipLatency = slippage.LatencyPips,
                SlipImpact = slippage.ImpactPips,
                LatencyMs = context.LatencyMs,
                Spread = context.Spread,
                AtrM1 = context.AtrM1,
                OrderSize = context.OrderSize,
                AvgDepth = context.AvgDepth,
                WasRejected = context.WasRejected,
                WasPartial = context.WasPartial,
                RejectReason = context.RejectReason
            };
            
            lock (_recentExecutions)
            {
                _recentExecutions.Add(record);
                
                // Keep last 1000 records
                if (_recentExecutions.Count > 1000)
                    _recentExecutions.RemoveAt(0);
            }
            
            // Buffer for CSV write
            string csvLine = $"{record.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{record.OrderId},{record.Side}," +
                           $"{record.SignalPrice:F5},{record.FillPrice:F5},{record.SlippagePips:F3}," +
                           $"{record.SlipBase:F3},{record.SlipLatency:F3},{record.SlipImpact:F3}," +
                           $"{record.LatencyMs:F1},{record.Spread:F2},{record.AtrM1:F5}," +
                           $"{record.OrderSize:F2},{record.AvgDepth:F2}," +
                           $"{record.WasRejected},{record.WasPartial},{record.RejectReason ?? ""}";
            
            lock (_logBuffer)
            {
                _logBuffer.Enqueue(csvLine);
            }
        }
        
        /// <summary>
        /// Flushes buffered logs to CSV file.
        /// </summary>
        public void Flush()
        {
            lock (_logBuffer)
            {
                if (_logBuffer.Count == 0)
                    return;
                
                try
                {
                    File.AppendAllLines(_logFilePath, _logBuffer);
                    _logBuffer.Clear();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExecutionQoS] Flush failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Gets QoS statistics for monitoring.
        /// </summary>
        public QoSStats GetStats(TimeSpan window)
        {
            lock (_recentExecutions)
            {
                DateTime cutoff = DateTime.UtcNow - window;
                var recent = _recentExecutions.Where(r => r.Timestamp > cutoff).ToList();
                
                if (recent.Count == 0)
                    return new QoSStats();
                
                int rejectedCount = recent.Count(r => r.WasRejected);
                int partialCount = recent.Count(r => r.WasPartial);
                double rejectRate = (double)rejectedCount / recent.Count * 100.0;
                
                var latencies = recent.Where(r => !r.WasRejected).Select(r => r.LatencyMs).OrderBy(x => x).ToArray();
                var slippages = recent.Where(r => !r.WasRejected).Select(r => r.SlippagePips).ToArray();
                
                return new QoSStats
                {
                    TotalExecutions = recent.Count,
                    SuccessfulExecutions = recent.Count - rejectedCount,
                    RejectedExecutions = rejectedCount,
                    PartialExecutions = partialCount,
                    RejectRatePct = rejectRate,
                    AvgLatencyMs = latencies.Length > 0 ? latencies.Average() : 0,
                    P50LatencyMs = GetPercentile(latencies, 0.50),
                    P95LatencyMs = GetPercentile(latencies, 0.95),
                    P99LatencyMs = GetPercentile(latencies, 0.99),
                    AvgSlippagePips = slippages.Length > 0 ? slippages.Average() : 0,
                    P95SlippagePips = GetPercentile(slippages, 0.95),
                    AvgSlippageATRs = slippages.Length > 0 && recent[0].AtrM1 > 0 
                        ? slippages.Average() / recent[0].AtrM1 
                        : 0
                };
            }
        }
        
        /// <summary>
        /// Checks if QoS meets targets.
        /// </summary>
        public QoSAssessment AssessQoS()
        {
            var stats = GetStats(TimeSpan.FromHours(24));
            
            var assessment = new QoSAssessment
            {
                MeetsRejectTarget = stats.RejectRatePct <= _config.TargetRejectRatePct,
                MeetsSlippageTarget = stats.AvgSlippageATRs <= _config.TargetAvgSlippageMultiplier,
                RejectRatePct = stats.RejectRatePct,
                AvgSlippageATRs = stats.AvgSlippageATRs,
                Timestamp = DateTime.UtcNow
            };
            
            return assessment;
        }
        
        private SlippageBreakdown CalculateSlippage(ExecutionContext context)
        {
            double priceDiff = context.Side == TradeSide.Long
                ? context.FillPrice - context.SignalPrice
                : context.SignalPrice - context.FillPrice;
            
            double totalPips = priceDiff / context.PipSize;
            
            // Base slippage: 0.1× ATR_M1
            double basePips = _config.BaseSlippageMultiplier * context.AtrM1 / context.PipSize;
            
            // Latency slippage: (LatencyMs/1000) × |price_velocity| × ATR_M1
            double priceVelocity = context.PriceVelocity; // pips/second
            double latencyPips = (context.LatencyMs / 1000.0) * Math.Abs(priceVelocity) * context.AtrM1 / context.PipSize;
            
            // Impact slippage: (OrderSize/AvgDepth) × 0.5 × Spread
            double impactPips = context.AvgDepth > 0
                ? (context.OrderSize / context.AvgDepth) * _config.ImpactSlippageCoefficient * context.Spread
                : 0;
            
            return new SlippageBreakdown
            {
                TotalPips = totalPips,
                BasePips = basePips,
                LatencyPips = latencyPips * _config.LatencySlippageCoefficient,
                ImpactPips = impactPips
            };
        }
        
        private double GetPercentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 0)
                return 0;
            
            int index = (int)Math.Ceiling(sortedValues.Length * percentile) - 1;
            index = Math.Clamp(index, 0, sortedValues.Length - 1);
            
            return sortedValues[index];
        }
    }
    
    #region Supporting Types
    
    public class ExecutionContext
    {
        public DateTime Timestamp { get; set; }
        public string OrderId { get; set; }
        public TradeSide Side { get; set; }
        public double SignalPrice { get; set; }
        public double FillPrice { get; set; }
        public double LatencyMs { get; set; }
        public double Spread { get; set; }
        public double AtrM1 { get; set; }
        public double PipSize { get; set; }
        public double OrderSize { get; set; }
        public double AvgDepth { get; set; }
        public double PriceVelocity { get; set; } // pips/second
        public bool WasRejected { get; set; }
        public bool WasPartial { get; set; }
        public string RejectReason { get; set; }
    }
    
    public class ExecutionRecord
    {
        public DateTime Timestamp { get; set; }
        public string OrderId { get; set; }
        public TradeSide Side { get; set; }
        public double SignalPrice { get; set; }
        public double FillPrice { get; set; }
        public double SlippagePips { get; set; }
        public double SlipBase { get; set; }
        public double SlipLatency { get; set; }
        public double SlipImpact { get; set; }
        public double LatencyMs { get; set; }
        public double Spread { get; set; }
        public double AtrM1 { get; set; }
        public double OrderSize { get; set; }
        public double AvgDepth { get; set; }
        public bool WasRejected { get; set; }
        public bool WasPartial { get; set; }
        public string RejectReason { get; set; }
    }
    
    public class SlippageBreakdown
    {
        public double TotalPips { get; set; }
        public double BasePips { get; set; }
        public double LatencyPips { get; set; }
        public double ImpactPips { get; set; }
    }
    
    public class QoSStats
    {
        public int TotalExecutions { get; set; }
        public int SuccessfulExecutions { get; set; }
        public int RejectedExecutions { get; set; }
        public int PartialExecutions { get; set; }
        public double RejectRatePct { get; set; }
        public double AvgLatencyMs { get; set; }
        public double P50LatencyMs { get; set; }
        public double P95LatencyMs { get; set; }
        public double P99LatencyMs { get; set; }
        public double AvgSlippagePips { get; set; }
        public double P95SlippagePips { get; set; }
        public double AvgSlippageATRs { get; set; }
        
        public override string ToString()
        {
            return $"QoS: {SuccessfulExecutions}/{TotalExecutions} successful, " +
                   $"reject={RejectRatePct:F2}%, " +
                   $"latency p50/p95/p99={P50LatencyMs:F1}/{P95LatencyMs:F1}/{P99LatencyMs:F1}ms, " +
                   $"slippage avg/p95={AvgSlippagePips:F2}/{P95SlippagePips:F2} pips ({AvgSlippageATRs:F3}×ATR)";
        }
    }
    
    public class QoSAssessment
    {
        public bool MeetsRejectTarget { get; set; }
        public bool MeetsSlippageTarget { get; set; }
        public double RejectRatePct { get; set; }
        public double AvgSlippageATRs { get; set; }
        public DateTime Timestamp { get; set; }
        
        public bool PassesAll => MeetsRejectTarget && MeetsSlippageTarget;
        
        public override string ToString()
        {
            string status = PassesAll ? "PASS" : "FAIL";
            return $"QoS Assessment [{status}]: RejectRate={RejectRatePct:F2}% (target ≤2%), " +
                   $"Slippage={AvgSlippageATRs:F3}×ATR (target ≤0.25)";
        }
    }
    
    #endregion
}

