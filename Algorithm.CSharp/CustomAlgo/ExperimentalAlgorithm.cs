using QuantConnect.Data;
using QuantConnect.Indicators;
using QuantConnect.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    public class ExperimentalAlgorithm : QCAlgorithm//, IRegressionAlgorithmDefinition
    {
        private Symbol symbol;
        private ExponentialMovingAverage ema;

        public override void Initialize()
        {
            SetStartDate(2021, 7, 1);
            SetEndDate(2025, 6, 30);
            SetCash(100000);

            symbol = AddEquity("MSFT", Resolution.Daily);
            ema = EMA(symbol, 50, Resolution.Daily);

            SetBenchmark("SPY");
            SetWarmup(50);
        }

        public override void OnData(Slice slice)
        {
            if (!ema.IsReady || IsWarmingUp)
            {
                return;
            }

            if (!Portfolio.Invested && slice[symbol].Price > ema)
            {
                SetHoldings(symbol, 1.0);
            }
            else if (Portfolio.Invested && slice[symbol].Price < ema)
            {
                Liquidate(symbol);
            }
        }

        //public AlgorithmStatus AlgorithmStatus => AlgorithmStatus.Completed;

        //public bool CanRunLocally => true;

        //public List<Language> Languages => [Language.CSharp];

        //public long DataPoints => 1003;

        //public int AlgorithmHistoryDataPoints => 0;

        //public Dictionary<string, string> ExpectedStatistics => throw new NotImplementedException();
    }
}
