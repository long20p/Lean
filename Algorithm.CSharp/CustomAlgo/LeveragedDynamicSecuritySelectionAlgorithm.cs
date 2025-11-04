using QuantConnect.Data.UniverseSelection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// Universe selection helps us avoid selection bias by algorithmically choosing our assets for trading. 
    /// Selection bias is introduced when the asset selection is influenced by personal or non-random decision making, 
    /// and often results in selecting winning stocks based on future knowledge.
    /// </summary>
    public class LeveragedDynamicSecuritySelectionAlgorithm : QCAlgorithm
    {
        private IEnumerable<Symbol> filteredByPrice;

        public override void Initialize()
        {
            SetStartDate(2019, 1, 11);
            SetEndDate(2019, 7, 1);
            SetCash(100000);
            AddUniverse(CoarseSelectionFilter);
            UniverseSettings.Resolution = Resolution.Daily;

            //1. Set the leverage to 2 
            UniverseSettings.Leverage = 2;
        }

        public IEnumerable<Symbol> CoarseSelectionFilter(IEnumerable<CoarseFundamental> coarse)
        {
            //Filter to select top 10 by dollar volume with price greater than $10
            filteredByPrice = coarse
                .OrderByDescending(x => x.DollarVolume)
                .Where(x => x.Price > 10).Select(x => x.Symbol)
                .Take(10);
            return filteredByPrice;
        }

        public override void OnSecuritiesChanged(SecurityChanges changes)
        {
            Log($"OnSecuritiesChanged({UtcTime}):: {changes}");
            foreach (var security in changes.RemovedSecurities)
            {
                if (security.Invested)
                {
                    Liquidate(security.Symbol);
                }
            }

            //2. Now that we have more leverage, set the allocation to set the allocation to 18% each instead of 10%
            foreach (var security in changes.AddedSecurities)
            {
                SetHoldings(security.Symbol, 0.18m);
            }
        }
    }
}
