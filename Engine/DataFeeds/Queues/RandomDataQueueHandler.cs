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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using QuantConnect;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Lean.Engine.DataFeeds.Queues
{
    /// <summary>
    /// Simple <see cref="IDataQueueHandler"/> that emits pseudo-random ticks for any subscribed symbol.
    /// Useful when debugging live algorithms without wiring a real brokerage/feed.
    /// </summary>
    public class RandomDataQueueHandler : IDataQueueHandler, IDataQueueUniverseProvider
    {
        private readonly IDataAggregator _aggregator;
        private readonly EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;
        private readonly IOptionChainProvider _optionChainProvider;
        private readonly MarketHoursDatabase _marketHoursDatabase;
        private readonly ITimeProvider _timeProvider;
        private readonly Random _random = new Random();
        private readonly ConcurrentDictionary<Symbol, SymbolState> _symbolStates = new();
        private readonly Timer _timer;

        private readonly TimeSpan _interval;
        private readonly decimal _priceStep;
        private readonly decimal _drift;
        private readonly decimal _initialPrice;
        private readonly decimal _minPrice;
        private readonly decimal _maxPrice;
        private readonly decimal _spreadFraction;
        private readonly int _minTradeSize;
        private readonly int _maxTradeSize;
        private readonly int _minQuoteSize;
        private readonly int _maxQuoteSize;
        private volatile bool _disposed;

        /// <summary>
        /// Continuous UTC time provider
        /// </summary>
    protected virtual ITimeProvider TimeProvider => _timeProvider;

    private sealed class SymbolState
        {
            public SymbolState(TimeZoneOffsetProvider offsetProvider, decimal initialPrice)
            {
                OffsetProvider = offsetProvider;
                LastPrice = initialPrice;
            }

            public TimeZoneOffsetProvider OffsetProvider { get; }
            public decimal LastPrice;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="RandomDataQueueHandler"/> using the default aggregator.
        /// </summary>
        public RandomDataQueueHandler()
            : this(Composer.Instance.GetExportedValueByTypeName<IDataAggregator>(nameof(AggregationManager)))
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="RandomDataQueueHandler"/> with dependency injection support.
        /// </summary>
        public RandomDataQueueHandler(IDataAggregator aggregator, IOptionChainProvider optionChainProvider = null, ITimeProvider timeProvider = null)
        {
            _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
            _timeProvider = timeProvider ?? RealTimeProvider.Instance;

            _initialPrice = Config.GetValue("random-data-initial-price", 100m);
            _priceStep = Config.GetValue("random-data-price-step", 0.25m);
            _drift = Config.GetValue("random-data-drift", 0m);
            _spreadFraction = Config.GetValue("random-data-spread-percent", 0.0025m);
            _minPrice = Config.GetValue("random-data-min-price", 0m);
            _maxPrice = Config.GetValue("random-data-max-price", 0m);
            _minTradeSize = Math.Max(1, Config.GetInt("random-data-min-trade-size", 1));
            _maxTradeSize = Math.Max(_minTradeSize, Config.GetInt("random-data-max-trade-size", 50));
            _minQuoteSize = Math.Max(1, Config.GetInt("random-data-min-quote-size", _minTradeSize));
            _maxQuoteSize = Math.Max(_minQuoteSize, Config.GetInt("random-data-max-quote-size", _maxTradeSize));
            var intervalMs = Math.Max(1, Config.GetInt("random-data-interval-ms", 250));
            _interval = TimeSpan.FromMilliseconds(intervalMs);

            _marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
            _optionChainProvider = optionChainProvider ?? CreateOptionChainProvider();

            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += OnSymbolsSubscribed;
            _subscriptionManager.UnsubscribeImpl += OnSymbolsUnsubscribed;

            _timer = new Timer(OnTimerElapsed, null, _interval, _interval);
        }

        /// <summary>
        /// Subscribe to the specified configuration
        /// </summary>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            _subscriptionManager.Subscribe(dataConfig);
            return enumerator;
        }

        /// <summary>
        /// Sets the job we're subscribing for
        /// </summary>
        public void SetJob(LiveNodePacket job)
        {
        }

        /// <summary>
        /// Removes the specified configuration
        /// </summary>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            _subscriptionManager.Unsubscribe(dataConfig);
            _aggregator.Remove(dataConfig);
            RemoveSymbolStateIfUnused(dataConfig.Symbol);
        }

        /// <summary>
        /// Returns whether the data provider is connected
        /// </summary>
        public bool IsConnected => !_disposed;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            _timer?.Dispose();
        }

        /// <summary>
        /// Method returns a collection of Symbols that are available at the data source.
        /// </summary>
        public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
        {
            switch (symbol.SecurityType)
            {
                case SecurityType.Option:
                case SecurityType.IndexOption:
                case SecurityType.FutureOption:
                    foreach (var result in _optionChainProvider.GetOptionContractList(symbol, DateTime.UtcNow.Date))
                    {
                        yield return result;
                    }
                    break;
            }
        }

        /// <summary>
        /// Checks if the handler can perform universe selection
        /// </summary>
        public bool CanPerformSelection() => true;

        private void OnTimerElapsed(object _)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                EmitTicks();
            }
            catch (Exception err)
            {
                Log.Error(err);
            }
        }

        private void EmitTicks()
        {
            var utcNow = TimeProvider.GetUtcNow();
            foreach (var symbol in _subscriptionManager.GetSubscribedSymbols())
            {
                if (symbol.IsCanonical() || symbol.Value.Contains("UNIVERSE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var state = _symbolStates.GetOrAdd(symbol, CreateSymbolState);
                var exchangeTime = state.OffsetProvider.ConvertFromUtc(utcNow);
                var price = NextPrice(state);

                var supportedTypes = SubscriptionManager.DefaultDataTypes()[symbol.SecurityType];

                if (supportedTypes.Contains(TickType.Trade))
                {
                    var quantity = NextInt(_minTradeSize, _maxTradeSize);
                    _aggregator.Update(new Tick
                    {
                        Time = exchangeTime,
                        Symbol = symbol,
                        Value = price,
                        TickType = TickType.Trade,
                        Quantity = quantity
                    });
                }

                if (supportedTypes.Contains(TickType.Quote))
                {
                    var spread = Math.Max(price * _spreadFraction, 0.01m);
                    var halfSpread = spread / 2m;
                    var bidPrice = ClampPrice(price - halfSpread);
                    var askPrice = ClampPrice(price + halfSpread);
                    var bidSize = NextInt(_minQuoteSize, _maxQuoteSize);
                    var askSize = NextInt(_minQuoteSize, _maxQuoteSize);

                    _aggregator.Update(new Tick(exchangeTime, symbol, string.Empty, string.Empty,
                        bidSize: bidSize, bidPrice: bidPrice, askPrice: askPrice, askSize: askSize));
                }
            }
        }

        private SymbolState CreateSymbolState(Symbol symbol)
        {
            var exchangeTimeZone = _marketHoursDatabase.GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType).TimeZone;
            var offsetProvider = new TimeZoneOffsetProvider(exchangeTimeZone, TimeProvider.GetUtcNow(), Time.EndOfTime);
            return new SymbolState(offsetProvider, _initialPrice);
        }

        private decimal NextPrice(SymbolState state)
        {
            var randomStep = ((decimal)_random.NextDouble() * 2m - 1m) * _priceStep;
            var candidate = state.LastPrice + randomStep + state.LastPrice * _drift;
            candidate = ClampPrice(candidate <= 0 ? _initialPrice : candidate);
            state.LastPrice = candidate;
            return state.LastPrice;
        }

        private decimal ClampPrice(decimal price)
        {
            if (_minPrice > 0 && price < _minPrice)
            {
                price = _minPrice;
            }

            if (_maxPrice > 0 && price > _maxPrice)
            {
                price = _maxPrice;
            }

            return price;
        }

        private int NextInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive)
            {
                return minInclusive;
            }

            return _random.Next(minInclusive, maxInclusive + 1);
        }

        private bool OnSymbolsSubscribed(IEnumerable<Symbol> symbols, TickType tickType)
        {
            foreach (var symbol in symbols)
            {
                _symbolStates.GetOrAdd(symbol, CreateSymbolState);
            }

            return true;
        }

        private bool OnSymbolsUnsubscribed(IEnumerable<Symbol> symbols, TickType tickType)
        {
            foreach (var symbol in symbols)
            {
                RemoveSymbolStateIfUnused(symbol);
            }

            return true;
        }

        private void RemoveSymbolStateIfUnused(Symbol symbol)
        {
            if (!_subscriptionManager.GetSubscribedSymbols().Contains(symbol))
            {
                _symbolStates.TryRemove(symbol, out _);
            }
        }

        private static LiveOptionChainProvider CreateOptionChainProvider()
        {
            var mapFileProvider = Composer.Instance.GetPart<IMapFileProvider>();
            var historyManager = (IHistoryProvider)Composer.Instance.GetPart<HistoryProviderManager>()
                                  ?? Composer.Instance.GetPart<IHistoryProvider>();
            var optionChainProvider = new LiveOptionChainProvider();
            optionChainProvider.Initialize(new(mapFileProvider, historyManager));
            return optionChainProvider;
        }
    }
}
