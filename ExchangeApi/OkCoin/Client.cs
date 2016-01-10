using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Client : DurableConnection<IMessageIn, IMessageOut>
    {
        public Client(string endpoint)
            : base(new CodingConnector<IMessageIn, IMessageOut>(
                       new WebSocket.Connector(Instance.OkCoinCom), new Codec()))
        { }
    }
}
