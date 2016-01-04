using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public interface IMessageOut
    {
        T Visit<T>(IVisitorOut<T> v);
    }

    public interface IVisitorOut<T>
    {
        T Visit(SubscribeRequest msg);
    }

    public interface IMessageIn
    {
        T Visit<T>(IVisitorIn<T> v);
    }

    public interface IVisitorIn<T>
    {
        T Visit(ProductDepth msg);
        T Visit(ProductTrades msg);
    }

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
        public Side Side { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }

    public enum ProductType
    {
        Spot,
        Future,
    }

    public interface Product
    {
        ProductType ProductType { get; }
        Currency Currency { get; set; }
        CoinType CoinType { get; set; }
    }

    public class Spot : Printable<Spot>, Product
    {
        public ProductType ProductType { get { return ProductType.Spot; } }
        public Currency Currency { get; set; }
        public CoinType CoinType { get; set; }
    }

    public class Future : Printable<Future>, Product
    {
        public ProductType ProductType { get { return ProductType.Future; } }
        public Currency Currency { get; set; }
        public CoinType CoinType { get; set; }
        public FutureType FutureType { get; set; }
    }

    public class ProductDepth : Printable<ProductDepth>, IMessageIn
    {
        public Product Product { get; set; }
        public DateTime Timestamp { get; set; }
        public List<Amount> Orders { get; set; }

        public T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    public class Trade : Printable<Trade>
    {
        public long Id { get; set; }
        public Amount Amount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ProductTrades : Printable<ProductTrades>, IMessageIn
    {
        public Product Product { get; set; }
        public List<Trade> Trades { get; set; }

        public T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
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
        public Currency Currency { get; set; }
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
        public Currency Currency { get; set; }
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

    public enum ChanelType
    {
        Depth60,
        Trades,
    }

    public class SubscribeRequest : Printable<SubscribeRequest>, IMessageOut
    {
        public Product Product { get; set; }
        public ChanelType ChannelType { get; set; }

        public T Visit<T>(IVisitorOut<T> v)
        {
            return v.Visit(this);
        }
    }
}
