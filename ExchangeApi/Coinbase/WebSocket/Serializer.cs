using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase.WebSocket
{
    public class Serializer : IVisitorOut<string>
    {
        public string Visit(SubscribeRequest msg)
        {
            Condition.Requires(msg.ProductId, "msg.ProductId").IsNotNullOrEmpty();
            return String.Format("{{ \"type\": \"subscribe\", \"product_id\": \"{0}\" }}", msg.ProductId);
        }
    }
}
