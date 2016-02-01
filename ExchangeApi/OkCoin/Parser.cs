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
    public class MessageParser : IVisitorIn<IMessageIn>
    {
        readonly JToken _data;

        public MessageParser(JToken data)
        {
            Condition.Requires(data, "data").IsNotNull();
            _data = data;
        }

        public IMessageIn Visit(NewFutureResponse msg)
        {
            throw new NotImplementedException();
        }

        public IMessageIn Visit(ProductTrades msg)
        {
            // [["78270746", "431.3", "0.01", "22:02:41", "ask"], ...]
            //
            // Each element is [TradeId, Price, Quantity, Time, Side].
            msg.Trades = new List<Trade>();
            foreach (JToken elem in _data)
            {
                msg.Trades.Add(new Trade()
                {
                    Id = (long)elem[0],
                    Timestamp = Util.Time.FromDayTime((TimeSpan)elem[3], TimeSpan.FromHours(8)),
                    Amount = new Amount()
                    {
                        Price = (decimal)elem[1],
                        Quantity = (decimal)elem[2],
                        Side = ParseSide((string)elem[4]),
                    }
                });
            }
            return msg;
        }

        public IMessageIn Visit(ProductDepth msg)
        {
            // {
            //   "bids": [[432.76, 3.55],...],
            //   "asks": [[440.01, 3.15],...],
            //   "timestamp":"1451920248246",
            // }
            msg.Timestamp = Util.Time.FromUnixMillis((long)_data["timestamp"]);
            msg.Orders = new List<Amount>();
            Action<string, Side> ParseOrders = (field, side) =>
            {
                JArray orders = (JArray)_data[field];
                Condition.Requires(orders, "orders").IsNotNull();
                foreach (var pair in orders)
                {
                    msg.Orders.Add(new Amount()
                    {
                        Side = side,
                        Price = (decimal)pair[0],
                        Quantity = (decimal)pair[1],
                    });
                }
            };
            ParseOrders("bids", Side.Buy);
            ParseOrders("asks", Side.Sell);
            return msg;
        }

        static Side ParseSide(string side)
        {
            Condition.Requires(side, "side").IsNotNull();
            if (side == "bid") return Side.Buy;
            if (side == "ask") return Side.Sell;
            throw new ArgumentException("Unknown value of `side`: " + side);
        }
    }

    public static class ResponseParser
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Channel name => constructor for the message that prepopulates all fields whose values
        // are determined by the channel name.
        static readonly Dictionary<string, Func<IMessageIn>> _messageCtors =
            new Dictionary<string, Func<IMessageIn>>();

        static ResponseParser()
        {
            Action<Product> Subscribe = (Product product) =>
            {
                _messageCtors.Add(Serialization.SubscribeChannel(product, ChannelType.Depth60),
                                  () => new ProductDepth() { Product = product });
                _messageCtors.Add(Serialization.SubscribeChannel(product, ChannelType.Trades),
                                  () => new ProductTrades() { Product = product });
            };
            foreach (var currency in Util.Enum.Values<Currency>())
            {
                _messageCtors.Add(Serialization.NewFutureChannel(currency),
                                  () => new NewFutureResponse() { Currency = currency });
                foreach (var coin in Util.Enum.Values<CoinType>())
                {
                    Subscribe(new Spot() { Currency = currency, CoinType = coin });
                    foreach (var ft in Util.Enum.Values<FutureType>())
                    {
                        Subscribe(new Future() { Currency = currency, CoinType = coin, FutureType = ft });
                    }
                }
            }
        }

        public static IEnumerable<IMessageIn> Parse(string serialized)
        {
            // Error (subscribing to inexisting channel "bogus"):
            //   [{"channel":"bogus","errorcode":"10015","success":"false"}]
            // Success (trade notification):
            //   [{ "channel":"ok_btcusd_trades_v1","data":[["78270746","431.3","0.01","22:02:41","ask"]]}]
            //
            // There may be more than one object in the list.
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
                _log.Error("Incoming message without a channel. Ignoring it.");
                return null;
            }
            Func<IMessageIn> ctor;
            if (!_messageCtors.TryGetValue(channel, out ctor))
            {
                _log.Error("Incoming message with unknown `channel`: '{0}'. Ignoring it.", channel);
                return null;
            }
            IMessageIn msg = ctor.Invoke();
            string errorcode = (string)root["errorcode"];
            if (errorcode != null)
            {
                msg.Error = (ErrorCode)int.Parse(errorcode);
                return msg;
            }
            JToken data = root["data"];
            if (data == null)
            {
                _log.Error("Incoming message is missing `data` field. Ignoring it.");
                return null;
            }
            try
            {
                return msg.Visit(new MessageParser(data));
            }
            catch (Exception e)
            {
                _log.Error(e, "Unable to parse incoming message. Ignoring it.");
                return null;
            }
        }
    }
}
