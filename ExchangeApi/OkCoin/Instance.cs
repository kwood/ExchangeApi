using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
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

        public static Instance OkCoinCom =
            new Instance("wss://real.okcoin.com:10440/websocket/okcoinapi", "https://www.okcoin.com/api/v1/");
        public static Instance OkCoinCn =
            new Instance("wss://real.okcoin.cn:10440/websocket/okcoinapi", "https://www.okcoin.cn/api/v1/");
    }
}
