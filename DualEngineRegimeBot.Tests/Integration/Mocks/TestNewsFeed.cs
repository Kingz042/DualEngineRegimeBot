using System;
using System.Collections.Generic;
using System.Linq;

namespace DualEngineRegimeBot.Tests.Integration.Mocks
{
    /// <summary>
    /// News phase for testing.
    /// </summary>
    public enum NewsPhase
    {
        /// <summary>Normal operation - all actions allowed.</summary>
        Normal,
        
        /// <summary>Block phase - no entries, no hedges.</summary>
        Block,
        
        /// <summary>Unwind-only phase - only unwinds allowed.</summary>
        UnwindOnly,
        
        /// <summary>Restricted phase - hedges with higher threshold.</summary>
        Restricted
    }
    
    /// <summary>
    /// Test news feed with timeline-based phase control.
    /// </summary>
    public sealed class TestNewsFeed
    {
        private readonly List<(DateTime From, DateTime To, NewsPhase Phase)> _timeline;
        
        /// <summary>
        /// Initializes a new TestNewsFeed.
        /// </summary>
        public TestNewsFeed()
        {
            _timeline = new List<(DateTime, DateTime, NewsPhase)>();
        }
        
        /// <summary>
        /// Adds a news event period.
        /// </summary>
        public void AddEvent(DateTime from, DateTime to, NewsPhase phase)
        {
            _timeline.Add((from, to, phase));
        }
        
        /// <summary>
        /// Gets the current news phase at the specified time.
        /// If multiple events overlap, returns the most restrictive phase.
        /// </summary>
        public NewsPhase GetPhase(DateTime utcNow)
        {
            var activePhases = _timeline
                .Where(e => utcNow >= e.From && utcNow < e.To)
                .Select(e => e.Phase)
                .ToList();
            
            if (activePhases.Count == 0)
                return NewsPhase.Normal;
            
            // Return most restrictive: Block > UnwindOnly > Restricted > Normal
            if (activePhases.Contains(NewsPhase.Block))
                return NewsPhase.Block;
            if (activePhases.Contains(NewsPhase.UnwindOnly))
                return NewsPhase.UnwindOnly;
            if (activePhases.Contains(NewsPhase.Restricted))
                return NewsPhase.Restricted;
            
            return NewsPhase.Normal;
        }
        
        /// <summary>
        /// Checks if entries are allowed at the specified time.
        /// </summary>
        public bool AllowEntries(DateTime utcNow)
        {
            var phase = GetPhase(utcNow);
            return phase == NewsPhase.Normal;
        }
        
        /// <summary>
        /// Checks if hedges are allowed at the specified time.
        /// </summary>
        public bool AllowHedges(DateTime utcNow)
        {
            var phase = GetPhase(utcNow);
            return phase == NewsPhase.Normal || phase == NewsPhase.Restricted;
        }
        
        /// <summary>
        /// Checks if unwinds/exits are allowed at the specified time.
        /// </summary>
        public bool AllowUnwinds(DateTime utcNow)
        {
            // Always allow unwinds
            return true;
        }
    }
}

