using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    public static class Instance
    {
        public static class Prod
        {
            // Web interface: https://exchange.coinbase.com/
            public static readonly string REST = "https://api.exchange.coinbase.com";
            public static readonly string WebSocket = "wss://ws-feed.exchange.coinbase.com";
        }

        public static class Sandbox
        {
            // Web interface: https://public.sandbox.exchange.coinbase.com/.
            public static readonly string REST = "https://api-public.sandbox.exchange.coinbase.com";
            public static readonly string WebSocket = "wss://ws-feed-public.sandbox.exchange.coinbase.com";
        }
    }
}
