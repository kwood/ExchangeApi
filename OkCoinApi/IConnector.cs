using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OkCoinApi
{
    public interface IConnector<In, Out>
    {
        // May block for a few seconds but not indefinitely.
        // Must throw or return null if unable to establish a connection.
        // Doesn't need to be thread safe.
        //
        // T.Dispose() may block for a few seconds but not indefinitely.
        // It'll be called exactly once and not concurrently with any other method.
        //
        // DurableConnection<T> guarantees that at most one object of type T is
        // live at any given time. It calls T.Dispose() and waits for its completion
        // before calling NewConnection() to create a new instance.
        IConnection<In, Out> NewConnection();
    }
}
