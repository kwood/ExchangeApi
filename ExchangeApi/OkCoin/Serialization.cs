using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public static class Serialization
    {
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

        public static string AsString(CoinType coin, Currency currency)
        {
            // "btc_usd"
            return String.Format("{0}_{1}", AsString(coin), AsString(currency));
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

        public static Leverage ParseLeverage(string leverage)
        {
            Condition.Requires(leverage, "leverage").IsNotNull();
            if (leverage == "10") return Leverage.x10;
            if (leverage == "20") return Leverage.x20;
            throw new ArgumentException("Unknown value of `leverage`: " + leverage);
        }

        public static Side ParseSide(string side)
        {
            Condition.Requires(side, "side").IsNotNull();
            if (side == "bid") return Side.Buy;
            if (side == "ask") return Side.Sell;
            throw new ArgumentException("Unknown value of `side`: " + side);
        }

        public static FutureType ParseFutureType(string futureType)
        {
            Condition.Requires(futureType, "futureType").IsNotNull();
            if (futureType == "this_week") return FutureType.ThisWeek;
            if (futureType == "next_week") return FutureType.NextWeek;
            if (futureType == "quarter") return FutureType.Quarter;
            throw new ArgumentException("Unknown value of `futureType`: " + futureType);
        }

        public static OrderStatus ParseOrderStatus(int status)
        {
            switch (status)
            {
                case -1: return OrderStatus.Cancelled;
                case 0: return OrderStatus.Unfilled;
                case 1: return OrderStatus.PartiallyFilled;
                case 2: return OrderStatus.FullyFilled;
                case 3:  // For futures.
                case 4:  // For spots.
                    return OrderStatus.Cancelling;
                default: throw new ArgumentException("Unknown value of `status`: " + status);
            }
        }

        public static Currency ParseCurrency(string currency)
        {
            Condition.Requires(currency, "currency").IsNotNull();
            if (currency == "usd") return Currency.Usd;
            if (currency == "cny") return Currency.Cny;
            throw new ArgumentException("Unknown value of `currency`: " + currency);
        }

        public static CoinType ParseCoinType(string coin)
        {
            Condition.Requires(coin, "coin").IsNotNull();
            if (coin == "btc") return CoinType.Btc;
            if (coin == "ltc") return CoinType.Ltc;
            throw new ArgumentException("Unknown value of `coin`: " + coin);
        }

        public static PositionType ParsePositionType(string position)
        {
            Condition.Requires(position, "position").IsNotNull();
            if (position == "1") return PositionType.Long;
            if (position == "2") return PositionType.Short;
            throw new ArgumentException("Unknown value of `position`: " + position);
        }
    }
}
