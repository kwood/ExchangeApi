using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OkCoinApi
{
    public class Connector : IConnector<ActiveSocket>
    {
        readonly string _endpoint;
        readonly Action<ActiveSocket> _initializer;

        public Connector(string endpoint, Action<ActiveSocket> initializer)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            _endpoint = endpoint;
            _initializer = initializer;
        }

        public ActiveSocket NewConnection()
        {
            var res = new ActiveSocket();
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
