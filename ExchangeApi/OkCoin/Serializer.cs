using Conditions;
using ExchangeApi.Util;
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

        public string Visit(MarketDataRequest msg)
        {
            return UnauthenticatedRequest(msg);
        }

        public string Visit(MyOrdersRequest msg)
        {
            return AuthenticatedRequest(msg, null);
        }

        public string Visit(NewFutureRequest msg)
        {
            IEnumerable<KV> param = new KV[]
            {
                new KV("contract_type", Serialization.AsString(msg.Product.FutureType)),
                new KV("amount", Serialization.AsString(msg.Amount.Quantity)),
                new KV("type", Serialization.AsString(msg.Amount.Side, msg.PositionType)),
                new KV("lever_rate", Serialization.AsString(msg.Leverage)),
                new KV("symbol", Serialization.AsString(msg.Product.CoinType, msg.Product.Currency)),
            };
            if (msg.OrderType == OrderType.Limit)
            {
                param = param.Append(new KV("price", Serialization.AsString(msg.Amount.Price)))
                             .Append(new KV("match_price", "0"));
            }
            else
            {
                param = param.Append(new KV("match_price", "1"));
            }
            return AuthenticatedRequest(msg, param);
        }

        public string Visit(CancelOrderRequest msg)
        {
            IEnumerable<KV> param = new KV[]
            {
                new KV("order_id", msg.OrderId.ToString()),
                new KV("symbol", Serialization.AsString(msg.Product.CoinType, msg.Product.Currency)),
            };
            var future = msg.Product as Future;
            if (future != null)
            {
                param = param.Append(new KV("contract_type", Serialization.AsString(future.FutureType)));
            }
            return AuthenticatedRequest(msg, param);
        }

        public string Visit(FuturePositionsRequest msg)
        {
            return AuthenticatedRequest(msg, null);
        }

        public string Visit(PingRequest msg)
        {
            return "{\"event\":\"ping\"}";
        }

        string UnauthenticatedRequest(IMessageOut msg)
        {
            // Example: {"event":"addChannel","channel":"ok_btcusd_future_depth_this_week_60"}.
            return String.Format("{{\"event\":\"addChannel\",\"channel\":\"{0}\"}}",
                                 Channels.FromMessage(msg));
        }

        // `param` may be null (means empty).
        string AuthenticatedRequest(IMessageOut msg, IEnumerable<KV> param)
        {
            param = param ?? new KV[0];
            param = param.Append(new KV("api_key", _keys.ApiKey));
            // Signature is added last because its value depends on all other parameters.
            param = param.Append(new KV("sign", Authenticator.Sign(_keys, param)));
            string parameters = String.Join(",", param.Select(kv => String.Format("\"{0}\":\"{1}\"", kv.Key, kv.Value)));
            return String.Format("{{\"event\":\"addChannel\",\"channel\":\"{0}\",\"parameters\":{{{1}}}}}",
                                 Channels.FromMessage(msg), parameters);
        }
    }
}
