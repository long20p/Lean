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
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Util;

namespace QuantConnect.Lean.Engine.DataFeeds.Queues
{
    /// <summary>
    /// <see cref="IDataQueueHandler"/> that fetches hourly price data from Tiingo IEX API.
    /// Optimized for hourly resolution with appropriate fetch intervals.
    /// </summary>
    public class TiingoHourlyDataQueueHandler : TiingoDataQueueHandlerBase
    {
        /// <summary>
        /// Gets the fetch interval between API calls (default: 1 hour)
        /// </summary>
        protected override TimeSpan FetchInterval => TimeSpan.FromSeconds(3600);

        /// <summary>
        /// Gets the initial delay before first fetch (default: 1 minute)
        /// </summary>
        protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(60);

        /// <summary>
        /// Gets the offset to apply to the last update time to make sure the following refresh is not skipped
        /// </summary>
        protected override TimeSpan LastUpdateOffset => TimeSpan.FromMinutes(1);

        /// <summary>
        /// Initializes a new instance of <see cref="TiingoHourlyDataQueueHandler"/> using the default aggregator.
        /// </summary>
        public TiingoHourlyDataQueueHandler()
            : this(Composer.Instance.GetExportedValueByTypeName<IDataAggregator>(nameof(AggregationManager)))
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="TiingoHourlyDataQueueHandler"/> with dependency injection support.
        /// </summary>
        public TiingoHourlyDataQueueHandler(IDataAggregator aggregator, IOptionChainProvider optionChainProvider = null, ITimeProvider timeProvider = null)
            : base(aggregator, optionChainProvider, timeProvider)
        {
        }
    }
}
