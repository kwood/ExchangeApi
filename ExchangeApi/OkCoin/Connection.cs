using StatePrinter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Lock : IDisposable
    {
        public void Dispose()
        {
            // TODO
        }
    }

    public class Connection : IDisposable
    {
        public Connection()
        {
        }

        public event Action OnConnected;
        // No events will fire after OnDisconnected fires until AsyncReconnect is called.
        public event Action<object> OnDisconnected;
        public event Action OnPong;

        public event Action<ProductDepth> OnDepth;
        public event Action<ProductTrades> OnTrades;
        public event Action<Fill> OnFill;
        public event Action<NewOrderResponse> OnNewOrder;

        public void Dispose()
        {
            // TODO
        }

        // Doesn't throw. Fires either OnConnected or OnDisconnected.
        public void AsyncReconnect()
        {
            // TODO
        }

        // The state is passed to OnDisconnected. You can use it to distinguish
        // between spontaneous disconnects, in which case state is null, and manually
        // requested disconnects.
        //
        // If the result is null, we are already disconnected.
        public Lock AsyncDisconnect(object state)
        {
            // TODO
            return null;
        }

        // Returns non-null if the request is sent.
        public Lock Ping()
        {
            // TODO
            return null;
        }

        // Returns non-null if the request is sent.
        public Lock SubscribeToDepths(Product product)
        {
            // TODO
            return null;
        }

        // Returns non-null if the request is sent.
        public Lock SubscribeToTrades(Product product)
        {
            // TODO
            return null;
        }

        // Returns non-null if the request is sent.
        public Lock SubscribeToFills(ProductType type)
        {
            // TODO
            return null;
        }

        // Returns non-null if the request is sent.
        public Lock CreateOrder(NewSpotRequest req)
        {
            // TODO
            return null;
        }

        // Returns non-null if the request is sent.
        public Lock CreateOrder(NewFutureRequest req)
        {
            // TODO
            return null;
        }

        // Returns non-null if the request is sent.
        public Lock CancelOrder(CancelOrderRequest req)
        {
            return null;
        }

        // TODO: Add a method for querying assets.

        // TODO: Add a method for querying open orders.
        // Futures API definitely has a was to query all open orders.
        // Spot API seems to allow only querying by order ID.
    }
}
