using QuantConnect.Data;
using QuantConnect.Data.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// Range breakout uses a defined period of time to set a price-range, and trades on leaving that range
    /// </summary>
    public class RangeBreakoutAlgorithm : QCAlgorithm
    {
        TradeBar openingBar;

        public override void Initialize()
        {
            SetStartDate(2018, 7, 1);
            SetEndDate(2019, 7, 1);
            SetCash(100000);
            AddEquity("TSLA", Resolution.Minute);
            //Consolidators are used to combine smaller data points into larger bars
            Consolidate("TSLA", TimeSpan.FromMinutes(30), OnDataConsolidated);

            //Created a scheduled event triggered at 1:30 calling the ClosePositions function
            Schedule.On(DateRules.EveryDay("TSLA"), TimeRules.At(13, 30), ClosePositions);
        }


        public override void OnData(Slice data)
        {
            // only trade after we have 30-minute consolidated bar with upper (High) and lower (Low) thresholds
            if (Portfolio.Invested || openingBar == null)
            {
                return;
            }

            // trade if price breaks above or below the opening range
            if (data["TSLA"].Close > openingBar.High)
            {
                SetHoldings("TSLA", 1);
            }

            if (data["TSLA"].Close < openingBar.Low)
            {
                SetHoldings("TSLA", -1);
            }
        }

        private void OnDataConsolidated(TradeBar bar)
        {
            //consolidated bar represents 30 minutes of data
            if (bar.Time.Hour == 9 && bar.Time.Minute == 30)
            {
                openingBar = bar;
            }
        }

        private void ClosePositions()
        {
            openingBar = null;
            Liquidate("TSLA");
        }
    }
}
