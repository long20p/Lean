using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.UniverseSelection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    public class SectorWeightAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            SetStartDate(2016, 12, 28);
            SetEndDate(2017, 3, 1);
            SetCash(100000);

            UniverseSettings.Resolution = Resolution.Hour;
            SetUniverseSelection(new MyUniverseSelectionModel());
            SetAlpha(new ConstantAlphaModel(InsightType.Price, InsightDirection.Up, TimeSpan.FromDays(1), 0.025, null));
            SetPortfolioConstruction(new MySectorWeightingPortfolioConstructionModel(Resolution.Daily));
            SetExecution(new ImmediateExecutionModel());
        }
    }

    public class MyUniverseSelectionModel : FundamentalUniverseSelectionModel
    {
        public MyUniverseSelectionModel()
            : base(true)
        {
        }

        public override IEnumerable<Symbol> SelectCoarse(QCAlgorithm algorithm, IEnumerable<CoarseFundamental> coarse)
        {
            return
                (from c in coarse
                 where c.HasFundamentalData && c.Price > 0
                 orderby c.DollarVolume descending
                 select c.Symbol).Take(100);
        }

        public override IEnumerable<Symbol> SelectFine(QCAlgorithm algorithm, IEnumerable<FineFundamental> fine)
        {
            var technology = new List<FineFundamental>();
            technology.AddRange(
                (from f in fine
                 where f.AssetClassification.MorningstarSectorCode == MorningstarSectorCode.Technology
                 orderby f.MarketCap descending
                 select f).Take(3)
            );
            var financialServices = new List<FineFundamental>();
            financialServices.AddRange(
                (from f in fine
                 where f.AssetClassification.MorningstarSectorCode == MorningstarSectorCode.FinancialServices
                 orderby f.MarketCap descending
                 select f).Take(2)
            );
            var consumerDefensive = new List<FineFundamental>();
            consumerDefensive.AddRange(
                (from f in fine
                 where f.AssetClassification.MorningstarSectorCode == MorningstarSectorCode.ConsumerDefensive
                 orderby f.MarketCap descending
                 select f).Take(1)
            );
            var selection = new List<Symbol>();
            selection.AddRange(technology.Select(f => f.Symbol));
            selection.AddRange(financialServices.Select(f => f.Symbol));
            selection.AddRange(consumerDefensive.Select(f => f.Symbol));
            return selection;
        }
    }

    public class MySectorWeightingPortfolioConstructionModel : EqualWeightingPortfolioConstructionModel
    {
        private readonly Dictionary<int, List<Symbol>> symbolBySectorCode = new Dictionary<int, List<Symbol>>();
        private readonly Dictionary<Insight, double> result = new Dictionary<Insight, double>();
        private List<Insight> insightsInSector = new List<Insight>();
        private decimal percent = 0;
        private decimal sectorBuyingPower = 0;

        public MySectorWeightingPortfolioConstructionModel(Resolution resolution = Resolution.Daily)
            : base(resolution)
        {
        }

        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            foreach (var security in changes.AddedSecurities)
            {
                var sectorCode = security.Fundamentals?.AssetClassification?.MorningstarSectorCode;
                if (sectorCode.HasValue)
                {
                    if (!symbolBySectorCode.ContainsKey(sectorCode.Value))
                    {
                        symbolBySectorCode[sectorCode.Value] = new List<Symbol>();
                    }
                    symbolBySectorCode[sectorCode.Value].Add(security.Symbol);
                }
            }

            foreach (var security in changes.RemovedSecurities)
            {
                var symbol = security.Symbol;
                var sectorCode = security.Fundamentals?.AssetClassification?.MorningstarSectorCode;
                if (sectorCode.HasValue)
                {
                    symbolBySectorCode[sectorCode.Value].Remove(symbol);
                }
            }

            base.OnSecuritiesChanged(algorithm, changes);
        }

        protected override Dictionary<Insight, double> DetermineTargetPercent(List<Insight> activeInsights)
        {
            result.Clear();

            //1. Set the sectorBuyingPower before by dividing one by the length of symbolBySectorCode
            // Hint: Make sure you use "1m" to denote a decimal number
            sectorBuyingPower = 1m / symbolBySectorCode.Keys.Count;

            //2. For the sector and symbols in the symbolBySectorCode dictionary, use iterate through to save 
            // the insight if the symbol is in our symbol list to insightsInSector
            foreach (var kvp in symbolBySectorCode)
            {
                // (This is a class variable, no need for var)
                insightsInSector = activeInsights
                    .Where(i => kvp.Value.Contains(i.Symbol))
                    .ToList();

                //3. Divide the sectorBuyingPower by the length of insightsInSector to calculate the variable percent
                // The percent is the weight we'll assign the direction of the insight
                percent = sectorBuyingPower / insightsInSector.Count;

                //4. For each insight in insightsInSector, save the insight duration as a value in the result dictionary
                // the duration is calculated by multiplying the insight direction by the percent 
                foreach (var insight in insightsInSector)
                {
                    result[insight] = (double)((int)insight.Direction * percent);
                }
            }

            return result;
        }
    }
}
