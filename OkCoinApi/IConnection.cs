using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OkCoinApi
{
    // A thread-safe bidirectional stream of serialized messages.
    public interface IConnection<In, Out> : IDisposable
    {
        // Initially false. Becomes true as the result of the call to Connect() (even if
        // it throws). Calling Dispose() makes it false.
        bool Connected { get; }

        // While the event handler is running, nothing is getting read from the
        // stream.
        //
        // On read error this event is raised with null as the argument.
        // However, the event isn't raised if the error happened after the first
        // call to Dispose(). If OnMessage(null) is invoked, it's the last raised
        // event.
        //
        // Won't be fired until Connect() is called.
        event Action<In> OnMessage;

        // Must be called at most once. Blocks. Throws on error, in which case you
        // must call Dispose().
        void Connect();

        // Blocks. Throws on error.
        void Send(Out message);
    }
}
