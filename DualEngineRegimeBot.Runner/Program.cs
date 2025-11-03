using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Hosts.cTrader;
using DualEngineRegimeBot.Hosts.cTrader.Adapters;

namespace DualEngineRegimeBot.Runner
{
    /// <summary>
    /// Console runner for batch walk-forward and Monte Carlo simulations.
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var options = ParseArguments(args);
                
                if (options == null || options.ShowHelp)
                {
                    PrintUsage();
                    return options?.ShowHelp == true ? 0 : 1;
                }
                
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.WriteLine("  DualEngineRegimeBot - Walk-Forward & Monte Carlo Runner");
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.WriteLine($"Config:     {options.ConfigPath}");
                Console.WriteLine($"Symbol:     {options.Symbol}");
                Console.WriteLine($"Period:     {options.FromDate:yyyy-MM-dd} to {options.ToDate:yyyy-MM-dd}");
                Console.WriteLine($"Mode:       {options.Mode}");
                
                if (options.Mode == "wf")
                {
                    Console.WriteLine($"WF Setup:   {options.WfInSample} months in-sample, {options.WfOutSample} months out-sample");
                }
                else if (options.Mode == "mc")
                {
                    Console.WriteLine($"MC Runs:    {options.McIterations}");
                }
                
                Console.WriteLine($"Output:     {options.OutputPath}");
                Console.WriteLine($"Seed:       {(options.RandomSeed ? "random" : options.Seed.ToString())}");
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                
                // Load configuration
                var preset = LoadPreset(options.ConfigPath);
                Console.WriteLine($"Loaded preset: {preset.VersionTag}");
                Console.WriteLine($"Config hash: {preset.ConfigHashSha256()}");
                
                // Run simulation
                var results = options.Mode == "wf"
                    ? RunWalkForward(options, preset)
                    : RunMonteCarlo(options, preset);
                
                // Write results
                WriteKpisCsv(results, options.OutputPath);
                
