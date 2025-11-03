// =====================================================================
// DUAL-ENGINE REGIME BOT - SINGLE FILE VERSION FOR cTRADER
// =====================================================================
// This is a simplified, single-file version for direct use in cTrader.
// For the full modular solution, see the DualEngineRegimeBot.Core project.
//
// USAGE:
// 1. Open cTrader → Automate → New cBot
// 2. Copy this entire file into the editor
// 3. Configure parameters (Symbol: XAUUSD or BTCUSD, Timeframe: M1)
// 4. Run backtest or live
//
// OUTPUT:
// - Logs: Documents\DualEngineBot_Logs\trades.csv & bars.csv
// - State: Documents\DualEngineBot_Logs\state_<symbol>_<timeframe>.json
//
// =====================================================================

/*
 * NOTE FOR IMPLEMENTATION:
 * 
 * To create the full single-file cBot, you would:
 * 
 * 1. Copy ALL classes from DualEngineRegimeBot.Core into this file:
 *    - CoreModels.cs (enums, MarketContext, OrderIntent, etc.)
 *    - ServiceInterfaces.cs (all interfaces)
 *    - Config/*.cs (all config classes)
 *    - Indicators/*.cs (KalmanMean, AtrEma, KappaEstimator)
 *    - Sizing/InverseVolSizer.cs
 *    - Risk/RiskService.cs
 *    - Telemetry/CsvTelemetry.cs
 *    - Macro/RegimeModule.cs
 *    - Engines/*/*.cs (TrendFollowerService, SareService)
 *    - Hedging/TailHedgeService.cs
 *    - State/JsonStateStore.cs
 * 
 * 2. Add cTrader bot class (see structure below)
 * 
 * 3. Wire services in OnStart()
 * 
 * 4. Implement OnBar() and OnTick() with deterministic execution order
 * 
 * The full single-file version would be ~3000-4000 lines.
 * For maintainability, use the modular Core project instead.
 */

using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace DualEngineRegimeBot
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FileSystem)]
    public class DualEngineBot : Robot
    {
        // ========== PARAMETERS ==========
        [Parameter("Symbol Preset", DefaultValue = "XAUUSD")]
        public string SymbolPresetName { get; set; }
        
        [Parameter("Output Directory", DefaultValue = "DualEngineBot_Logs")]
        public string OutputDirectory { get; set; }
        
        // ========== IMPLEMENTATION NOTES ==========
        /*
         * TO COMPLETE THIS BOT:
         * 
         * 1. Add all Core classes above this Robot class
         * 
         * 2. Declare service fields:
         *    private IRegimeService _regimeService;
         *    private ITrendFollowerService _tfService;
         *    private ISareService _sareService;
         *    private ISizerService _sizerService;
         *    private ITailHedgeService _hedgeService;
         *    private IRiskService _riskService;
         *    private ITelemetry _telemetry;
         *    private IStateStore _stateStore;
         * 
         * 3. In OnStart():
         *    - Load symbol preset
         *    - Initialize all services
         *    - Initialize cTrader indicators (EMA, ATR)
         *    - Load persisted state
         * 
         * 4. In OnBar():
         *    - Update indicators
         *    - Refresh regime on M15 boundary
         *    - Update all services
         *    - Check exits first
         *    - Check entries (TF, SARE)
         *    - Log bar metrics
         *    - Persist state
         *    - Flush telemetry
         * 
         * 5. In OnTick():
         *    - Probe tail hedge
         *    - Update spread tracker
         * 
         * See README.md for detailed execution flow.
         */
        
        protected override void OnStart()
        {
            Print("[DualEngineBot] Single-file template - requires full implementation");
            Print("[DualEngineBot] See README.md or use modular Core project");
            Stop();
        }
        
        protected override void OnBar()
        {
            // Implement deterministic execution order here
        }
        
        protected override void OnTick()
        {
            // Implement tail-hedge probe here
        }
        
        protected override void OnStop()
        {
            Print("[DualEngineBot] Stopped");
        }
    }
}

// =====================================================================
// END OF SINGLE FILE BOT TEMPLATE
// =====================================================================
// 
// NEXT STEPS:
// 
// 1. For a working bot, copy all classes from:
//    C:\Users\kelechi\Documents\DualEngineRegimeBot\DualEngineRegimeBot.Core\
//    
// 2. Merge them into this file (remove namespace conflicts)
// 
// 3. Complete the OnStart/OnBar/OnTick implementation
// 
// 4. Test in cTrader Automate
// 
// OR
// 
// Use the modular solution and reference Core DLL in cTrader
// (requires cTrader to support external DLL references)
// 
// =====================================================================

