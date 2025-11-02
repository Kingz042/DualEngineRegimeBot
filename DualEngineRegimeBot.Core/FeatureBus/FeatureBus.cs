using System;
using System.Collections.Generic;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.FeatureBus
{
    /// <summary>
    /// Non-blocking feature event bus for distributing market features to subscribers.
    /// Centralizes M1/M15 features (DirScore, VolRatio, Confidence, SMS, ATRs, spread, etc.)
    /// with version tracking and DLQ for error handling.
    /// </summary>
    public class FeatureBus
    {
        private readonly FeatureBusConfig _config;
        private readonly DeadLetterQueue _dlq;
        private readonly List<IFeatureSubscriber> _subscribers = new List<IFeatureSubscriber>();
        private string _eventVersion;
        
        public FeatureBus(FeatureBusConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dlq = new DeadLetterQueue(config.DLQRateLimitPerHour);
            _eventVersion = config.EventVersion;
        }
        
        /// <summary>
        /// Subscribes a feature consumer.
        /// </summary>
        public void Subscribe(IFeatureSubscriber subscriber)
        {
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber));
            
            lock (_subscribers)
            {
                if (!_subscribers.Contains(subscriber))
                    _subscribers.Add(subscriber);
            }
        }
        
        /// <summary>
        /// Unsubscribes a feature consumer.
        /// </summary>
        public void Unsubscribe(IFeatureSubscriber subscriber)
        {
            lock (_subscribers)
            {
                _subscribers.Remove(subscriber);
            }
        }
        
        /// <summary>
        /// Publishes M1 features non-blocking.
        /// </summary>
        public void PublishM1Features(M1Features features)
        {
            if (!_config.Enabled)
                return;
            
            var envelope = new FeatureEnvelope<M1Features>
            {
                Version = _eventVersion,
                Timestamp = features.Timestamp,
                EventType = "M1Features",
                Payload = features
            };
            
            PublishEnvelope(envelope);
        }
        
        /// <summary>
        /// Publishes M15 features non-blocking.
        /// </summary>
        public void PublishM15Features(M15Features features)
        {
            if (!_config.Enabled)
                return;
            
            var envelope = new FeatureEnvelope<M15Features>
            {
                Version = _eventVersion,
                Timestamp = features.Timestamp,
                EventType = "M15Features",
                Payload = features
            };
            
            PublishEnvelope(envelope);
        }
        
        /// <summary>
        /// Gets DLQ statistics.
        /// </summary>
        public DLQStats GetDLQStats() => _dlq.GetStats();
        
        /// <summary>
        /// Checks if DLQ rate limit breached (should halt entries).
        /// </summary>
        public bool IsDLQRateLimitBreached() => _dlq.IsRateLimitBreached();
        
        /// <summary>
        /// Clears DLQ (after investigation/fix).
        /// </summary>
        public void ClearDLQ() => _dlq.Clear();
        
        private void PublishEnvelope<T>(FeatureEnvelope<T> envelope)
        {
            IFeatureSubscriber[] subscribersCopy;
            
            lock (_subscribers)
            {
                subscribersCopy = _subscribers.ToArray();
            }
            
            // Non-blocking: fire and forget to subscribers
            foreach (var subscriber in subscribersCopy)
            {
                try
                {
                    // Async dispatch in production; synchronous here for simplicity
                    subscriber.OnFeatureEvent(envelope.EventType, envelope.Version, envelope.Payload);
                }
                catch (Exception ex)
                {
                    // Push to DLQ
                    if (_config.EnableDLQ)
                    {
                        _dlq.Enqueue(envelope.EventType, envelope.Payload, ex);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Feature subscriber interface.
    /// </summary>
    public interface IFeatureSubscriber
    {
        void OnFeatureEvent(string eventType, string version, object payload);
    }
    
    /// <summary>
    /// Versioned feature envelope.
    /// </summary>
    public class FeatureEnvelope<T>
    {
        public string Version { get; set; }
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
        public T Payload { get; set; }
    }
    
    /// <summary>
    /// M1 feature snapshot.
    /// </summary>
    public class M1Features
    {
        public DateTime Timestamp { get; set; }
        public double SMS { get; set; }
        public double AtrM1 { get; set; }
        public double Spread { get; set; }
        public double[] EMAs { get; set; } // Derived from bundle
        public double MidlinePrice { get; set; }
        public double DirScore { get; set; }
        public double VolRatio { get; set; }
    }
    
    /// <summary>
    /// M15 feature snapshot.
    /// </summary>
    public class M15Features
    {
        public DateTime Timestamp { get; set; }
        public RegimeDirection Direction { get; set; }
        public RegimeVolState VolState { get; set; }
        public double Confidence { get; set; }
        public double DirScore { get; set; }
        public double VolRatio { get; set; }
        public double AtrM15 { get; set; }
    }
}

