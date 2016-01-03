using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OkCoinApi
{
    public class Connector : IConnector<WebSocket>
    {
        readonly string _endpoint;
        readonly Action<WebSocket> _initializer;

        public Connector(string endpoint, Action<WebSocket> initializer)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            _endpoint = endpoint;
            _initializer = initializer;
        }

        public WebSocket NewConnection()
        {
            var res = new WebSocket();
            res.Connect(_endpoint);
            if (_initializer != null)
            {
                try
                {
                    _initializer.Invoke(res);
                }
                catch
                {
                    res.Dispose();
                    throw;
                }
            }
            return res;
        }
    }
}
