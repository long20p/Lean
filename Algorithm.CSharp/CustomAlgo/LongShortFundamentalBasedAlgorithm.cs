using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.UniverseSelection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// A long-short strategy purchases securities that are expected to increase in value 
    /// and shorts securities that are expected to decrease in value to balance out the bias of the portfolio
    /// </summary>
    public class LongShortFundamentalBasedAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            SetStartDate(2017, 5, 15);
            SetEndDate(2017, 7, 15);
            SetCash(100000);
            UniverseSettings.Resolution = Resolution.Hour;
            AddUniverseSelection(new LiquidValueUniverseSelectionModel());
            AddAlpha(new LongShortEYAlphaModel());
            SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
            SetExecution(new ImmediateExecutionModel());
        }
    }

    public class LiquidValueUniverseSelectionModel : FundamentalUniverseSelectionModel
    {
        private int _lastMonth = -1;

        public LiquidValueUniverseSelectionModel()
            : base(true, null)
        {
        }

        public override IEnumerable<Symbol> SelectCoarse(QCAlgorithm algorithm, IEnumerable<CoarseFundamental> coarse)
        {
            if (_lastMonth == algorithm.Time.Month)
            {
                return Universe.Unchanged;
            }

            _lastMonth = algorithm.Time.Month;

            var sortedByDollarVolume = coarse
                .Where(x => x.HasFundamentalData)
                .OrderByDescending(x => x.DollarVolume);

            return sortedByDollarVolume
                .Take(100)
                .Select(x => x.Symbol);
        }

        public override IEnumerable<Symbol> SelectFine(QCAlgorithm algorithm, IEnumerable<FineFundamental> fine)
        {
            var sortedByYields = fine.OrderByDescending(x => x.ValuationRatios.EarningYield);

            var universe = sortedByYields.Take(10).Concat(sortedByYields.TakeLast(10));

            return universe.Select(x => x.Symbol);
        }
    }

    public class LongShortEYAlphaModel : AlphaModel
    {
        private int _lastMonth = -1;

        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            var insights = new List<Insight>();

            //2. If statement to emit signals once a month
            if (_lastMonth == algorithm.Time.Month)
            {
                return insights;
            }
            _lastMonth = algorithm.Time.Month;

            //3. Use foreach to emit insights with insight directions 
            // based on whether earnings yield is greater or less than zero once a month
            foreach (var sec in algorithm.ActiveSecurities.Values)
            {
                var yield = sec.Fundamentals.ValuationRatios.EarningYield;
                if (yield.IsNaNOrInfinity())
                {
                    continue;
                }
                var direction = (InsightDirection)Math.Sign(yield);
                insights.Add(Insight.Price(sec.Symbol, TimeSpan.FromDays(28), direction));
            }
            return insights;
        }
    }
}
