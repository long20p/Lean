using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    public class FastSlowEmaAlgorithm : QCAlgorithm
    {
        private readonly ConcurrentDictionary<Symbol, SelectionData> averages = new ConcurrentDictionary<Symbol, SelectionData>();

        public override void Initialize()
        {
            SetStartDate(2019, 1, 1);
            SetEndDate(2019, 4, 1);
            SetCash(100000);
            AddUniverse(CoarseSelectionFilter);
            UniverseSettings.Resolution = Resolution.Daily;
        }

        public IEnumerable<Symbol> CoarseSelectionFilter(IEnumerable<CoarseFundamental> universe)
        {
            var selected = new List<Symbol>();
            universe = universe
                .Where(x => x.Price > 10)
                .OrderByDescending(x => x.DollarVolume).Take(100);

            foreach (var coarse in universe)
            {
                var symbol = coarse.Symbol;

                //1. Check if averages contains symbol and if not; create a new SelectionData for it.
                if (!averages.ContainsKey(coarse.Symbol))
                {
                    //2. If the symbol is not in our dictionary, create a new SelectionData for it 
                    averages[coarse.Symbol] = new SelectionData();
                }

                var symbolData = averages[coarse.Symbol];
                //3. Update the averages with the latest adjusted price
                symbolData.Update(Time, coarse.AdjustedPrice);

                if (symbolData.IsReady() && symbolData.Fast > symbolData.Slow)
                {
                    selected.Add(symbol);
                }
            }

            return selected.Take(10);
        }

        public override void OnSecuritiesChanged(SecurityChanges changes)
        {
            foreach (var security in changes.RemovedSecurities)
            {
                if (security.Invested)
                {
                    Liquidate(security.Symbol);
                }
            }

            foreach (var security in changes.AddedSecurities)
            {
                SetHoldings(security.Symbol, 0.10m);
            }
        }
    }

    public partial class SelectionData
    {
        public ExponentialMovingAverage Fast { get; private set; }
        public ExponentialMovingAverage Slow { get; private set; }

        public bool IsReady() { return Slow.IsReady && Fast.IsReady; }

        public SelectionData()
        {
            Fast = new ExponentialMovingAverage(50);
            Slow = new ExponentialMovingAverage(200);
        }

        public bool Update(DateTime time, decimal value)
        {
            Slow.Update(time, value);
            Fast.Update(time, value);
            return IsReady();
        }
    }
}
