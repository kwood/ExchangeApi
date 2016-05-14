using Conditions;
using ExchangeApi.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase.WebSocket
{
    public class MessageParser : IVisitorIn<IMessageIn>
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly JToken _data;

        public MessageParser(JToken data)
        {
            Condition.Requires(data, "data").IsNotNull();
            _data = data;
        }

        public void ParseCommon(IMessageIn msg)
        {
            msg.Time = (DateTime)_data["time"];
            msg.ProductId = (string)_data["product_id"];
            msg.Sequence = (long)_data["sequence"];
            msg.Side = ParseSide((string)_data["side"]);
        }

        public IMessageIn Visit(OrderReceived msg)
        {
            msg.OrderId = (string)_data["order_id"];
            msg.Size = (decimal?)_data["size"];
            msg.Price = (decimal?)_data["price"];
            msg.Funds = (decimal?)_data["funds"];
            msg.OrderType = ParseOrderType((string)_data["order_type"]);
            msg.ClientOrderId = (string)_data["client_oid"];
            return msg;
        }

        public IMessageIn Visit(OrderOpen msg)
        {
            msg.OrderId = (string)_data["order_id"];
            msg.Price = (decimal)_data["price"];
            msg.RemainingSize = (decimal)_data["remaining_size"];
            return msg;
        }

        public IMessageIn Visit(OrderDone msg)
        {
            msg.OrderId = (string)_data["order_id"];
            msg.Price = (decimal?)_data["price"];
            msg.RemainingSize = (decimal?)_data["remaining_size"];
            msg.Reason = ParseDoneReason((string)_data["reason"]);
            msg.OrderType = ParseOrderType((string)_data["order_type"]);
            return msg;
        }

        public IMessageIn Visit(OrderMatch msg)
        {
            msg.TradeId = (long)_data["trade_id"];
            msg.MakerOrderId = (string)_data["maker_order_id"];
            msg.TakerOrderId = (string)_data["taker_order_id"];
            msg.Price = (decimal)_data["price"];
            msg.Size = (decimal)_data["size"];
            return msg;
        }

        public IMessageIn Visit(OrderChange msg)
        {
            msg.OrderId = (string)_data["order_id"];
            msg.Price = (decimal?)_data["price"];
            msg.NewSize = (decimal?)_data["new_size"];
            msg.OldSize = (decimal?)_data["old_size"];
            msg.NewFunds = (decimal?)_data["new_funds"];
            msg.OldFunds = (decimal?)_data["old_funds"];
            return msg;
        }

        static Side ParseSide(string s)
        {
            if (s == "buy") return Side.Buy;
            if (s == "sell") return Side.Sell;
            throw new ArgumentException("Unable to parse side: " + s);
        }

        static OrderType ParseOrderType(string s)
        {
            if (s == "limit") return OrderType.Limit;
            if (s == "market") return OrderType.Market;
            throw new ArgumentException("Unable to parse order type: " + s);
        }

        static DoneReason ParseDoneReason(string s)
        {
            if (s == "canceled") return DoneReason.Canceled;
            if (s == "filled") return DoneReason.Filled;
            throw new ArgumentException("Unable to parse done reason: " + s);
        }
    }

    public static class ResponseParser
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public static IMessageIn Parse(string serialized)
        {
            var data = Json.ParseObject(serialized);
            Condition.Requires(data, "data").IsNotNull();
            string type = (string)data["type"];
            Condition.Requires(type, "type").IsNotNull();
            IMessageIn res = null;
            switch (type)
            {
                case "received":
                    res = new OrderReceived();
                    break;
                case "open":
                    res = new OrderOpen();
                    break;
                case "done":
                    res = new OrderDone();
                    break;
                case "match":
                    res = new OrderMatch();
                    break;
                case "change":
                    res = new OrderChange();
                    break;
                default:
                    throw new ArgumentException("Unexpected message type: " + type);
            }
            var parser = new MessageParser(data);
            parser.ParseCommon(res);
            return res.Visit(parser);
        }
    }
}
