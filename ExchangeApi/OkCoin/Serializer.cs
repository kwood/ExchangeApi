using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Serializer : IVisitorOut<string>
    {
        public string Visit(SubscribeRequest msg)
        {
            // SubscribeRequest looks like this: {'event':'addChannel','channel':'${channel}'}.
            //
            // Representative examples of channel names:
            //   ok_btcusd_trades_v1
            //   ok_btcusd_depth60
            //   ok_btcusd_future_trade_v1_this_week
            //   ok_btcusd_future_depth_this_week_60
            var res = new StringBuilder(100);
            res.Append("{'event':'addChannel','channel':'ok_");
            switch (msg.Product.CoinType)
            {
                case CoinType.Btc: res.Append("btc"); break;
                case CoinType.Ltc: res.Append("ltc"); break;
            }
            switch (msg.Product.Currency)
            {
                case Currency.Usd: res.Append("usd"); break;
                case Currency.Cny: res.Append("cny"); break;
            }
            res.Append("_");
            switch (msg.Product.ProductType)
            {
                case ProductType.Spot:
                    switch (msg.ChannelType)
                    {
                        case ChanelType.Depth60: res.Append("depth60"); break;
                        case ChanelType.Trades: res.Append("trades_v1"); break;
                    }
                    break;
                case ProductType.Future:
                    res.Append("future_");
                    switch (msg.ChannelType)
                    {
                        case ChanelType.Depth60:
                            res.Append("depth_");
                            res.Append(AsString(((Future)msg.Product).FutureType));
                            res.Append("_60");
                            break;
                        case ChanelType.Trades:
                            res.Append("trade_v1_");
                            res.Append(AsString(((Future)msg.Product).FutureType));
                            break;
                    }
                    break;
            }
            res.Append("'}");
            return res.ToString();
        }

        static string AsString(FutureType f)
        {
            switch (f)
            {
                case FutureType.ThisWeek: return "this_week";
                case FutureType.NextWeek: return "next_week";
                case FutureType.Quarter: return "quarter";
            }
            throw new ArgumentException("Unknown FutureType: " + f);
        }
    }
}