                Console.WriteLine($"\n✓ Completed {results.Count} run(s)");
                Console.WriteLine($"✓ KPIs written to: {options.OutputPath}");
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }
        
        static RunOptions? ParseArguments(string[] args)
        {
            var options = new RunOptions();
            
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        return options;
                    
                    case "--config":
                        options.ConfigPath = args[++i];
                        break;
                    
                    case "--symbol":
                        options.Symbol = args[++i];
                        break;
                    
                    case "--from":
                        options.FromDate = DateTime.Parse(args[++i]);
                        break;
                    
                    case "--to":
                        options.ToDate = DateTime.Parse(args[++i]);
                        break;
                    
                    case "--wf":
                        options.Mode = "wf";
                        var wfParts = args[++i].Split('x');
                        options.WfInSample = int.Parse(wfParts[0]);
                        options.WfOutSample = int.Parse(wfParts[1]);
                        break;
                    
                    case "--mc":
                        options.Mode = "mc";
                        options.McIterations = int.Parse(args[++i]);
                        break;
                    
                    case "--out":
                        options.OutputPath = args[++i];
                        break;
                    
                    case "--seed":
                        if (args[i + 1] == "random")
                        {
                            options.RandomSeed = true;
                            i++;
                        }
                        else
                        {
                            options.Seed = int.Parse(args[++i]);
                        }
                        break;
                    
                    case "--data":
                        options.DataPath = args[++i];
                        break;
                }
            }
            
            // Validate required options
            if (string.IsNullOrEmpty(options.ConfigPath) ||
                string.IsNullOrEmpty(options.Symbol) ||
                string.IsNullOrEmpty(options.Mode))
            {
                return null;
            }
            
            return options;
        }
        
        static void PrintUsage()
        {
            Console.WriteLine("Usage: DualEngineRegimeBot.Runner [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --config <path>      Path to config JSON (required)");
            Console.WriteLine("  --symbol <symbol>    Symbol to trade (required)");
            Console.WriteLine("  --from <date>        Start date (yyyy-MM-dd)");
            Console.WriteLine("  --to <date>          End date (yyyy-MM-dd)");
            Console.WriteLine("  --wf <NxM>           Walk-forward: N months in-sample, M months out-sample");
            Console.WriteLine("  --mc <iterations>    Monte Carlo: number of runs");
            Console.WriteLine("  --out <path>         Output CSV path (default: kpis.csv)");
            Console.WriteLine("  --seed <number>      Random seed (or 'random')");
            Console.WriteLine("  --data <path>        Historical tick data CSV path");
            Console.WriteLine("  --help, -h           Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Walk-forward (4 months in-sample, 3 months out):");
            Console.WriteLine("    --config preset.json --symbol XAUUSD --from 2024-01-01 --to 2025-10-31 --wf 4x3 --out kpis.csv");
            Console.WriteLine();
            Console.WriteLine("  Monte Carlo (1000 iterations):");
            Console.WriteLine("    --config preset.json --symbol XAUUSD --mc 1000 --seed random --out mc_kpis.csv");
        }
        
        static FtmoPreset LoadPreset(string path)
        {
            // For now, return default preset
            // In production, load from JSON file
            if (!File.Exists(path))
            {
                Console.WriteLine($"Warning: Config file not found, using default preset");
                return FtmoPreset.CreateDefault();
            }
            
            // TODO: Deserialize from JSON
            return FtmoPreset.CreateDefault();
        }
        
        static List<KpiResult> RunWalkForward(RunOptions options, FtmoPreset preset)
        {
            var results = new List<KpiResult>();
            var random = options.RandomSeed ? new Random() : new Random(options.Seed);
            
            DateTime currentStart = options.FromDate;
            int windowNumber = 1;
            
            while (currentStart < options.ToDate)
            {
                DateTime inSampleEnd = currentStart.AddMonths(options.WfInSample);
                DateTime outSampleEnd = inSampleEnd.AddMonths(options.WfOutSample);
                
                if (outSampleEnd > options.ToDate)
                    break;
                
                Console.WriteLine($"\nWindow {windowNumber}: In-sample {currentStart:yyyy-MM-dd} to {inSampleEnd:yyyy-MM-dd}, " +
                                  $"Out-sample {inSampleEnd:yyyy-MM-dd} to {outSampleEnd:yyyy-MM-dd}");
                
                // Simulate out-sample period
                var result = SimulatePeriod(
                    options.Symbol,
                    inSampleEnd,
                    outSampleEnd,
                    preset,
                    random,
                    $"WF_{windowNumber}");
                
                results.Add(result);
                
                // Slide window forward
                currentStart = currentStart.AddMonths(options.WfInSample);
                windowNumber++;
            }
            
            return results;
        }
        
        static List<KpiResult> RunMonteCarlo(RunOptions options, FtmoPreset preset)
        {
            var results = new List<KpiResult>();
            var random = options.RandomSeed ? new Random() : new Random(options.Seed);
            
            for (int i = 0; i < options.McIterations; i++)
            {
                if (i % 100 == 0)
                    Console.WriteLine($"MC iteration {i + 1}/{options.McIterations}...");
                
                var result = SimulatePeriod(
                    options.Symbol,
                    options.FromDate,
                    options.ToDate,
                    preset,
                    random,
                    $"MC_{i + 1}");
                
                results.Add(result);
            }
            
            return results;
        }
        
        static KpiResult SimulatePeriod(
            string symbol,
            DateTime fromDate,
            DateTime toDate,
            FtmoPreset preset,
            Random random,
            string runId)
        {
            // Simplified simulation - generates random trade sequence
            // In production, this would replay historical ticks through the bot
            
            int numTrades = random.Next(20, 100);
            double initialBalance = 100000.0;
            double balance = initialBalance;
            double peakBalance = balance;
            int wins = 0;
            int losses = 0;
            double totalProfit = 0.0;
            double totalLoss = 0.0;
            
            for (int i = 0; i < numTrades; i++)
            {
                // Random trade outcome
                bool isWin = random.NextDouble() > 0.45; // 55% win rate
                double pnl = isWin
                    ? random.NextDouble() * 1000 + 200  // Win: $200-$1200
                    : -(random.NextDouble() * 800 + 100); // Loss: $100-$900
                
                balance += pnl;
                
                if (balance > peakBalance)
                    peakBalance = balance;
                
                if (pnl > 0)
                {
                    wins++;
                    totalProfit += pnl;
                }
                else
                {
                    losses++;
                    totalLoss += Math.Abs(pnl);
                }
            }
            
            double netProfit = balance - initialBalance;
            double maxDrawdown = ((peakBalance - balance) / peakBalance) * 100.0;
            double profitFactor = totalLoss > 0 ? totalProfit / totalLoss : 0.0;
            double winRate = numTrades > 0 ? (wins / (double)numTrades) * 100.0 : 0.0;
            double avgWin = wins > 0 ? totalProfit / wins : 0.0;
            double avgLoss = losses > 0 ? totalLoss / losses : 0.0;
            double expectancy = numTrades > 0 ? netProfit / numTrades : 0.0;
            
            int tradingDays = (toDate - fromDate).Days;
            double years = tradingDays / 365.25;
            double cagr = years > 0 ? (Math.Pow(balance / initialBalance, 1.0 / years) - 1.0) * 100.0 : 0.0;
            
            double mar = maxDrawdown > 0 ? cagr / maxDrawdown : 0.0;
            
            return new KpiResult
            {
                RunId = runId,
                Symbol = symbol,
                FromDate = fromDate,
                ToDate = toDate,
                NumTrades = numTrades,
                WinRate = winRate,
                ProfitFactor = profitFactor,
                NetProfit = netProfit,
                MaxDrawdown = maxDrawdown,
                CAGR = cagr,
                Expectancy = expectancy,
                AvgWin = avgWin,
                AvgLoss = avgLoss,
                MAR = mar,
                ConfigHash = preset.ConfigHashSha256()
            };
        }
        
        static void WriteKpisCsv(List<KpiResult> results, string path)
        {
            var sb = new StringBuilder();
            
            // Header
            sb.AppendLine("RunId,Symbol,FromDate,ToDate,NumTrades,WinRate,ProfitFactor,NetProfit," +
                          "MaxDrawdown,CAGR,Expectancy,AvgWin,AvgLoss,MAR,ConfigHash");
            
            // Rows
            foreach (var result in results)
            {
                sb.AppendLine($"{result.RunId},{result.Symbol}," +
                              $"{result.FromDate:yyyy-MM-dd},{result.ToDate:yyyy-MM-dd}," +
                              $"{result.NumTrades},{result.WinRate:F2},{result.ProfitFactor:F2}," +
                              $"{result.NetProfit:F2},{result.MaxDrawdown:F2},{result.CAGR:F2}," +
                              $"{result.Expectancy:F2},{result.AvgWin:F2},{result.AvgLoss:F2}," +
                              $"{result.MAR:F2},{result.ConfigHash}");
            }
            
            File.WriteAllText(path, sb.ToString());
        }
    }
    
    class RunOptions
    {
        public bool ShowHelp { get; set; }
        public string ConfigPath { get; set; } = "";
        public string Symbol { get; set; } = "";
        public DateTime FromDate { get; set; } = new DateTime(2024, 1, 1);
        public DateTime ToDate { get; set; } = new DateTime(2025, 10, 31);
        public string Mode { get; set; } = "";
        public int WfInSample { get; set; } = 4;
        public int WfOutSample { get; set; } = 3;
        public int McIterations { get; set; } = 1000;
        public string OutputPath { get; set; } = "kpis.csv";
        public int Seed { get; set; } = 42;
        public bool RandomSeed { get; set; } = false;
        public string DataPath { get; set; } = "";
    }
    
    class KpiResult
    {
        public string RunId { get; set; } = "";
        public string Symbol { get; set; } = "";
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int NumTrades { get; set; }
        public double WinRate { get; set; }
        public double ProfitFactor { get; set; }
        public double NetProfit { get; set; }
        public double MaxDrawdown { get; set; }
        public double CAGR { get; set; }
        public double Expectancy { get; set; }
        public double AvgWin { get; set; }
        public double AvgLoss { get; set; }
        public double MAR { get; set; }
        public string ConfigHash { get; set; } = "";
    }
}

