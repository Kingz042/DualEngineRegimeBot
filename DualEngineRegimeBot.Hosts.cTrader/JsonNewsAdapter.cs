using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DualEngineRegimeBot.Hosts.cTrader
{
    /// <summary>
    /// News event entry from JSON configuration.
    /// </summary>
    public sealed class NewsEvent
    {
        /// <summary>Event start time (UTC).</summary>
        public DateTime From { get; set; } = DateTime.UtcNow;
        
        /// <summary>Event end time (UTC).</summary>
        public DateTime To { get; set; } = DateTime.UtcNow;
        
        /// <summary>News phase during this event.</summary>
        public string Phase { get; init; } = "Normal";
    }
    
    /// <summary>
    /// JSON-based news adapter that reads event schedule from file.
    /// Caches events and uses binary search for efficient lookups.
    /// </summary>
    public sealed class JsonNewsAdapter : INewsAdapter
    {
        private readonly List<(DateTime From, DateTime To, NewsPhase Phase)> _events;
        
        /// <summary>
        /// Initializes a new JsonNewsAdapter from file path.
        /// </summary>
        /// <param name="jsonFilePath">Path to news events JSON file.</param>
        public JsonNewsAdapter(string jsonFilePath)
        {
            _events = new List<(DateTime, DateTime, NewsPhase)>();
            
            if (string.IsNullOrEmpty(jsonFilePath) || !File.Exists(jsonFilePath))
            {
                // No file or empty - default to Normal for all times
                return;
            }
            
            try
            {
                string json = File.ReadAllText(jsonFilePath);
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var events = JsonSerializer.Deserialize<List<NewsEvent>>(json, options);
                
                if (events != null)
                {
                    foreach (var evt in events)
                    {
                        if (Enum.TryParse<NewsPhase>(evt.Phase, ignoreCase: true, out var phase))
                        {
                            _events.Add((evt.From, evt.To, phase));
                        }
                    }
                    
                    // Sort by start time for efficient lookup
                    _events.Sort((a, b) => a.From.CompareTo(b.From));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JsonNewsAdapter] Failed to load {jsonFilePath}: {ex.Message}");
                // Continue with empty event list (defaults to Normal)
            }
        }
        
        /// <summary>
        /// Initializes a new JsonNewsAdapter with explicit events (for testing).
        /// </summary>
        /// <param name="events">List of news events.</param>
        public JsonNewsAdapter(List<(DateTime From, DateTime To, NewsPhase Phase)> events)
        {
            _events = events ?? new List<(DateTime, DateTime, NewsPhase)>();
            _events.Sort((a, b) => a.From.CompareTo(b.From));
        }
        
        /// <inheritdoc/>
        public NewsPhase GetPhase(DateTime utcNow)
        {
            if (_events.Count == 0)
                return NewsPhase.Normal;
            
            // Find all overlapping events
            var activePhases = _events
                .Where(e => utcNow >= e.From && utcNow < e.To)
                .Select(e => e.Phase)
                .ToList();
            
            if (activePhases.Count == 0)
                return NewsPhase.Normal;
            
            // Return most restrictive phase: Block > UnwindOnly > Restricted > Normal
            if (activePhases.Contains(NewsPhase.Block))
                return NewsPhase.Block;
            if (activePhases.Contains(NewsPhase.UnwindOnly))
                return NewsPhase.UnwindOnly;
            if (activePhases.Contains(NewsPhase.Restricted))
                return NewsPhase.Restricted;
            
            return NewsPhase.Normal;
        }
        
        /// <summary>
        /// Gets the number of loaded events.
        /// </summary>
        public int EventCount => _events.Count;
    }
    
    /// <summary>
    /// No-op news adapter that always returns Normal phase.
    /// Used when news feed is disabled.
    /// </summary>
    public sealed class NoNewsAdapter : INewsAdapter
    {
        /// <inheritdoc/>
        public NewsPhase GetPhase(DateTime utcNow)
        {
            return NewsPhase.Normal;
        }
    }
}

