using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DualEngineRegimeBot.Core.Hedging;
using DualEngineRegimeBot.Core.NewsGuard;

namespace DualEngineRegimeBot.Core.State
{
    /// <summary>
    /// Comprehensive state persistence for crash recovery.
    /// Persists all critical bot state including positions, hedges, regime, SMS, ATR, drawdown, etc.
    /// </summary>
    public class StatePersistence
    {
        private readonly string _stateFilePath;
        private readonly string _tempFilePath;
        private readonly string _backupFilePath;
        
        public StatePersistence(string stateDirectory, string botInstanceId)
        {
            Directory.CreateDirectory(stateDirectory);
            
            _stateFilePath = Path.Combine(stateDirectory, $"state_{botInstanceId}.json");
            _tempFilePath = Path.Combine(stateDirectory, $"state_{botInstanceId}.tmp");
            _backupFilePath = Path.Combine(stateDirectory, $"state_{botInstanceId}.bak");
        }
        
        /// <summary>
        /// Saves complete bot state atomically using AtomicFile.
        /// </summary>
        public bool Save(ComprehensiveBotState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            
            try
            {
                // Serialize to JSON
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                string json = JsonSerializer.Serialize(state, options);
                
                // Use AtomicFile for crash-safe write
                AtomicFile.WriteAtomicText(_stateFilePath, json);
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StatePersistence] Save failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Loads bot state with fallback to backup if corrupted.
        /// </summary>
        public ComprehensiveBotState Load()
        {
            // Try primary state file
            var state = TryLoadFromFile(_stateFilePath);
            if (state != null)
                return state;
            
            // Try backup
            Console.WriteLine("[StatePersistence] Primary state corrupted, trying backup...");
            state = TryLoadFromFile(_backupFilePath);
            if (state != null)
            {
                Console.WriteLine("[StatePersistence] Backup loaded successfully");
                return state;
            }
            
            // Return empty state
            Console.WriteLine("[StatePersistence] No valid state found, initializing empty state");
            return null;
        }
        
        private ComprehensiveBotState TryLoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;
            
            try
            {
                string json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                return JsonSerializer.Deserialize<ComprehensiveBotState>(json, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StatePersistence] Load from {filePath} failed: {ex.Message}");
                return null;
            }
        }
    }
    
    /// <summary>
    /// Comprehensive bot state for persistence.
    /// </summary>
    public class ComprehensiveBotState
    {
        // Metadata
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public string ConfigHash { get; set; }
        public string BotVersion { get; set; }
        
        // Positions
        public List<PersistedPosition> OpenPositions { get; set; } = new List<PersistedPosition>();
        public PersistedHedge ActiveHedge { get; set; }
        
        // Regime state
        public PersistedRegime CurrentRegime { get; set; }
        public DateTime RegimeChangeTime { get; set; }
        public int RegimeBarsAge { get; set; }
        
        // SMS state
        public Queue<double> SMSHistory { get; set; } = new Queue<double>();
        public double[] ATRFloors { get; set; }
        
        // Drawdown state
        public double AllTimeHigh { get; set; }
        public double RollingPeakEquity { get; set; }
        public Queue<EquitySnapshot> EquityHistory { get; set; } = new Queue<EquitySnapshot>();
        public bool SurvivalModeActive { get; set; }
        
        // NewsGuard state
        public NewsGuardPhase NewsGuardPhase { get; set; }
        public DateTime NewsSpikeDetectedAt { get; set; }
        public double NewsSpikeStrength { get; set; }
        
        // Hedge controller state
        public HedgeState HedgeState { get; set; }
        public DateTime LastHedgeTime { get; set; }
        
        // Execution QoS
        public List<QoSMetric> RecentQoSMetrics { get; set; } = new List<QoSMetric>();
        
        // DLQ
        public int DLQErrorCount { get; set; }
        public DateTime DLQLastError { get; set; }
    }
    
    public class PersistedPosition
    {
        public string Id { get; set; }
        public TradeSide Side { get; set; }
        public double Volume { get; set; }
        public double EntryPrice { get; set; }
        public DateTime EntryTime { get; set; }
        public double CurrentPrice { get; set; }
        public double UnrealizedPnL { get; set; }
        public int BarsOpen { get; set; }
        public double CurrentTrailDistance { get; set; }
        public string Label { get; set; }
    }
    
    public class PersistedHedge
    {
        public TradeSide Side { get; set; }
        public double Volume { get; set; }
        public double OpenPrice { get; set; }
        public DateTime OpenTime { get; set; }
        public int BarsHeld { get; set; }
        public double MinutesHeld { get; set; }
    }
    
    public class PersistedRegime
    {
        public RegimeDirection Direction { get; set; }
        public RegimeVolState VolState { get; set; }
        public double Confidence { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    public class EquitySnapshot
    {
        public DateTime Time { get; set; }
        public double Equity { get; set; }
    }
    
    public class QoSMetric
    {
        public DateTime Timestamp { get; set; }
        public double LatencyMs { get; set; }
        public double SlippagePips { get; set; }
        public bool WasRejected { get; set; }
    }
}

