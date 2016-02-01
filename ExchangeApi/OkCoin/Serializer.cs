using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using KV = System.Collections.Generic.KeyValuePair<string, string>;

namespace ExchangeApi.OkCoin
{
    public class Serializer : IVisitorOut<string>
    {
        // Not null.
        readonly Keys _keys;

        public Serializer(Keys keys)
        {
            Condition.Requires(keys, "keys").IsNotNull();
            _keys = keys;
        }

        public string Visit(SubscribeRequest msg)
        {
            // Example: {"event":"addChannel","channel":"ok_btcusd_future_depth_this_week_60"}.
            return String.Format("{{\"event\":\"addChannel\",\"channel\":\"{0}\"}}",
                                 Serialization.SubscribeChannel(msg.Product, msg.MarketData));
        }

        public string Visit(NewFutureRequest msg)
        {
            var param = new List<KV>(10);
            param.Add(new KV("api_key", _keys.ApiKey));
            param.Add(new KV("contract_type", Serialization.AsString(msg.FutureType)));
            param.Add(new KV("amount", Serialization.AsString(msg.Amount.Quantity)));
            param.Add(new KV("type", Serialization.AsString(msg.Amount.Side, msg.PositionType)));
            param.Add(new KV("lever_rate", Serialization.AsString(msg.Leverage)));
            param.Add(new KV(
                "symbol", String.Format("{0}_{1}", Serialization.AsString(msg.CoinType), Serialization.AsString(msg.Currency))));
            if (msg.OrderType == OrderType.Limit)
            {
                param.Add(new KV("price", Serialization.AsString(msg.Amount.Price)));
                param.Add(new KV("match_price", "0"));
            }
            else
            {
                param.Add(new KV("match_price", "1"));
            }
            // Signature is added last because its value depends on all other parameters.
            param.Add(new KV("sign", Authenticator.Sign(_keys, param)));

            string parameters = String.Join(",", param.Select(kv => String.Format("\"{0}\":\"{1}\"", kv.Key, kv.Value)));
            return String.Format("{{\"event\":\"addChannel\",\"channel\":\"{0}\",\"parameters\":{{{1}}}}}",
                                 Serialization.NewOrderChannel(ProductType.Future, msg.Currency), parameters);
        }
    }
}
