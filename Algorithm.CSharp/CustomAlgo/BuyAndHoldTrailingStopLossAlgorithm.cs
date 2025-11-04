using QuantConnect.Data;
using QuantConnect.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp.CustomAlgo
{
    public class BuyAndHoldTrailingStopLossAlgorithm : QCAlgorithm
    {
        //Order ticket for our stop order, Datetime when stop order was last hit
        private OrderTicket stopMarketTicket;
        private DateTime stopMarketOrderFilled;
        private decimal highestSPYPrice;

        public override void Initialize()
        {
            SetStartDate(2018, 12, 1);
            SetEndDate(2018, 12, 10);
            SetCash(100000);
            AddEquity("SPY", Resolution.Daily, dataNormalizationMode: DataNormalizationMode.Raw);
        }

        public override void OnData(Slice slice)
        {
            //If we hit our stop within the last 15 days, do nothing
            if ((Time - stopMarketOrderFilled).Days < 15)
                return;

            if (!Portfolio.Invested)
            {
                MarketOrder("SPY", 500);
                highestSPYPrice = Securities["SPY"].Close;
                // Set initial stop price at 90% of purchase price
                stopMarketTicket = StopMarketOrder("SPY", -500, highestSPYPrice * 0.9m);

            }
            else
            {

                //1. Check if the SPY price is higher that highestSPYPrice.
                if (Securities["SPY"].Close > highestSPYPrice)
                {
                    //2. Save the new high to highestSPYPrice; then update the stop price to 90% of highestSPYPrice 
                    highestSPYPrice = Securities["SPY"].Close;
                    var stopPrice = 0.9m * highestSPYPrice;
                    stopMarketTicket.Update(new UpdateOrderFields
                    {
                        StopPrice = stopPrice
                    });
                    //3. Print the new stop price with Debug()
                    Debug($"New stop price: {stopPrice}");
                }
            }
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            //Only act on fills (ignore submits)
            if (orderEvent.Status != OrderStatus.Filled)
                return;

            //Check if we hit our stop loss
            if (stopMarketTicket != null && orderEvent.OrderId == stopMarketTicket.OrderId)
            {
                stopMarketOrderFilled = Time;
            }
        }
    }
}
