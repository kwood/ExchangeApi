using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    public class Instance
    {
        readonly string _websocket;
        readonly string _rest;

        public Instance(string websocket, string rest)
        {
            Condition.Requires(websocket, "websocket").IsNotNullOrEmpty();
            Condition.Requires(rest, "rest").IsNotNullOrEmpty();
            _websocket = websocket;
            _rest = rest;
        }

        public string WebSocket { get { return _websocket; } }
        public string REST { get { return _rest; } }

        // Web interface: https://exchange.coinbase.com/.
        public static Instance Prod = new Instance(
            "wss://ws-feed.gdax.com", "https://api.gdax.com");

        // Web interface: https://public.sandbox.exchange.coinbase.com/.
        public static Instance Sandbox = new Instance(
            "wss://ws-feed-public.sandbox.gdax.com", "https://api-public.sandbox.gdax.com");
    }
}
