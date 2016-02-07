using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public static class Channels
    {
        public static string MarketData(Product p, MarketData ch)
        {
            // Representative examples of channel names:
            //   ok_btcusd_trades_v1
            //   ok_btcusd_depth60
            //   ok_btcusd_future_trade_v1_this_week
            //   ok_btcusd_future_depth_this_week_60
            var res = new StringBuilder(64);
            res.Append("ok_");
            res.Append(Serialization.AsString(p.CoinType));
            res.Append(Serialization.AsString(p.Currency));
            res.Append("_");
            switch (p.ProductType)
            {
                case ProductType.Spot:
                    switch (ch)
                    {
                        case OkCoin.MarketData.Depth60: res.Append("depth60"); break;
                        case OkCoin.MarketData.Trades: res.Append("trades_v1"); break;
                    }
                    break;
                case ProductType.Future:
                    res.Append("future_");
                    switch (ch)
                    {
                        case OkCoin.MarketData.Depth60:
                            res.Append("depth_");
                            res.Append(Serialization.AsString(((Future)p).FutureType));
                            res.Append("_60");
                            break;
                        case OkCoin.MarketData.Trades:
                            res.Append("trade_v1_");
                            res.Append(Serialization.AsString(((Future)p).FutureType));
                            break;
                    }
                    break;
            }
            return res.ToString();
        }

        public static string MyOrders(ProductType p, Currency c)
        {
            switch (p)
            {
                case ProductType.Future: return String.Format("ok_{0}_future_realtrades", Serialization.AsString(c));
                case ProductType.Spot: return String.Format("ok_{0}_realtrades", Serialization.AsString(c));
            }
            throw new ArgumentException("Unknown ProductType: " + p);
        }

        public static string NewOrder(ProductType p, Currency c)
        {
            return String.Format("ok_{0}{1}_trade", Serialization.AsString(p), Serialization.AsString(c));
        }

        public static string CancelOrder(ProductType p, Currency c)
        {
            return String.Format("ok_{0}{1}_cancel_order", Serialization.AsString(p), Serialization.AsString(c));
        }

        public static string FromMessage(IMessageIn msg)
        {
            return msg.Visit(new MessageChannel());
        }

        public static string FromMessage(IMessageOut msg)
        {
            return msg.Visit(new MessageChannel());
        }

        class MessageChannel : IVisitorIn<string>, IVisitorOut<string>
        {
            // IVisitorOut

            public string Visit(MarketDataRequest msg)
            {
                return MarketData(msg.Product, msg.MarketData);
            }

            public string Visit(MyOrdersRequest msg)
            {
                return MyOrders(msg.ProductType, msg.Currency);
            }

            public string Visit(NewFutureRequest msg)
            {
                return NewOrder(ProductType.Future, msg.Currency);
            }

            public string Visit(CancelOrderRequest msg)
            {
                return CancelOrder(msg.Product.ProductType, msg.Product.Currency);
            }

            // IVisitorIn

            public string Visit(ProductTrades msg)
            {
                return MarketData(msg.Product, OkCoin.MarketData.Trades);
            }

            public string Visit(ProductDepth msg)
            {
                return MarketData(msg.Product, OkCoin.MarketData.Depth60);
            }

            public string Visit(NewOrderResponse msg)
            {
                return NewOrder(msg.ProductType, msg.Currency);
            }

            public string Visit(CancelOrderResponse msg)
            {
                return CancelOrder(msg.ProductType, msg.Currency);
            }

            public string Visit(MyOrderUpdate msg)
            {
                return MyOrders(msg.ProductType, msg.Currency);
            }
        }
    }
}
