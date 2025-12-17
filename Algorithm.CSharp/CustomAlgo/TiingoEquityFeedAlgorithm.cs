using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Interfaces;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// Live paper trading algorithm that uses Tiingo data queue handler. It only emits insights so you can
    /// validate Live Trading ResultHandler pipelines without wiring execution, portfolio construction, or risk models.
    /// Run it with the <c>live-paper-tiingo</c> environment to stream Tiingo IEX data.
    /// </summary>
    public class TiingoEquityFeedAlgorithm : QCAlgorithm
    {
        private static readonly string[] DefaultTickers = new[] { "SPY", "META", "MSFT", "NVDA", "GOOG" };
        private static readonly char[] SymbolSeparators = new[] { ',', ';', '|', ' ' };
        private const string SymbolsParameterName = "tiingo-feed-symbols";
        private const string InsightPeriodParameterName = "tiingo-feed-insight-hours";
        private const string FastPeriodParameterName = "tiingo-feed-fast-period";
        private const string SlowPeriodParameterName = "tiingo-feed-slow-period";

        /// <summary>
        /// Initialise the algorithm. In live-paper mode Lean sets the start date and cash automatically, but we
        /// keep the declarations for easier local testing.
        /// </summary>
        public override void Initialize()
        {
            UniverseSettings.Resolution = Resolution.Hour;
            UniverseSettings.FillForward = true;
            UniverseSettings.ExtendedMarketHours = false;

            SetCash(100000);
            SetBrokerageModel(new DefaultBrokerageModel(AccountType.Margin));
            SetWarmUp(TimeSpan.FromMinutes(21));
            //SetBenchmark("SPY");

            if (!LiveMode)
            {
                Log("Switch the configuration environment to 'live-paper-tiingo' to experience the Tiingo data feed.");
            }

            var symbols = ParseSymbolsParameter().ToList();
            SetUniverseSelection(new ManualUniverseSelectionModel(symbols));

            var insightPeriodHours = GetPositiveIntParameter(InsightPeriodParameterName, 1);
            var fastPeriod = GetPositiveIntParameter(FastPeriodParameterName, 8);
            var slowPeriod = Math.Max(fastPeriod + 2, GetPositiveIntParameter(SlowPeriodParameterName, 21));
            var predictionInterval = TimeSpan.FromHours(insightPeriodHours);

            // Only set an Alpha model so Lean emits insights but does not place trades
            SetAlpha(new TiingoMomentumAlphaModel(predictionInterval, fastPeriod, slowPeriod));

            InsightsGenerated += OnInsightsGenerated;
        }

        private IEnumerable<Symbol> ParseSymbolsParameter()
        {
            var raw = GetParameter(SymbolsParameterName);
            var tickers = string.IsNullOrWhiteSpace(raw)
                ? DefaultTickers
                : raw.Split(SymbolSeparators, StringSplitOptions.RemoveEmptyEntries)
                     .Select(ticker => ticker.Trim().ToUpperInvariant())
                     .Where(ticker => !string.IsNullOrEmpty(ticker));

            foreach (var ticker in tickers)
            {
                yield return QuantConnect.Symbol.Create(ticker, SecurityType.Equity, Market.USA);
            }
        }

        private int GetPositiveIntParameter(string name, int fallback)
        {
            var raw = GetParameter(name);
            return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
        }

        private void OnInsightsGenerated(object sender, GeneratedInsightsCollection collection)
        {
            foreach (var insight in collection.Insights)
            {
                Log($"[{UtcTime:HH:mm:ss}] {insight.Symbol.Value} -> {insight.Direction} | Magnitude: {insight.Magnitude:P2} | Confidence: {insight.Confidence:P2}");
            }
        }

        public override void OnSecuritiesChanged(SecurityChanges changes)
        {
            foreach (var added in changes.AddedSecurities)
            {
                Log($"Subscribed to {added.Symbol}");
            }

            foreach (var removed in changes.RemovedSecurities)
            {
                Log($"Unsubscribed from {removed.Symbol}");
            }
        }

        /// <summary>
        /// Alpha model that runs a simple fast/slow EMA comparison on Tiingo data and turns the delta
        /// into recurring up/down insights.
        /// </summary>
        private sealed class TiingoMomentumAlphaModel : AlphaModel
        {
            private readonly TimeSpan _predictionInterval;
            private readonly int _fastPeriod;
            private readonly int _slowPeriod;
            private readonly Dictionary<Symbol, SymbolState> _symbolStates = new();

            public TiingoMomentumAlphaModel(TimeSpan predictionInterval, int fastPeriod, int slowPeriod)
            {
                _predictionInterval = predictionInterval;
                _fastPeriod = fastPeriod;
                _slowPeriod = slowPeriod;
                Name = $"{nameof(TiingoMomentumAlphaModel)}({_fastPeriod},{_slowPeriod})";
            }

            public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice slice)
            {
                var insights = new List<Insight>();

                foreach (var state in _symbolStates.Values)
                {
                    if (!slice.TryGetValue(state.Symbol, out var dataPoint))
                    {
                        continue;
                    }

                    algorithm.Log($"[Tiingo Data] {state.Symbol.Value} | Time: {dataPoint.EndTime:yyyy-MM-dd HH:mm:ss} | Price: {dataPoint.Price:F2}");

                    state.Update(dataPoint.EndTime, dataPoint.Price);

                    if (!state.IsReady || !state.CanEmit(algorithm.UtcTime))
                    {
                        continue;
                    }

                    var direction = state.GetDirection();
                    if (direction == InsightDirection.Flat)
                    {
                        continue;
                    }

                    var magnitude = (double)state.GetMagnitude();
                    var confidence = (double)state.GetConfidence();

                    state.MarkEmitted(algorithm.UtcTime);
                    insights.Add(Insight.Price(state.Symbol, _predictionInterval, direction, magnitude, confidence));
                }

                return insights;
            }

            public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
            {
                foreach (var added in changes.AddedSecurities)
                {
                    if (_symbolStates.ContainsKey(added.Symbol))
                    {
                        continue;
                    }

                    _symbolStates[added.Symbol] = new SymbolState(added.Symbol, _fastPeriod, _slowPeriod, _predictionInterval);
                }

                foreach (var removed in changes.RemovedSecurities)
                {
                    _symbolStates.Remove(removed.Symbol);
                }
            }

            private sealed class SymbolState
            {
                private readonly ExponentialMovingAverage _fast;
                private readonly ExponentialMovingAverage _slow;
                private readonly TimeSpan _cooldown;
                private DateTime _lastInsightUtc = DateTime.MinValue;

                public Symbol Symbol { get; }

                public SymbolState(Symbol symbol, int fastPeriod, int slowPeriod, TimeSpan horizon)
                {
                    Symbol = symbol;
                    _fast = new ExponentialMovingAverage(fastPeriod);
                    _slow = new ExponentialMovingAverage(slowPeriod);
                    // For hourly data, throttle insights to once per hour minimum
                    var minSeconds = Math.Max(3600, (int)Math.Round(horizon.TotalSeconds / 2.0));
                    _cooldown = TimeSpan.FromSeconds(minSeconds);
                }

                public bool IsReady => _fast.IsReady && _slow.IsReady;

                public void Update(DateTime time, decimal value)
                {
                    var point = new IndicatorDataPoint(time, value);
                    _fast.Update(point);
                    _slow.Update(point);
                }

                public InsightDirection GetDirection()
                {
                    if (_fast.Current.Value > _slow.Current.Value)
                    {
                        return InsightDirection.Up;
                    }

                    if (_fast.Current.Value < _slow.Current.Value)
                    {
                        return InsightDirection.Down;
                    }

                    return InsightDirection.Flat;
                }

                public decimal GetMagnitude()
                {
                    if (!IsReady || _slow.Current.Value == 0)
                    {
                        return 0.001m;
                    }

                    var delta = Math.Abs(_fast.Current.Value - _slow.Current.Value) / Math.Abs(_slow.Current.Value);
                    return Math.Min(0.02m, delta);
                }

                public decimal GetConfidence()
                {
                    var scaled = GetMagnitude() * 50m;
                    return Math.Min(0.9m, Math.Max(0.05m, scaled));
                }

                public bool CanEmit(DateTime utcNow)
                {
                    return utcNow - _lastInsightUtc >= _cooldown;
                }

                public void MarkEmitted(DateTime utcNow)
                {
                    _lastInsightUtc = utcNow;
                }
            }
        }
    }
}
