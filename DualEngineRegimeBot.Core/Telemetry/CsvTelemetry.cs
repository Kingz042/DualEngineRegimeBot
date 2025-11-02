using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DualEngineRegimeBot.Core.Telemetry
{
    /// <summary>
    /// CSV-based telemetry logger for trades and bar metrics.
    /// Buffers writes and flushes on bar close for performance.
    /// </summary>
    public class CsvTelemetry : ITelemetry
    {
        private readonly string _tradeLogPath;
        private readonly string _barLogPath;
        private readonly List<string> _tradeBuffer = new List<string>();
        private readonly List<string> _barBuffer = new List<string>();
        private readonly List<double> _spreadHistory = new List<double>();
        private readonly int _spreadWindowSize;
        
        public CsvTelemetry(string outputDirectory, int spreadWindowSize = 100)
        {
            Directory.CreateDirectory(outputDirectory);
            
            _spreadWindowSize = spreadWindowSize;
            _tradeLogPath = Path.Combine(outputDirectory, "trades.csv");
            _barLogPath = Path.Combine(outputDirectory, "bars.csv");
            
            // Write headers if new files
            if (!File.Exists(_tradeLogPath))
            {
                File.WriteAllText(_tradeLogPath,
                    "Time,Symbol,Engine,Side,Qty,EntryPx,SLPx,StopDist,EffRiskPct,NATR,VolMult,VDI,Kappa,TFBias,RegimeDir,RegimeVol,RegimeConf,ExitReason,PnL\n");
            }
            
            if (!File.Exists(_barLogPath))
            {
                File.WriteAllText(_barLogPath,
                    "Time,Symbol,NATR,VolMult,Theta,Mu,Kappa,TauHat,Spread,AtrRatio,NetUnits,HedgeUnits\n");
            }
        }
        
        public void LogTrade(
            DateTime time,
            string symbol,
            ExecutionEngine engine,
            TradeSide side,
            double qty,
            double entryPx,
            double slPx,
            double stopDist,
            double effRiskPct,
            double natr,
            double volMult,
            double vdi,
            double kappa,
            double tfBias,
            RegimeSnapshot regime,
            ExitReason exitReason,
            double pnl)
        {
            var line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4:F4},{5:F5},{6:F5},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12:F3},{13:F2},{14},{15},{16:F2},{17},{18:F2}",
                time, symbol, engine, side, qty, entryPx, slPx, stopDist, effRiskPct, natr, volMult,
                vdi, kappa, tfBias, regime.Direction, regime.VolState, regime.Confidence, exitReason, pnl);
            
            _tradeBuffer.Add(line);
        }
        
        public void LogBar(
            DateTime time,
            string symbol,
            double natr,
            double volMult,
            double theta,
            double mu,
            double kappa,
            int tauHat,
            double spread,
            double atrRatio,
            double netUnits,
            double hedgeUnits)
        {
            var line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss},{1},{2:F2},{3:F2},{4:F2},{5:F5},{6:F3},{7},{8:F5},{9:F2},{10:F2},{11:F2}",
                time, symbol, natr, volMult, theta, mu, kappa, tauHat, spread, atrRatio, netUnits, hedgeUnits);
            
            _barBuffer.Add(line);
        }
        
        public void Flush()
        {
            if (_tradeBuffer.Count > 0)
            {
                File.AppendAllLines(_tradeLogPath, _tradeBuffer);
                _tradeBuffer.Clear();
            }
            
            if (_barBuffer.Count > 0)
            {
                File.AppendAllLines(_barLogPath, _barBuffer);
                _barBuffer.Clear();
            }
        }
        
        public void UpdateSpread(double spread)
        {
            if (spread <= 0 || double.IsNaN(spread)) return;
            
            _spreadHistory.Add(spread);
            
            if (_spreadHistory.Count > _spreadWindowSize)
                _spreadHistory.RemoveAt(0);
        }
        
        public double GetMedianSpread()
        {
            if (_spreadHistory.Count == 0) return 0.0;
            
            var sorted = _spreadHistory.OrderBy(x => x).ToList();
            int mid = sorted.Count / 2;
            
            if (sorted.Count % 2 == 0)
                return (sorted[mid - 1] + sorted[mid]) / 2.0;
            else
                return sorted[mid];
        }
    }
}

