using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data;
using QuantConnect.Data.Custom.Tiingo;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.DataSource;
using QuantConnect.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    public class SentimentAnalysisAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            SetStartDate(2016, 11, 1);
            SetEndDate(2017, 3, 1);
            var symbols = new[] 
            {
                QuantConnect.Symbol.Create("AAPL", SecurityType.Equity, Market.USA),
                QuantConnect.Symbol.Create("NKE", SecurityType.Equity, Market.USA)
            };
            SetUniverseSelection(new ManualUniverseSelectionModel(symbols));
            AddAlpha(new NewsSentimentAlphaModel());
            SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
            SetExecution(new ImmediateExecutionModel());
            SetRiskManagement(new NullRiskManagementModel());
        }
    }

    public class NewsData
    {
        public Symbol Symbol { get; }
        public RollingWindow<double> Window { get; }

        public NewsData(Symbol symbol)
        {
            Symbol = symbol;
            Window = new RollingWindow<double>(100);
        }
    }

    public partial class NewsSentimentAlphaModel : AlphaModel
    {
        private double _score;

        public Dictionary<Symbol, NewsData> _newsData = new Dictionary<Symbol, NewsData>();

        public Dictionary<string, double> wordScores = new Dictionary<string, double>()
        {
            {"attractive",0.5}, {"bad",-0.5}, {"beat",0.5}, {"beneficial",0.5},
            {"down",-0.5}, {"excellent",0.5}, {"fail",-0.5}, {"failed",-0.5}, {"good",0.5},
            {"great",0.5}, {"growth",0.5}, {"large",0.5}, {"lose",-0.5}, {"lucrative",0.5},
            {"mishandled",-0.5}, {"missed",-0.5}, {"missing",-0.5}, {"nailed",0.5},
            {"negative",-0.5}, {"poor",-0.5}, {"positive",0.5}, {"profitable",0.5},
            {"right",0.5}, {"solid",0.5}, {"sound",0.5}, {"success",0.5}, {"un_lucrative",-0.5},
            {"unproductive",-0.5}, {"up",0.5}, {"worthwhile",0.5}, {"wrong",-0.5}
        };

        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            var insights = new List<Insight>();

            var news = data.Get<TiingoNews>();

            foreach (var article in news.Values)
            {
                var words = article.Description.ToLower().Split(' ');
                _score = words
                    .Where(x => wordScores.ContainsKey(x))
                    .Sum(x => wordScores[x]);

                // 1.  Get the underlying symbol and save to the variable symbol
                var symbol = article.Symbol.Underlying;

                // 2. Add scores to the rolling window associated with its _newsData symbol 
                _newsData[symbol].Window.Add(_score);

                // 3. Sum the rolling window scores, save to sentiment
                var sentiment = _newsData[symbol].Window.Sum();

                // If _sentiment aggregate score for the time period is greater than 5, emit an up insight
                if (sentiment > 5)
                {
                    insights.Add(Insight.Price(symbol, TimeSpan.FromDays(1), InsightDirection.Up));
                }
            }
            return insights;
        }

        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {

            foreach (var security in changes.AddedSecurities)
            {
                var symbol = security.Symbol;
                var newsAsset = algorithm.AddData<TiingoNews>(symbol);
                _newsData[symbol] = new NewsData(newsAsset.Symbol);
            }

            foreach (var security in changes.RemovedSecurities)
            {
                NewsData newsData;
                if (_newsData.Remove(security.Symbol, out newsData))
                {
                    algorithm.RemoveSecurity(newsData.Symbol);
                }
            }
        }
    }
}
