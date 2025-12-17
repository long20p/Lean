using System;
using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Indicators;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// Simple live paper trading algorithm that uses Tiingo data queue handler without framework components.
    /// Logs insights directly in OnData() method based on EMA crossover signals.
    /// Run it with the <c>live-paper-tiingo</c> environment to stream Tiingo IEX data.
    /// </summary>
    public class SimpleTiingoFeedAlgorithm : QCAlgorithm
    {
        private readonly Dictionary<Symbol, SymbolData> _symbolData = new Dictionary<Symbol, SymbolData>();
        private readonly TimeSpan _insightCooldown = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Initialize the algorithm
        /// </summary>
        public override void Initialize()
        {
            UniverseSettings.Resolution = Resolution.Minute;
            UniverseSettings.FillForward = true;
            UniverseSettings.ExtendedMarketHours = true;

            SetCash(100000);
            SetBenchmark("SPY");

            Log($"[Initialize] Starting SimpleTiingoFeedAlgorithm | LiveMode: {LiveMode} | Time: {UtcTime:yyyy-MM-dd HH:mm:ss} UTC");

            var symbols = new[] { "SPY" };
            foreach (var ticker in symbols)
            {
                var symbol = AddEquity(ticker, Resolution.Minute).Symbol;
                _symbolData[symbol] = new SymbolData(symbol);
            }

            Log("[Initialize] Initialization complete. Algorithm will run until manually stopped.");
        }

        /// <summary>
        /// Called when algorithm ends
        /// </summary>
        public override void OnEndOfAlgorithm()
        {
            Log($"[OnEndOfAlgorithm] Algorithm ending at {UtcTime:yyyy-MM-dd HH:mm:ss} UTC");
        }

        /// <summary>
        /// OnData event handler - receives data from Tiingo and logs insights
        /// </summary>
        public override void OnData(Slice slice)
        {
            Log($"[OnData] Called | Bars: {slice.Bars.Count} | Ticks: {slice.Ticks.Count} | Time: {UtcTime:yyyy-MM-dd HH:mm:ss}");

            foreach (var kvp in slice.Bars)
            {
                var symbol = kvp.Key;
                var bar = kvp.Value;

                if (!_symbolData.TryGetValue(symbol, out var data))
                {
                    continue;
                }

                Log($"[Tiingo Data] {symbol.Value} | Time: {bar.EndTime:yyyy-MM-dd HH:mm:ss} | " +
                    $"O: {bar.Open:F2} H: {bar.High:F2} L: {bar.Low:F2} C: {bar.Close:F2} | Volume: {bar.Volume:N0}");

                // Check if cooldown period has passed
                if (UtcTime - data.LastInsightTime < _insightCooldown)
                {
                    continue;
                }

                // Generate insight based on price comparison with previous bar
                if (data.LastPrice == 0)
                {
                    // First bar - just record the price
                    data.LastPrice = bar.Close;
                    continue;
                }

                string direction;
                if (bar.Close > data.LastPrice)
                {
                    direction = "UP";
                }
                else if (bar.Close < data.LastPrice)
                {
                    direction = "DOWN";
                }
                else
                {
                    data.LastPrice = bar.Close;
                    continue; // Flat - skip
                }

                var priceChange = Math.Abs(bar.Close - data.LastPrice);
                var percentChange = priceChange / data.LastPrice;
                var magnitude = Math.Min(0.05m, percentChange);
                var confidence = Math.Min(0.9m, Math.Max(0.1m, percentChange * 10m));

                Log($"[Insight] {symbol.Value} | Direction: {direction} | " +
                    $"LastPrice: {data.LastPrice:F2} | CurrentPrice: {bar.Close:F2} | " +
                    $"Change: {percentChange:P2} | Magnitude: {magnitude:P2} | Confidence: {confidence:P2}");

                data.LastPrice = bar.Close;
                data.LastInsightTime = UtcTime;
            }
        }

        /// <summary>
        /// Helper class to track price state per symbol
        /// </summary>
        private class SymbolData
        {
            public Symbol Symbol { get; }
            public decimal LastPrice { get; set; }
            public DateTime LastInsightTime { get; set; }

            public SymbolData(Symbol symbol)
            {
                Symbol = symbol;
                LastPrice = 0;
                LastInsightTime = DateTime.MinValue;
            }
        }
    }
}
