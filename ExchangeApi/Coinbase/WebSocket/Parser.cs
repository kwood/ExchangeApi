using Conditions;
using ExchangeApi.Util;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
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

        public IMessageIn Visit(OrderChange msg)
        {
            throw new NotImplementedException();
        }

        public IMessageIn Visit(OrderDone msg)
        {
            throw new NotImplementedException();
        }

        public IMessageIn Visit(OrderMatch msg)
        {
            throw new NotImplementedException();
        }

        public IMessageIn Visit(OrderOpen msg)
        {
            throw new NotImplementedException();
        }

        public IMessageIn Visit(OrderReceived msg)
        {
            throw new NotImplementedException();
        }
    }

    public static class ResponseParser
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public static IMessageIn Parse(string serialized)
        {
            var data = JObject.Parse(serialized);
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
                case "change":
                    res = new OrderChange();
                    break;
                default:
                    throw new ArgumentException("Unexpected message type: " + type);
            }
            return res.Visit(new MessageParser(data));
        }
    }
}
