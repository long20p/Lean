using QuantConnect.Data;
using QuantConnect.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// A tactical asset allocation (TAA) strategy allows us to move the portfolio between assets depending on the market conditions. 
    /// We can use TAA to liquidate trend laggards and capture strong market trends for short-term profit.
    /// </summary>
    public class MomentumBasedAlgorithm : QCAlgorithm
    {
        private MomentumPercent spyMomentum;
        private MomentumPercent bondMomentum;
        public override void Initialize()
        {
            Settings.DailyPreciseEndTime = false;
            SetStartDate(2007, 8, 1);
            SetEndDate(2010, 8, 1);
            SetCash(3000);

            AddEquity("SPY", Resolution.Daily);
            AddEquity("BND", Resolution.Daily);

            spyMomentum = MOMP("SPY", 50, Resolution.Daily);
            bondMomentum = MOMP("BND", 50, Resolution.Daily);

            SetBenchmark("SPY");
            SetWarmUp(50);
        }

        public override void OnData(Slice data)
        {
            if (IsWarmingUp)
                return;

            //1. Limit trading to happen once per week
            if (Time.DayOfWeek != DayOfWeek.Tuesday)
            {
                return;
            }

            if (spyMomentum > bondMomentum)
            {
                Liquidate("BND");
                SetHoldings("SPY", 1);
            }

            else
            {
                Liquidate("SPY");
                SetHoldings("BND", 1);
            }
        }
    }
}
