using Conditions;
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

    public class Amount : Util.Printable<Amount>
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

        // Instrument uniquely identifies a product.
        // Use it for comparing products for equality.
        //
        // Instrument.Parse() does the reverse transformation.
        // For any well formed string s: Instrument.Parse(s).Instrument == s.
        string Instrument { get; }
    }

    public class Spot : Util.Printable<Spot>, Product
    {
        public ProductType ProductType { get { return ProductType.Spot; } }
        public Currency Currency { get; set; }
        public CoinType CoinType { get; set; }

        // { CoinType = Btc, Currency = Usd } => "btc_usd_spot".
        public string Instrument
        {
            get
            {
                return String.Format("{0}_{1}_spot", PrintEnum(CoinType), PrintEnum(Currency));
            }
        }

        static string PrintEnum<E>(E e)
        {
            return Util.Strings.CamelCaseToUnderscores(e.ToString()).ToLower();
        }
    }

    public class Future : Util.Printable<Future>, Product
    {
        public ProductType ProductType { get { return ProductType.Future; } }
        public Currency Currency { get; set; }
        public CoinType CoinType { get; set; }
        public FutureType FutureType { get; set; }

        // { CoinType = Btc, Currency = Usd, FutureType = ThisWeek } => "btc_usd_this_week".
        public string Instrument
        {
            get
            {
                return String.Format("{0}_{1}_{2}", PrintEnum(CoinType), PrintEnum(Currency), PrintEnum(FutureType));
            }
        }

        static string PrintEnum<E>(E e)
        {
            return Util.Strings.CamelCaseToUnderscores(e.ToString()).ToLower();
        }
    }

    public static class Instrument
    {
        // "btc_usd_spot" => new Spot() { CoinType = Btc, Currency = Usd }.
        //
        // Throws on error.
        //
        // Product.Instrument does the reverse transformation.
        // For any well formed string s: Instrument.Parse(s).Instrument == s.
        public static Product Parse(string instrument)
        {
            Condition.Requires(instrument, "instrument").IsNotNullOrEmpty();
            string[] parts = instrument.Split(new char[] { '_' }, 3);
            Condition.Requires(parts, "parts").HasLength(3, String.Format("Invalid instrument: '{0}'", instrument));
            var coin = ParseEnum<CoinType>(parts[0]);
            var currency = ParseEnum<Currency>(parts[1]);
            if (parts[2] == "spot")
            {
                return new Spot() { CoinType = coin, Currency = currency };
            }
            else
            {
                return new Future() { CoinType = coin, Currency = currency, FutureType = ParseEnum<FutureType>(parts[2]) };
            }
        }

        static E ParseEnum<E>(string s)
        {
            return (E)Enum.Parse(typeof(E), Util.Strings.UnderscoresToCamelCase(s));
        }
    }

    public class ProductDepth : Util.Printable<ProductDepth>, IMessageIn
    {
        public Product Product { get; set; }
        public DateTime Timestamp { get; set; }
        public List<Amount> Orders { get; set; }

        public T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    public class Trade : Util.Printable<Trade>
    {
        public long Id { get; set; }
        public Amount Amount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ProductTrades : Util.Printable<ProductTrades>, IMessageIn
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

    public class Fill : Util.Printable<Fill>
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

    public class NewSpotRequest : Util.Printable<NewSpotRequest>
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

    public class NewFutureRequest : Util.Printable<NewFutureRequest>
    {
        public CoinType CoinType { get; set; }
        public Currency Currency { get; set; }
        public OrderType OrderType { get; set; }
        public FutureType FutureType { get; set; }
        public Amount Amount { get; set; }
        public PositionType PositionType { get; set; }
        public Levarage Levarage { get; set; }
    }

    public class NewOrderResponse : Util.Printable<NewOrderResponse>
    {
        public Product Product { get; set; }
        public long? OrderId { get; set; }
    }

    public class CancelOrderRequest : Util.Printable<CancelOrderRequest>
    {
        public Product Product { get; set; }
        public long OrderId { get; set; }
    }

    public class CancelOrderResponse : Util.Printable<CancelOrderResponse>
    {
        public Product Product { get; set; }
        public long? OrderId { get; set; }
    }

    public enum ChanelType
    {
        Depth60,
        Trades,
    }

    public class SubscribeRequest : Util.Printable<SubscribeRequest>, IMessageOut
    {
        public Product Product { get; set; }
        public ChanelType ChannelType { get; set; }

        public T Visit<T>(IVisitorOut<T> v)
        {
            return v.Visit(this);
        }
    }
}
