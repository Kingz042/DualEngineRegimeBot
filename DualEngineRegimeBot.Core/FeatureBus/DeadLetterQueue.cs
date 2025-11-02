using System;
using System.Collections.Generic;
using System.Linq;

namespace DualEngineRegimeBot.Core.FeatureBus
{
    /// <summary>
    /// Dead Letter Queue for capturing failed feature processing events.
    /// Rate-limited to prevent log flooding and can trigger entry halts.
    /// </summary>
    public class DeadLetterQueue
    {
        private readonly int _rateLimitPerHour;
        private readonly Queue<DLQEntry> _queue = new Queue<DLQEntry>();
        private readonly Queue<DateTime> _errorTimestamps = new Queue<DateTime>();
        private int _totalErrorCount = 0;
        
        public DeadLetterQueue(int rateLimitPerHour)
        {
            _rateLimitPerHour = rateLimitPerHour;
        }
        
        /// <summary>
        /// Enqueues a failed event with exception details.
        /// </summary>
        public void Enqueue(string eventType, object payload, Exception exception)
        {
            var entry = new DLQEntry
            {
                Timestamp = DateTime.UtcNow,
                EventType = eventType,
                PayloadSummary = GetPayloadSummary(payload),
                ExceptionType = exception.GetType().Name,
                ExceptionMessage = exception.Message,
                StackTrace = exception.StackTrace
            };
            
            lock (_queue)
            {
                _queue.Enqueue(entry);
                _errorTimestamps.Enqueue(entry.Timestamp);
                _totalErrorCount++;
                
                // Trim queue to last 100 entries
                if (_queue.Count > 100)
                    _queue.Dequeue();
                
                // Trim timestamps older than 1 hour
                DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                while (_errorTimestamps.Count > 0 && _errorTimestamps.Peek() < cutoff)
                    _errorTimestamps.Dequeue();
            }
        }
        
        /// <summary>
        /// Checks if error rate exceeds limit (errors per hour).
        /// </summary>
        public bool IsRateLimitBreached()
        {
            lock (_queue)
            {
                return _errorTimestamps.Count > _rateLimitPerHour;
            }
        }
        
        /// <summary>
        /// Gets DLQ statistics.
        /// </summary>
        public DLQStats GetStats()
        {
            lock (_queue)
            {
                DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                int errorsLastHour = _errorTimestamps.Count;
                
                var recentEntries = _queue.Where(e => e.Timestamp > cutoff).ToList();
                var exceptionCounts = recentEntries
                    .GroupBy(e => e.ExceptionType)
                    .ToDictionary(g => g.Key, g => g.Count());
                
                return new DLQStats
                {
                    TotalErrors = _totalErrorCount,
                    ErrorsLastHour = errorsLastHour,
                    QueueSize = _queue.Count,
                    RateLimitBreached = errorsLastHour > _rateLimitPerHour,
                    TopExceptions = exceptionCounts,
                    OldestEntry = _queue.Count > 0 ? _queue.First().Timestamp : (DateTime?)null,
                    NewestEntry = _queue.Count > 0 ? _queue.Last().Timestamp : (DateTime?)null
                };
            }
        }
        
        /// <summary>
        /// Gets recent DLQ entries for investigation.
        /// </summary>
        public DLQEntry[] GetRecentEntries(int count = 10)
        {
            lock (_queue)
            {
                return _queue.TakeLast(count).ToArray();
            }
        }
        
        /// <summary>
        /// Clears the DLQ (after investigation/fix).
        /// </summary>
        public void Clear()
        {
            lock (_queue)
            {
                _queue.Clear();
                _errorTimestamps.Clear();
            }
        }
        
        private string GetPayloadSummary(object payload)
        {
            if (payload == null)
                return "null";
            
            try
            {
                var type = payload.GetType();
                return $"{type.Name}: {System.Text.Json.JsonSerializer.Serialize(payload).Substring(0, Math.Min(200, System.Text.Json.JsonSerializer.Serialize(payload).Length))}";
            }
            catch
            {
                return payload.GetType().Name;
            }
        }
    }
    
    /// <summary>
    /// Dead letter queue entry.
    /// </summary>
    public class DLQEntry
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
        public string PayloadSummary { get; set; }
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }
        public string StackTrace { get; set; }
        
        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {EventType}: {ExceptionType} - {ExceptionMessage}";
        }
    }
    
    /// <summary>
    /// DLQ statistics snapshot.
    /// </summary>
    public class DLQStats
    {
        public int TotalErrors { get; set; }
        public int ErrorsLastHour { get; set; }
        public int QueueSize { get; set; }
        public bool RateLimitBreached { get; set; }
        public Dictionary<string, int> TopExceptions { get; set; }
        public DateTime? OldestEntry { get; set; }
        public DateTime? NewestEntry { get; set; }
        
        public override string ToString()
        {
            string topExceptions = TopExceptions != null && TopExceptions.Count > 0
                ? string.Join(", ", TopExceptions.Select(kvp => $"{kvp.Key}:{kvp.Value}"))
                : "none";
            
            return $"DLQ: {ErrorsLastHour}/{QueueSize} errors (last hour/total), " +
                   $"RateLimitBreached={RateLimitBreached}, TopExceptions=[{topExceptions}]";
        }
    }
}

