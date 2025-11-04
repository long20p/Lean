using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    /// <summary>
    /// Often when two assets share related customer bases, or sell similar products, their asset price can move together.
    /// A pairs trade is triggered when the difference between a pair of assets crosses an upper or lower threshold.
    /// The strategy's goal is to sell whichever is the expensive stock of the pair at the time and buy the cheaper one.
    /// </summary>
    public class PairsTradingAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            SetStartDate(2018, 7, 1);
            SetEndDate(2019, 3, 31);
            SetCash(100000);
            var symbols = new[] 
            { 
                QuantConnect.Symbol.Create("PEP", SecurityType.Equity, Market.USA), //Pepsi
                QuantConnect.Symbol.Create("KO", SecurityType.Equity, Market.USA) //Coca-Cola
            };
            SetUniverseSelection(new ManualUniverseSelectionModel(symbols));
            UniverseSettings.Resolution = Resolution.Hour;
            UniverseSettings.DataNormalizationMode = DataNormalizationMode.Raw;
            AddAlpha(new PairsTradingAlphaModel());
            SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
            SetExecution(new ImmediateExecutionModel());
        }
        public override void OnEndOfDay(Symbol symbol)
        {
            Log("Taking a position of " + Portfolio[symbol].Quantity.ToString() + " units of symbol " + symbol.ToString());
        }
    }

    public partial class PairsTradingAlphaModel : AlphaModel
    {
        SimpleMovingAverage spreadMean;
        StandardDeviation spreadStd;
        TimeSpan period;
        public Security[] Pair;

        public PairsTradingAlphaModel()
        {
            spreadMean = new SimpleMovingAverage(500);
            spreadStd = new StandardDeviation(500);
            period = TimeSpan.FromHours(2);
            Pair = new Security[2];
        }

        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            var spread = Pair[1].Price - Pair[0].Price;
            spreadMean.Update(algorithm.Time, spread);
            spreadStd.Update(algorithm.Time, spread);

            var upperthreshold = spreadMean + spreadStd;
            var lowerthreshold = spreadMean - spreadStd;

            if (spread > upperthreshold)
            {
                return Insight.Group(
                    Insight.Price(Pair[0].Symbol, period, InsightDirection.Up),
                    Insight.Price(Pair[1].Symbol, period, InsightDirection.Down)
                );
            }

            if (spread < lowerthreshold)
            {
                return Insight.Group(
                    Insight.Price(Pair[0].Symbol, period, InsightDirection.Down),
                    Insight.Price(Pair[1].Symbol, period, InsightDirection.Up)
                );
            }

            return Enumerable.Empty<Insight>();
        }

        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            Pair = changes.AddedSecurities.ToArray();


            //1. Call for 500 days of history data for each symbol in the pair and save to the variable history
            var history = algorithm.History(Pair.Select(x => x.Symbol), 500);

            //2. Iterate through the history tuple and update the mean and standard deviation with historical data 
            foreach (var slice in history)
            {
                var spread = slice[Pair[1].Symbol].Close - slice[Pair[0].Symbol].Close;
                spreadMean.Update(algorithm.Time, spread);
                spreadStd.Update(algorithm.Time, spread);
            }

        }
    }
}
