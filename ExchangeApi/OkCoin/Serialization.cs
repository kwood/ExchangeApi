using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public static class Serialization
    {
        public static string SubscribeChannel(Product product, MarketData ch)
        {
            // Representative examples of channel names:
            //   ok_btcusd_trades_v1
            //   ok_btcusd_depth60
            //   ok_btcusd_future_trade_v1_this_week
            //   ok_btcusd_future_depth_this_week_60
            var res = new StringBuilder(64);
            res.Append("ok_");
            res.Append(AsString(product.CoinType));
            res.Append(AsString(product.Currency));
            res.Append("_");
            switch (product.ProductType)
            {
                case ProductType.Spot:
                    switch (ch)
                    {
                        case MarketData.Depth60: res.Append("depth60"); break;
                        case MarketData.Trades: res.Append("trades_v1"); break;
                    }
                    break;
                case ProductType.Future:
                    res.Append("future_");
                    switch (ch)
                    {
                        case MarketData.Depth60:
                            res.Append("depth_");
                            res.Append(AsString(((Future)product).FutureType));
                            res.Append("_60");
                            break;
                        case MarketData.Trades:
                            res.Append("trade_v1_");
                            res.Append(AsString(((Future)product).FutureType));
                            break;
                    }
                    break;
            }
            return res.ToString();
        }

        public static string NewOrderChannel(ProductType p, Currency currency)
        {
            return String.Format("ok_{0}{1}_trade", AsString(p), AsString(currency));
        }

        public static string AsString(ProductType p)
        {
            switch (p)
            {
                case ProductType.Spot: return "spot";
                case ProductType.Future: return "futures";
            }
            throw new ArgumentException("Unknown ProductType: " + p);
        }

        public static string AsString(FutureType f)
        {
            switch (f)
            {
                case FutureType.ThisWeek: return "this_week";
                case FutureType.NextWeek: return "next_week";
                case FutureType.Quarter: return "quarter";
            }
            throw new ArgumentException("Unknown FutureType: " + f);
        }

        public static string AsString(CoinType t)
        {
            switch (t)
            {
                case CoinType.Btc: return "btc";
                case CoinType.Ltc: return "ltc";
            }
            throw new ArgumentException("Unknown CoinType: " + t);
        }

        public static string AsString(Currency c)
        {
            switch (c)
            {
                case Currency.Usd: return "usd";
                case Currency.Cny: return "cny";
            }
            throw new ArgumentException("Unknown Currency: " + c);
        }

        // Formats decimal without trailing zeros.
        //
        //   1.0m => "1"
        //   1.1m => "1.1"
        public static string AsString(decimal d)
        {
            // There are 28 zeros, corresponding to the decimal's maximum exponent of 10^28.
            return (d / 1.0000000000000000000000000000m).ToString();
        }

        public static string AsString(Leverage lever)
        {
            switch (lever)
            {
                case Leverage.x10: return "10";
                case Leverage.x20: return "20";
            }
            throw new ArgumentException("Unknown Leverage: " + lever);
        }

        public static string AsString(Side side, PositionType pos)
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
