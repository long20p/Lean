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
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Custom.Tiingo;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;
using static QuantConnect.StringExtensions;

namespace QuantConnect.Lean.Engine.DataFeeds.Queues
{
    /// <summary>
    /// Base class for <see cref="IDataQueueHandler"/> implementations that fetch real-time price data from Tiingo IEX API.
    /// Uses standard AddEquity() subscriptions while Tiingo provides the data feed.
    /// </summary>
    public abstract class TiingoDataQueueHandlerBase : IDataQueueHandler, IDataQueueUniverseProvider
    {
        private readonly IDataAggregator _aggregator;
        private readonly EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;
        private readonly IOptionChainProvider _optionChainProvider;
        private readonly MarketHoursDatabase _marketHoursDatabase;
        private readonly ITimeProvider _timeProvider;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<Symbol, SymbolState> _symbolStates = new();
        private readonly Timer _timer;
        private readonly string _authToken;
        private volatile bool _disposed;

        private sealed class SymbolState
        {
            public SymbolState(TimeZoneOffsetProvider offsetProvider)
            {
                OffsetProvider = offsetProvider;
                LastDataTime = DateTime.MinValue;
                LastPrice = 0m;
            }

            public TimeZoneOffsetProvider OffsetProvider { get; }
            public DateTime LastDataTime;
            public decimal LastPrice;
        }

        /// <summary>
        /// Continuous UTC time provider
        /// </summary>
        protected virtual ITimeProvider TimeProvider => _timeProvider;

        /// <summary>
        /// Gets the fetch interval between API calls
        /// </summary>
        protected abstract TimeSpan FetchInterval { get; }

        /// <summary>
        /// Gets the initial delay before first fetch
        /// </summary>
        protected abstract TimeSpan InitialDelay { get; }

        /// <summary>
        /// Gets the offset to apply to the last update time to make sure the following refresh is not skipped
        /// E.g. Set to 1 minute for hourly data to ensure we don't skip the next hour's fetch
        /// </summary>
        protected abstract TimeSpan LastUpdateOffset { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="TiingoDataQueueHandlerBase"/> with dependency injection support.
        /// </summary>
        protected TiingoDataQueueHandlerBase(IDataAggregator aggregator, IOptionChainProvider optionChainProvider = null, ITimeProvider timeProvider = null)
        {
            _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
            _timeProvider = timeProvider ?? RealTimeProvider.Instance;

            _authToken = Config.GetValue<string>("tiingo-auth-token");
            if (string.IsNullOrWhiteSpace(_authToken))
            {
                throw new InvalidOperationException("Tiingo auth token not configured. Set 'tiingo-auth-token' in config.json");
            }

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
            _optionChainProvider = optionChainProvider ?? CreateOptionChainProvider();

            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += OnSymbolsSubscribed;
            _subscriptionManager.UnsubscribeImpl += OnSymbolsUnsubscribed;

            _timer = new Timer(OnTimerElapsed, null, InitialDelay, FetchInterval);
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of the managed and unmanaged resources
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _timer?.Dispose();
                _httpClient?.Dispose();
                _subscriptionManager?.DisposeSafely();
            }

            _disposed = true;
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
                FetchAndEmitData();
            }
            catch (Exception err)
            {
                Log.Error(err);
            }
        }

