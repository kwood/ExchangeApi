using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.WebSocket
{
    public class Connector : IConnector<ArraySegment<byte>?, ArraySegment<byte>>
    {
        readonly string _endpoint;

        public Connector(string endpoint)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            _endpoint = endpoint;
        }

        public IConnection<ArraySegment<byte>?, ArraySegment<byte>> NewConnection()
        {
            return new Connection(_endpoint);
        }
    }
}
