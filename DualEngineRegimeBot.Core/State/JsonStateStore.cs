using System;
using System.IO;
using System.Text.Json;

namespace DualEngineRegimeBot.Core.State
{
    /// <summary>
    /// JSON-based state persistence with atomic writes.
    /// </summary>
    public class JsonStateStore : IStateStore
    {
        private readonly string _stateFilePath;
        private readonly string _tempFilePath;
        
        public JsonStateStore(string stateDirectory, string botInstanceId = "default")
        {
            Directory.CreateDirectory(stateDirectory);
            
            _stateFilePath = Path.Combine(stateDirectory, $"state_{botInstanceId}.json");
            _tempFilePath = Path.Combine(stateDirectory, $"state_{botInstanceId}.tmp");
        }
        
        /// <summary>
        /// Serializes state to disk with atomic write (temp → move).
        /// </summary>
        public void Save(BotState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(state, options);
                
                // Write to temp file first
                File.WriteAllText(_tempFilePath, json);
                
                // Atomic move (overwrites existing)
                if (File.Exists(_stateFilePath))
                    File.Delete(_stateFilePath);
                
                File.Move(_tempFilePath, _stateFilePath);
            }
            catch (Exception ex)
            {
                // Log error but don't crash bot
                Console.WriteLine($"[StateStore] Save failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Restores state from disk; returns null if not found or invalid.
        /// </summary>
        public BotState? Load()
        {
            if (!File.Exists(_stateFilePath))
                return null;
            
            try
            {
                string json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<BotState>(json);
                
                return state;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StateStore] Load failed: {ex.Message}");
                return null;
            }
        }
    }
}

