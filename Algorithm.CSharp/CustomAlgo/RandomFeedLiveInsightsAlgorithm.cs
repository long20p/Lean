/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

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
    /// Live paper trading algorithm tailored for the random data queue handler. It only emits insights so you can
    /// validate Live Trading ResultHandler pipelines without wiring execution, portfolio construction, or risk models.
    /// Run it with the <c>live-paper-random</c> environment to stream random ticks.
    /// </summary>
    public class RandomFeedLiveInsightsAlgorithm : QCAlgorithm
    {
        private static readonly string[] DefaultTickers = new[] { "SPY", "AAPL", "IBM" };
        private static readonly char[] SymbolSeparators = new[] { ',', ';', '|', ' ' };
        private const string SymbolsParameterName = "random-feed-symbols";
        private const string InsightPeriodParameterName = "random-feed-insight-minutes";
        private const string FastPeriodParameterName = "random-feed-fast-period";
        private const string SlowPeriodParameterName = "random-feed-slow-period";

        /// <summary>
        /// Initialise the algorithm. In live-paper mode Lean sets the start date and cash automatically, but we
        /// keep the declarations for easier local testing.
        /// </summary>
        public override void Initialize()
        {
            UniverseSettings.Resolution = Resolution.Second;
            UniverseSettings.FillForward = true;
            UniverseSettings.ExtendedMarketHours = true;

            SetCash(100000);
            SetBrokerageModel(new DefaultBrokerageModel(AccountType.Margin));
            SetWarmUp(TimeSpan.FromMinutes(2));
            SetBenchmark("SPY");

            if (!LiveMode)
            {
                Log("Switch the configuration environment to 'live-paper-random' to experience the random data feed.");
            }

            var symbols = ParseSymbolsParameter().ToList();
            SetUniverseSelection(new ManualUniverseSelectionModel(symbols));

            var insightPeriodMinutes = GetPositiveIntParameter(InsightPeriodParameterName, 5);
            var fastPeriod = GetPositiveIntParameter(FastPeriodParameterName, 8);
            var slowPeriod = Math.Max(fastPeriod + 2, GetPositiveIntParameter(SlowPeriodParameterName, 21));
            var predictionInterval = TimeSpan.FromMinutes(insightPeriodMinutes);

            // Only set an Alpha model so Lean emits insights but does not place trades
            SetAlpha(new RandomDataMomentumAlphaModel(predictionInterval, fastPeriod, slowPeriod));

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
        /// Alpha model that runs a simple fast/slow SMA comparison on streaming random ticks and turns the delta
        /// into recurring up/down insights.
        /// </summary>
        private sealed class RandomDataMomentumAlphaModel : AlphaModel
        {
            private readonly TimeSpan _predictionInterval;
            private readonly int _fastPeriod;
            private readonly int _slowPeriod;
            private readonly Dictionary<Symbol, SymbolState> _symbolStates = new();

            public RandomDataMomentumAlphaModel(TimeSpan predictionInterval, int fastPeriod, int slowPeriod)
            {
                _predictionInterval = predictionInterval;
                _fastPeriod = fastPeriod;
                _slowPeriod = slowPeriod;
                Name = $"{nameof(RandomDataMomentumAlphaModel)}({_fastPeriod},{_slowPeriod})";
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
                private readonly SimpleMovingAverage _fast;
                private readonly SimpleMovingAverage _slow;
                private readonly TimeSpan _cooldown;
                private DateTime _lastInsightUtc = DateTime.MinValue;

                public Symbol Symbol { get; }

                public SymbolState(Symbol symbol, int fastPeriod, int slowPeriod, TimeSpan horizon)
                {
                    Symbol = symbol;
                    _fast = new SimpleMovingAverage(fastPeriod);
                    _slow = new SimpleMovingAverage(slowPeriod);
                    var minSeconds = Math.Max(5, (int)Math.Round(horizon.TotalSeconds / 2.0));
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
