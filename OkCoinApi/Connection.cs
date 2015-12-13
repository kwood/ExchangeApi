using StatePrinter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OkCoinApi
{
    public enum Currency
    {
        Usd,
        Cny,
    }

    public enum CoinType
    {
        Btc,
        Ltc,
    }

    public enum FutureType
    {
        ThisWeek,
        NextWeek,
        Quarter,
    }

    public enum Side
    {
        Buy = 1,
        Sell = -1,
    }

    public class Amount : Printable<Amount>
    {
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public Side Side { get; set; }
    }

    public enum ProductType
    {
        Spot,
        Future,
    }

    public interface Product
    {
        ProductType ProductType { get; }
        CoinType CoinType { get; set; }
    }

    public class Spot : Printable<Spot>, Product
    {
        public ProductType ProductType { get { return ProductType.Spot; } }
        public CoinType CoinType { get; set; }
    }

    public class Future : Printable<Future>, Product
    {
        public ProductType ProductType { get { return ProductType.Future; } }
        public CoinType CoinType { get; set; }
        public FutureType FutureType { get; set; }
    }

    public class ProductDepth : Printable<ProductDepth>
    {
        public Product Product { get; set; }
        public List<Amount> Orders { get; set; }
    }

    public class Trade : Printable<Trade>
    {
        public long Id { get; set; }
        public Amount Amount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ProductTrades : Printable<ProductTrades>
    {
        public Product Product { get; set; }
        public List<Trade> Trades { get; set; }
    }

    public enum OrderStatus
    {
        Cancelled = -1,
        Pending = 0,
        PartiallyFilled = 1,
        FullyFilled = 2,
        Cancelling = 3,
    }

    public class Fill : Printable<Fill>
    {
        public long FillId { get; set; }
        public long OrderId { get; set; }
        public Product Product { get; set; }
        public Amount Amount { get; set; }
        public decimal LeftQuantity { get; set; }
        public DateTime Timestamp { get; set; }
        public OrderStatus OrderStatus { get; set; }
    }

    public enum OrderType
    {
        Limit,
        Market,
    }

    public class NewSpotRequest : Printable<NewSpotRequest>
    {
        public CoinType CoinType { get; set; }
        public OrderType OrderType { get; set; }
        public Amount Amount { get; set; }
    }

    public enum PositionType
    {
        Open,
        Close,
    }

    public enum Levarage
    {
        x10 = 10,
        x20 = 20,
    }

    public class NewFutureRequest : Printable<NewFutureRequest>
    {
        public CoinType CoinType { get; set; }
        public OrderType OrderType { get; set; }
        public FutureType FutureType { get; set; }
        public Amount Amount { get; set; }
        public PositionType PositionType { get; set; }
        public Levarage Levarage { get; set; }
    }

    public class NewOrderResponse : Printable<NewOrderResponse>
    {
        public Product Product { get; set; }
        public long? OrderId { get; set; }
    }

    public class CancelOrderRequest : Printable<CancelOrderRequest>
    {
        public Product Product { get; set; }
        public long OrderId { get; set; }
    }

    public class CancelOrderResponse : Printable<CancelOrderResponse>
    {
        public Product Product { get; set; }
        public long? OrderId { get; set; }
    }

    public class Lock : IDisposable
    {
        public void Dispose()
        {
            // TODO
        }
    }

    public class Connection : IDisposable
    {
        readonly Currency _exchangeCurrency;

        public Connection(Currency exchangeCurrency)
        {
            _exchangeCurrency = exchangeCurrency;
        }

        public Currency ExchangeCurrency { get { return _exchangeCurrency; } }

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
