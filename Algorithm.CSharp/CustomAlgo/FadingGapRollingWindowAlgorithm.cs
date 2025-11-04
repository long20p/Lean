using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// The difference between the close price of the previous day and the opening price of the current day is referred to as a gap. 
    /// Fading the gap is a strategy that monitors for a large gap down and buys stock assuming it will rebound.
    /// </summary>
    public class FadingGapRollingWindowAlgorithm : QCAlgorithm
    {
        private RollingWindow<TradeBar> window;
        private StandardDeviation volatility;

        public override void Initialize()
        {
            SetStartDate(2017, 11, 1);
            SetEndDate(2018, 7, 1);
            SetCash(100000);
            AddEquity("TSLA", Resolution.Minute);

            // Get the last bar before market close and the first bar after market open
            Schedule.On(DateRules.EveryDay("TSLA"), TimeRules.BeforeMarketClose("TSLA", 0), ClosingBar);
            Schedule.On(DateRules.EveryDay("TSLA"), TimeRules.AfterMarketOpen("TSLA", 1), OpeningBar);

            // Close positions 45 minutes after market open
            Schedule.On(DateRules.EveryDay("TSLA"), TimeRules.AfterMarketOpen("TSLA", 45), ClosePositions);

            window = new RollingWindow<TradeBar>(2);

            // Volatility indicator with a 30-bar period
            volatility = new StandardDeviation("TSLA", 30);
        }

        public override void OnData(Slice slice)
        {
            if (slice["SPY"] != null)
            {
                // Manually update the volatility indicator
                volatility.Update(Time, slice["SPY"].Close);
            }
        }

        public void OpeningBar()
        {
            if (CurrentSlice["TSLA"] != null)
            {
                window.Add(CurrentSlice["TSLA"]);
            }

            //1. If our window is not full or volatility is not ready then return
            if (!window.IsReady || !volatility.IsReady)
            {
                return;
            }

            //2. Calculate the change in overnight price
            var delta = window[0].Price - window[1].Price;
            var deviation = delta / volatility;

            //3. If delta is less than -$0.9 and deviation is less than -3 (when we are 99% confident this gap is an anomaly)
            if (delta < -0.9m && deviation < -3)
            {
                SetHoldings("TSLA", 1);
            }
        }


        public void ClosingBar()
        {
            window.Add(CurrentSlice["TSLA"]);
        }

        public void ClosePositions()
        {
            Liquidate();
        }
    }
}