        private void FetchAndEmitData()
        {
            var utcNow = TimeProvider.GetUtcNow();
            var symbols = _subscriptionManager.GetSubscribedSymbols()
                .Where(s => !s.IsCanonical() && !s.Value.Contains("UNIVERSE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!symbols.Any())
            {
                return;
            }

            foreach (var symbol in symbols)
            {
                try
                {
                    var state = _symbolStates.GetOrAdd(symbol, CreateSymbolState);
                    
                    // Check if we need to fetch new data (avoid hammering API)
                    if (utcNow - state.LastDataTime < FetchInterval)
                    {
                        continue;
                    }

                    FetchAndEmitSymbolData(symbol, state, utcNow);
                }
                catch (Exception ex)
                {
                    Log.Error($"TiingoDataQueueHandler.FetchAndEmitData(): Error processing {symbol}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the API endpoint for fetching real-time IEX data
        /// </summary>
        private string GetApiEndpoint(string ticker)
        {
            return Invariant($"https://api.tiingo.com/iex/{ticker}?token={Config.GetValue<string>("tiingo-auth-token")}");
        }

        private void FetchAndEmitSymbolData(Symbol symbol, SymbolState state, DateTime utcNow)
        {
            var tiingoTicker = TiingoSymbolMapper.GetTiingoTicker(symbol);
            var url = GetApiEndpoint(tiingoTicker);

            try
            {
                var response = _httpClient.GetStringAsync(url).Result;
                var data = JsonConvert.DeserializeObject<List<TiingoIexData>>(response);

                if (data == null || !data.Any())
                {
                    Log.Debug($"TiingoDataQueueHandler: No data returned for {symbol.Value}");
                    return;
                }

                var bar = data[0];
                var exchangeTime = state.OffsetProvider.ConvertFromUtc(utcNow);

                // Update state
                state.LastDataTime = utcNow - LastUpdateOffset;
                state.LastPrice = bar.Last;

                // Emit trade tick
                var supportedTypes = SubscriptionManager.DefaultDataTypes()[symbol.SecurityType];
                if (supportedTypes.Contains(TickType.Trade) && bar.Last > 0)
                {
                    _aggregator.Update(new Tick
                    {
                        Time = exchangeTime,
                        Symbol = symbol,
                        Value = bar.Last,
                        TickType = TickType.Trade,
                        Quantity = bar.LastSize ?? -1
                    });
                }

                // Emit quote tick
                if (supportedTypes.Contains(TickType.Quote) && bar.BidPrice.HasValue && bar.AskPrice.HasValue)
                {
                    _aggregator.Update(new Tick(
                        exchangeTime,
                        symbol,
                        string.Empty,
                        string.Empty,
                        bidSize: bar.BidSize ?? -1,
                        bidPrice: bar.BidPrice.Value,
                        askPrice: bar.AskPrice.Value,
                        askSize: bar.AskSize ?? -1));
                }

                Log.Trace($"TiingoDataQueueHandler: {symbol.Value} | Last: {bar.Last} | Bid: {bar.BidPrice} | Ask: {bar.AskPrice} | Time: {bar.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                Log.Error($"TiingoDataQueueHandler.FetchAndEmitSymbolData(): Failed to fetch {symbol.Value}: {ex.Message}");
            }
        }

        private SymbolState CreateSymbolState(Symbol symbol)
        {
            var exchangeTimeZone = _marketHoursDatabase.GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType).TimeZone;
            var offsetProvider = new TimeZoneOffsetProvider(exchangeTimeZone, TimeProvider.GetUtcNow(), Time.EndOfTime);
            return new SymbolState(offsetProvider);
        }

        private bool OnSymbolsSubscribed(IEnumerable<Symbol> symbols, TickType tickType)
        {
            foreach (var symbol in symbols)
            {
                _symbolStates.GetOrAdd(symbol, CreateSymbolState);
                Log.Trace($"TiingoDataQueueHandler: Subscribed to {symbol.Value}");
            }

            return true;
        }

        private bool OnSymbolsUnsubscribed(IEnumerable<Symbol> symbols, TickType tickType)
        {
            foreach (var symbol in symbols)
            {
                RemoveSymbolStateIfUnused(symbol);
                Log.Trace($"TiingoDataQueueHandler: Unsubscribed from {symbol.Value}");
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

        /// <summary>
        /// Tiingo IEX API response model
        /// </summary>
        private class TiingoIexData
        {
            [JsonProperty("ticker")]
            public string Ticker { get; set; }

            [JsonProperty("timestamp")]
            public DateTime Timestamp { get; set; }

            [JsonProperty("tngoLast")]
            public decimal Last { get; set; }

            [JsonProperty("lastSize")]
            public int? LastSize { get; set; }

            [JsonProperty("bidPrice")]
            public decimal? BidPrice { get; set; }

            [JsonProperty("bidSize")]
            public int? BidSize { get; set; }

            [JsonProperty("askPrice")]
            public decimal? AskPrice { get; set; }

            [JsonProperty("askSize")]
            public int? AskSize { get; set; }

            [JsonProperty("open")]
            public decimal? Open { get; set; }

            [JsonProperty("high")]
            public decimal High { get; set; }

            [JsonProperty("low")]
            public decimal Low { get; set; }

            [JsonProperty("close")]
            public decimal? Close { get; set; }

            [JsonProperty("prevClose")]
            public decimal? PrevClose { get; set; }

            [JsonProperty("volume")]
            public long Volume { get; set; }
        }
    }
}
