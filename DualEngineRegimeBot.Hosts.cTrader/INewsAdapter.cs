using System;

namespace DualEngineRegimeBot.Hosts.cTrader
{
    /// <summary>
    /// News phase enumeration for trade restrictions.
    /// </summary>
    public enum NewsPhase
    {
        /// <summary>Normal operation - all actions allowed.</summary>
        Normal,
        
        /// <summary>Block phase (0-2 min) - no entries, no hedges.</summary>
        Block,
        
        /// <summary>Unwind-only phase (3-5 min) - only unwinds allowed.</summary>
        UnwindOnly,
        
        /// <summary>Restricted phase (6-15 min) - hedges with 2× threshold.</summary>
        Restricted
    }
    
    /// <summary>
    /// Interface for news event feed adapter.
    /// Provides real-time news phase information to enforce trading restrictions.
    /// </summary>
    public interface INewsAdapter
    {
        /// <summary>
        /// Gets the current news phase at the specified time.
        /// </summary>
        /// <param name="utcNow">Current UTC time.</param>
        /// <returns>Current news phase.</returns>
        NewsPhase GetPhase(DateTime utcNow);
    }
}

