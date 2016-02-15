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
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly JToken _data;

        public MessageParser(JToken data)
        {
            _data = data;
        }

        public IMessageIn Visit(NewOrderResponse msg)
        {
            Condition.Requires(_data, "data").IsNotNull();
            // {"order_id":"1476459990","result":"true"}
            if ((string)_data["result"] != "true")
            {
                _log.Error("Unexpected response to new future request. Ignoring it.");
                return null;
            }
            msg.OrderId = (long)_data["order_id"];
            return msg;
        }

        public IMessageIn Visit(CancelOrderResponse msg)
        {
            Condition.Requires(_data, "data").IsNotNull();
            // {"order_id":"1476459990","result":"true"}
            if ((string)_data["result"] != "true")
            {
                _log.Error("Unexpected response to new future request. Ignoring it.");
                return null;
            }
            msg.OrderId = (long)_data["order_id"];
            return msg;
        }

        public IMessageIn Visit(ProductTrades msg)
        {
            Condition.Requires(_data, "data").IsNotNull();
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
            Condition.Requires(_data, "data").IsNotNull();
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

        public IMessageIn Visit(MyOrderUpdate msg)
        {
            if (_data == null)
            {
                // OkCoin sends an empty message without data in response to
                // our subscription request.
                return msg;
            }
            switch (msg.ProductType)
            {
                case ProductType.Future:
                    msg.Order = ParseFutureState(_data, msg.Currency);
                    break;
                case ProductType.Spot:
                    msg.Order = ParseSpotState(_data, msg.Currency);
                    break;
                default:
                    throw new ArgumentException("Invalid ProductType: " + msg.ProductType);
            }
            return msg;
        }

        public IMessageIn Visit(FuturePositionsUpdate msg)
        {
            // "symbol": "btc_usd",
            // "positions": [
            //   {
            //     "contract_id": "20160219013",
            //     "contract_name": "BTC0219",
            //     "avgprice": "413.15435805",
            //     "balance": "0.04855328",
            //     "bondfreez": "0",
            //     "costprice": "413.15435805",
            //     "eveningup": "2",
            //     "forcedprice": "379.0405725",
            //     "position": "1",
            //     "profitreal": "-1.4522E-4",
            //     "fixmargin": "0.04840806",
            //     "hold_amount": "2",
            //     "lever_rate": "10",
            //     "position_id": "9018065"
            //   },
            // ]
            if (_data == null)
            {
                // OkCoin sends an empty message without data in response to
                // our subscription request.
                return msg;
            }

            string symbol = (string)_data["symbol"];
            string[] parts = symbol.Split(new char[] { '_' }, 2);
            Condition.Requires(parts, "parts").HasLength(2);
            msg.CoinType = ParseCoinType(parts[0]);
            msg.Currency = ParseCurrency(parts[1]);

            msg.Positions = new List<FuturePosition>();
            foreach (JToken elem in _data["positions"])
            {
                msg.Positions.Add(new FuturePosition()
                {
                    Leverage = ParseLeverage((string)elem["lever_rate"]),
                    PositionType = ParsePositionType((string)elem["position"]),
                    ContractId = (string)elem["contract_id"],
                    Quantity = (decimal)elem["hold_amount"],
                    AvgPrice = (decimal)elem["avgprice"],
                });
            }
            return msg;
        }

        static FutureState ParseFutureState(JToken data, Currency currency)
        {
            // {
            //   "amount":"1",
            //   "contract_id":"20160212034",
            //   "contract_name":"BTC0212",
            //   "contract_type":"this_week",
            //   "create_date":"1454855333918",
            //   "create_date_str":"2016-02-07 22:28:53",
            //   "deal_amount":"0",
            //   "fee":"0",
            //   "lever_rate":"10",
            //   "orderid":1502057218,
            //   "price":"375",
            //   "price_avg":"0",
            //   "status":"0",
            //   "type":"1",
            //   "unit_amount":"100"
            // }
            var res = new FutureState()
            {
                Timestamp = Util.Time.FromUnixMillis((long)data["create_date"]),
                OrderId = (long)data["orderid"],
                OrderStatus = ParseOrderStatus((int)data["status"]),
                Product = new Future()
                {
                    Currency = currency,
                    FutureType = ParseFutureType((string)data["contract_type"]),
                },
                Amount = new Amount()
                {
                    Price = (decimal)data["price"],
                    Quantity = (decimal)data["amount"],
                },
                CumFillQuantity = (decimal)data["deal_amount"],
                AvgFillPrice = (decimal)data["price_avg"],
                Fee = (decimal)data["fee"],
                ContractId = (string)data["contract_id"],
            };

            // Infer CoinType from "contract_name". E.g., "BTC0212" => CoinType.Btc.
            string contract = (string)data["contract_name"];
            Condition.Requires(contract, "contract").IsNotNullOrEmpty();
            if (contract.StartsWith("BTC")) res.Product.CoinType = CoinType.Btc;
            else if (contract.StartsWith("LTC")) res.Product.CoinType = CoinType.Ltc;
            else throw new ArgumentException("Unknown value of `contract_name`: " + contract);

            // Decompose "type" into Side and PositionType.
            int type = (int)data["type"];
            switch (type)
            {
                case 1:
                    res.Amount.Side = Side.Buy;
                    res.PositionType = PositionType.Long;
                    break;
                case 2:
                    res.Amount.Side = Side.Buy;
                    res.PositionType = PositionType.Short;
                    break;
                case 3:
                    res.Amount.Side = Side.Sell;
                    res.PositionType = PositionType.Long;
                    break;
                case 4:
                    res.Amount.Side = Side.Sell;
                    res.PositionType = PositionType.Short;
                    break;
                default:
                    throw new ArgumentException("Unknown `type`: " + type);
            }
            return res;
        }

        static SpotState ParseSpotState(JToken data, Currency currency)
        {
            // TODO: implement me.
            return null;
        }

        static Side ParseSide(string side)
        {
            Condition.Requires(side, "side").IsNotNull();
            if (side == "bid") return Side.Buy;
            if (side == "ask") return Side.Sell;
            throw new ArgumentException("Unknown value of `side`: " + side);
        }

        static FutureType ParseFutureType(string futureType)
        {
            Condition.Requires(futureType, "futureType").IsNotNull();
            if (futureType == "this_week") return FutureType.ThisWeek;
            if (futureType == "next_week") return FutureType.NextWeek;
            if (futureType == "quarter") return FutureType.Quarter;
            throw new ArgumentException("Unknown value of `futureType`: " + futureType);
        }

        static OrderStatus ParseOrderStatus(int status)
        {
            switch (status)
            {
                case -1: return OrderStatus.Cancelled;
                case 0: return OrderStatus.Unfilled;
                case 1: return OrderStatus.PartiallyFilled;
                case 2: return OrderStatus.FullyFilled;
                case 3: return OrderStatus.Cancelling;
                default: throw new ArgumentException("Unknown value of `status`: " + status);
            }
        }

        static Currency ParseCurrency(string currency)
        {
            Condition.Requires(currency, "currency").IsNotNull();
            if (currency == "usd") return Currency.Usd;
            if (currency == "cny") return Currency.Cny;
            throw new ArgumentException("Unknown value of `currency`: " + currency);
        }

        static CoinType ParseCoinType(string coin)
        {
            Condition.Requires(coin, "coin").IsNotNull();
            if (coin == "btc") return CoinType.Btc;
            if (coin == "ltc") return CoinType.Ltc;
            throw new ArgumentException("Unknown value of `coin`: " + coin);
        }

        static Leverage ParseLeverage(string leverage)
        {
            Condition.Requires(leverage, "leverage").IsNotNull();
            if (leverage == "10") return Leverage.x10;
            if (leverage == "20") return Leverage.x20;
            throw new ArgumentException("Unknown value of `leverage`: " + leverage);
        }

        static PositionType ParsePositionType(string position)
        {
            Condition.Requires(position, "position").IsNotNull();
            if (position == "1") return PositionType.Long;
            if (position == "2") return PositionType.Short;
            throw new ArgumentException("Unknown value of `position`: " + position);
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
            _messageCtors.Add(Channels.FuturePositions(), () => new FuturePositionsUpdate() { });
            Action<Product> Subscribe = (Product product) =>
            {
                _messageCtors.Add(Channels.MarketData(product, MarketData.Depth60),
                                  () => new ProductDepth() { Product = product.Clone() });
                _messageCtors.Add(Channels.MarketData(product, MarketData.Trades),
                                  () => new ProductTrades() { Product = product.Clone() });
            };
            foreach (var currency in Util.Enum.Values<Currency>())
            {
                foreach (var product in Util.Enum.Values<ProductType>())
                {
                    _messageCtors.Add(Channels.NewOrder(product, currency),
                                      () => new NewOrderResponse() { ProductType = product, Currency = currency });
                    _messageCtors.Add(Channels.CancelOrder(product, currency),
                                      () => new CancelOrderResponse() { ProductType = product, Currency = currency });
                    _messageCtors.Add(Channels.MyOrders(product, currency),
                                      () => new MyOrderUpdate() { ProductType = product, Currency = currency });
                }
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
            // Note that data can be null. It expected for some messages.
            // MessageParser will throw if it doesn't like null data.
            JToken data = root["data"];
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
