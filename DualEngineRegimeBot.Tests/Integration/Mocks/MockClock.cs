using System;

namespace DualEngineRegimeBot.Tests.Integration.Mocks
{
    /// <summary>
    /// Mock clock for deterministic time control in tests.
    /// </summary>
    public sealed class MockClock
    {
        private DateTime _utcNow;
        
        /// <summary>
        /// Initializes a new MockClock with the current UTC time.
        /// </summary>
        public MockClock()
        {
            _utcNow = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Initializes a new MockClock with a specific time.
        /// </summary>
        public MockClock(DateTime initialUtcTime)
        {
            _utcNow = initialUtcTime;
        }
        
        /// <summary>
        /// Gets or sets the current UTC time.
        /// </summary>
        public DateTime UtcNow
        {
            get => _utcNow;
            set => _utcNow = value;
        }
        
        /// <summary>
        /// Advances time by the specified duration.
        /// </summary>
        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
        
        /// <summary>
        /// Advances time by the specified number of minutes.
        /// </summary>
        public void AdvanceMinutes(double minutes)
        {
            Advance(TimeSpan.FromMinutes(minutes));
        }
        
        /// <summary>
        /// Advances time by the specified number of hours.
        /// </summary>
        public void AdvanceHours(double hours)
        {
            Advance(TimeSpan.FromHours(hours));
        }
        
        /// <summary>
        /// Advances time by the specified number of days.
        /// </summary>
        public void AdvanceDays(double days)
        {
            Advance(TimeSpan.FromDays(days));
        }
    }
}

