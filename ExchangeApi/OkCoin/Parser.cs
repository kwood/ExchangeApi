using Conditions;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public static class Parser
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();
        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // TODO: pupulate this dictionary programmatically. In order to do this, factor
        // out code that assembles channel names. This code can be shared by Parser and Serializer.
        static readonly Dictionary<string, Func<JToken, IMessageIn>> _channelParsers =
            new Dictionary<string, Func<JToken, IMessageIn>>()
            {
                // Spot depth.
                { "ok_btcusd_depth60", (data) => ParseProductDepth(
                    new Spot() { CoinType = CoinType.Btc, Currency = Currency.Usd }, data) },
                { "ok_ltcusd_depth60", (data) => ParseProductDepth(
                    new Spot() { CoinType = CoinType.Ltc, Currency = Currency.Usd }, data) },
                { "ok_btccny_depth60", (data) => ParseProductDepth(
                    new Spot() { CoinType = CoinType.Btc, Currency = Currency.Cny }, data) },
                { "ok_ltccny_depth60", (data) => ParseProductDepth(
                    new Spot() { CoinType = CoinType.Ltc, Currency = Currency.Cny }, data) },

                // Future depth.
                { "ok_btcusd_future_depth_this_week_60", (data) => ParseProductDepth(
                    new Future() { CoinType = CoinType.Btc, Currency = Currency.Usd, FutureType = FutureType.ThisWeek }, data) },
                { "ok_ltcusd_future_depth_this_week_60", (data) => ParseProductDepth(
                    new Future() { CoinType = CoinType.Ltc, Currency = Currency.Usd, FutureType = FutureType.ThisWeek }, data) },
                { "ok_btcusd_future_depth_next_week_60", (data) => ParseProductDepth(
                    new Future() { CoinType = CoinType.Btc, Currency = Currency.Usd, FutureType = FutureType.NextWeek }, data) },
                { "ok_ltcusd_future_depth_next_week_60", (data) => ParseProductDepth(
                    new Future() { CoinType = CoinType.Ltc, Currency = Currency.Usd, FutureType = FutureType.NextWeek }, data) },
                { "ok_btcusd_future_depth_quarter_60", (data) => ParseProductDepth(
                    new Future() { CoinType = CoinType.Btc, Currency = Currency.Usd, FutureType = FutureType.Quarter }, data) },
                { "ok_ltcusd_future_depth_quarter_60", (data) => ParseProductDepth(
                    new Future() { CoinType = CoinType.Ltc, Currency = Currency.Usd, FutureType = FutureType.Quarter }, data) },

                // Spot trades.
                { "ok_btcusd_trades_v1", (data) => ParseProductTrades(
                    new Spot() { CoinType = CoinType.Btc, Currency = Currency.Usd }, data) },
                { "ok_ltcusd_trades_v1", (data) => ParseProductTrades(
                    new Spot() { CoinType = CoinType.Ltc, Currency = Currency.Usd }, data) },
                { "ok_btccny_trades_v1", (data) => ParseProductTrades(
                    new Spot() { CoinType = CoinType.Btc, Currency = Currency.Cny }, data) },
                { "ok_ltccny_trades_v1", (data) => ParseProductTrades(
                    new Spot() { CoinType = CoinType.Ltc, Currency = Currency.Cny }, data) },

                // Future trades.
                { "ok_btcusd_future_trade_v1_this_week", (data) => ParseProductTrades(
                    new Future() { CoinType = CoinType.Btc, Currency = Currency.Usd, FutureType = FutureType.ThisWeek }, data) },
                { "ok_ltcusd_future_trade_v1_this_week", (data) => ParseProductTrades(
                    new Future() { CoinType = CoinType.Ltc, Currency = Currency.Usd, FutureType = FutureType.ThisWeek }, data) },
                { "ok_btcusd_future_trade_v1_next_week", (data) => ParseProductTrades(
                    new Future() { CoinType = CoinType.Btc, Currency = Currency.Usd, FutureType = FutureType.NextWeek }, data) },
                { "ok_ltcusd_future_trade_v1_next_week", (data) => ParseProductTrades(
                    new Future() { CoinType = CoinType.Ltc, Currency = Currency.Usd, FutureType = FutureType.NextWeek }, data) },
                { "ok_btcusd_future_trade_v1_quarter", (data) => ParseProductTrades(
                    new Future() { CoinType = CoinType.Btc, Currency = Currency.Usd, FutureType = FutureType.Quarter }, data) },
                { "ok_ltcusd_future_trade_v1_quarter", (data) => ParseProductTrades(
                    new Future() { CoinType = CoinType.Ltc, Currency = Currency.Usd, FutureType = FutureType.Quarter }, data) },
            };

        public static IEnumerable<IMessageIn> Parse(string serialized)
        {
            // Error (subscribing to inexisting channel "bogus"):
            //   [{"channel":"bogus","errorcode":"10015","success":"false"}]
            // Success (trade notification):
            //   [{ "channel":"ok_btcusd_trades_v1","data":[["78270746","431.3","0.01","22:02:41","ask"]]}]
            //
            // There may be more than one object in the list.

            // TODO: recognize error messages.
            var array = JArray.Parse(serialized);
            Condition.Requires(array, "array").IsNotNull();
            return array.Select(ParseMessage).Where(msg => msg != null).ToArray();
        }

        static IMessageIn ParseMessage(JToken root)
        {
            // If `root` isn't a JSON object, throws.
            // If there is no such field, we get a null.
            var channel = (string)root["channel"];
            if (channel == null)
            {
                _log.Warn("Incoming message without a channel. Ignoring it.");
                return null;
            }
            Func<JToken, IMessageIn> parser;
            if (!_channelParsers.TryGetValue(channel, out parser))
            {
                _log.Warn("Incoming message with unknown `channel`: '{0}'. Ignoring it.", channel);
                return null;
            }
            JToken data = root["data"];
            if (data == null)
            {
                _log.Warn("Incoming message is missing `data` field. Ignoring it.");
                return null;
            }
            return parser.Invoke(data);
        }

        static IMessageIn ParseProductDepth(Product product, JToken data)
        {
            // {
            //   "bids": [[432.76, 3.55],...],
            //   "asks": [[440.01, 3.15],...],
            //   "timestamp":"1451920248246",
            // }
            var res = new ProductDepth()
            {
                Product = product,
                Timestamp = _epoch + TimeSpan.FromMilliseconds((long)data["timestamp"]),
                Orders = new List<Amount>(),
            };
            Action<string, Side> ParseOrders = (field, side) =>
            {
                JArray orders = (JArray)data[field];
                Condition.Requires(orders, "orders").IsNotNull();
                foreach (var pair in orders)
                {
                    res.Orders.Add(new Amount()
                    {
                        Side = side,
                        Price = (decimal)pair[0],
                        Quantity = (decimal)pair[1],
                    });
                }
            };
            ParseOrders("bids", Side.Buy);
            ParseOrders("asks", Side.Sell);
            return res;
        }

        static IMessageIn ParseProductTrades(Product product, JToken data)
        {
            // [["78270746", "431.3", "0.01", "22:02:41", "ask"], ...]
            //
            // Each element is [TradeId, Price, Quantity, Time, Side].
            var res = new ProductTrades()
            {
                Product = product,
                Trades = new List<Trade>(),
            };
            foreach (JToken elem in data)
            {
                res.Trades.Add(new Trade()
                {
                    Id = (long)elem[0],
                    Timestamp = TimeToTimestamp((TimeSpan)elem[3]),
                    Amount = new Amount()
                    {
                        Price = (decimal)elem[1],
                        Quantity = (decimal)elem[2],
                        Side = ParseSide((string)elem[4]),
                    }
                });
            }
            return res;
        }

        static Side ParseSide(string side)
        {
            Condition.Requires(side, "side").IsNotNull();
            if (side == "bid") return Side.Buy;
            if (side == "ask") return Side.Sell;
            throw new ArgumentException("Unknown value of `side`: " + side);
        }

        // Returns the closes timestamp to `baseTimestamp` that has time portion equal to `timeOnly`.
        //
        //   Combine("2015-01-04 23:59", "23:55") => "2015-01-04 23:55"
        //   Combine("2015-01-04 23:59", "00:03") => "2015-01-05 00:03"
        static DateTime Combine(DateTime baseTimestamp, TimeSpan timeOnly)
        {
            return new int[] { -1, 0, 1 }
                .Select(n => baseTimestamp.Date + TimeSpan.FromDays(n) + timeOnly)
                .OrderBy(d => Math.Abs(d.Ticks - baseTimestamp.Ticks))
                .First();
        }

        static DateTime TimeToTimestamp(TimeSpan time)
        {
            // OkCoin sends times in Beijing time zone (UTC+8:00).
            // We convert it to UTC timestamp assuming that it's close to current time.
            // Note that the second argument of Combine() may end up being negative.
            // It's OK because it's not too negative.
            return Combine(DateTime.UtcNow, time - TimeSpan.FromHours(8));
        }
    }
}
