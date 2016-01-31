using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Serializer : IVisitorOut<string>
    {
        // Not null.
        readonly Keys _keys;

        public Serializer(Keys keys)
        {
            Condition.Requires(keys, "keys").IsNotNull();
            _keys = keys;
        }

        public string Visit(SubscribeRequest msg)
        {
            // SubscribeRequest looks like this: {"event":"addChannel","channel":"${channel}"}.
            //
            // Representative examples of channel names:
            //   ok_btcusd_trades_v1
            //   ok_btcusd_depth60
            //   ok_btcusd_future_trade_v1_this_week
            //   ok_btcusd_future_depth_this_week_60
            var res = new StringBuilder(100);
            res.Append("{\"event\":\"addChannel\",\"channel\":\"ok_");
            res.Append(AsString(msg.Product.CoinType));
            res.Append(AsString(msg.Product.Currency));
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
            res.Append("\"}");
            return res.ToString();
        }

        public string Visit(NewFutureRequest msg)
        {
            var param = new List<KeyValuePair<string, string>>(10);
            param.Add(new KeyValuePair<string, string>("api_key", _keys.ApiKey));
            param.Add(new KeyValuePair<string, string>("contract_type", AsString(msg.FutureType)));
            param.Add(new KeyValuePair<string, string>("amount", AsString(msg.Amount.Quantity)));
            param.Add(new KeyValuePair<string, string>("type", AsString(msg.Amount.Side, msg.PositionType)));
            param.Add(new KeyValuePair<string, string>("lever_rate", AsString(msg.Levarage)));
            param.Add(new KeyValuePair<string, string>(
                "symbol", String.Format("{0}_{1}", AsString(msg.CoinType), AsString(msg.Currency))));
            if (msg.OrderType == OrderType.Limit)
            {
                param.Add(new KeyValuePair<string, string>("price", AsString(msg.Amount.Price)));
                param.Add(new KeyValuePair<string, string>("match_price", "0"));
            }
            else
            {
                param.Add(new KeyValuePair<string, string>("match_price", "1"));
            }
            // Signature is added last because its value depends on all other parameters.
            param.Add(new KeyValuePair<string, string>("sign", Authenticator.Sign(_keys, param)));

            string channel = String.Format("ok_futures{0}_trade", AsString(msg.Currency));
            string parameters = String.Join(",", param.Select(kv => String.Format("\"{0}\":\"{1}\"", kv.Key, kv.Value)));
            return String.Format("{{\"event\":\"addChannel\",\"channel\":\"{0}\",\"parameters\":{{{1}}}}}",
                                 channel, parameters);
        }

        // Formats decimal without trailing zeros.
        //
        //   1.0m => "1"
        //   1.1m => "1.1"
        static string AsString(decimal d)
        {
            // There are 28 zeros, corresponding to the decimal's maximum exponent of 10^28.
            return (d / 1.0000000000000000000000000000m).ToString();
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

        static string AsString(CoinType t)
        {
            switch (t)
            {
                case CoinType.Btc: return "btc";
                case CoinType.Ltc: return "ltc";
            }
            throw new ArgumentException("Unknown CoinType: " + t);
        }

        static string AsString(Currency c)
        {
            switch (c)
            {
                case Currency.Usd: return "usd";
                case Currency.Cny: return "cny";
            }
            throw new ArgumentException("Unknown Currency: " + c);
        }

        static string AsString(Leverage lever)
        {
            switch (lever)
            {
                case Leverage.x10: return "10";
                case Leverage.x20: return "20";
            }
            throw new ArgumentException("Unknown Leverage: " + lever);
        }

        static string AsString(Side side, PositionType pos)
        {
            if (side == Side.Buy)
            {
                // Open long position.
                if (pos == PositionType.Long) return "1";
                // Open short position.
                if (pos == PositionType.Short) return "2";
            }
            if (side == Side.Sell)
            {
                // Liquidate long position.
                if (pos == PositionType.Long) return "3";
                // Liquidate short position.
                if (pos == PositionType.Short) return "4";
            }
            throw new ArgumentException(String.Format("Invalid enums: {0}, {1}", side, pos));
        }
    }
}
