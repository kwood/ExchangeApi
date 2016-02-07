﻿using Conditions;
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
        T Visit(MarketDataRequest msg);
        T Visit(NewFutureRequest msg);
        T Visit(CancelOrderRequest msg);
        T Visit(MyOrdersRequest msg);
    }

    public interface IMessageIn
    {
        ErrorCode? Error { get; set; }
        T Visit<T>(IVisitorIn<T> v);
    }

    public interface IVisitorIn<T>
    {
        T Visit(ProductDepth msg);
        T Visit(ProductTrades msg);
        T Visit(NewOrderResponse msg);
        T Visit(CancelOrderResponse msg);
        T Visit(MyOrderUpdate msg);
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

    // Note: the list of errors may not be exhaustive. As a result, an instance
    // of ErrorCode may be equal to an arbitrary integer.
    public enum ErrorCode
    {
        // Errors from the exchange.
        // See https://www.okcoin.com/about/ws_request.do.
        IllegalParameters = 10001,
        AuthenticationFailure = 10002,
        ThisConnectionHasRequestedOtherUserData = 10003,
        ThisConnectionDidNotRequestThisUserData = 10004,
        SystemError1 = 10005,
        OrderDoesNotExist1 = 10009,
        InsufficientFunds = 10010,
        OrderQuantityTooLow = 10011,
        OnlySupportBtcUsdAndLtcUsd = 10012,
        OrderPriceMustBeBetween0And1000000 = 10014,
        ChannelSubscriptionTemporallyNotAvailable = 10015,
        InsufficientCoins = 10016,
        WebSocketAuthorizationError = 10017,
        UserFrozen1 = 10100,
        NonPublicApi = 10216,
        UserCanNotHaveMoreThan50UnfilledSmallOrdersWithAmountBelowHalfBtc = 10049,
        UserDoesNotExist = 20001,
        UserFrozen2 = 20002,
        FrozenDueToForceLiquidation = 20003,
        FutureAccountFrozen = 20004,
        UserFutureAccountDoesNotExist = 20005,
        RequiredFieldCanNotBeNull = 20006,
        IllegalParameter = 20007,
        FutureAccountFundBalanceIsZero = 20008,
        FutureContractStatusError = 20009,
        RiskRateInformationDoesNotExist = 20010,
        RiskRateBiggerThan90PercentBeforeOpeningPosition = 20011,
        RiskRateBiggerThan90PercentAfterOpeningPosition = 20012,
        TemporallyNoCounterPartyPrice = 20013,
        SystemError2 = 20014,
        OrderDoesNotExist2 = 20015,
        LiquidationQuantityBiggerThanHolding = 20016,
        NotAuthorizedOrIllegalOrderId = 20017,
        OrderPriceHigherThan105PercentOrLowerThan95PercentOfThePriceOfLastMinute = 20018,
        IpRestrainedToAccessTheResource = 20019,
        SecretKeyDoesNotExist = 20020,
        IndexInformationDoesNotExist = 20021,
        WrongApiInterface = 20022,
        FixedMarginUser = 20023,
        SignatureDoesNotMatch = 20024,
        LeverageRateError = 20025,

        // These are synthetic errors, generated by the ExchangeApi itself.
        MalformedResponse = 666001,
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
        public ErrorCode? Error { get; set; }

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
        public ErrorCode? Error { get; set; }

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
        Unfilled = 0,
        PartiallyFilled = 1,
        FullyFilled = 2,
        Cancelling = 3,
    }

    public interface OrderState
    {
        // When order state changes as a result of a fill, Timestamp is truncated to seconds.
        // Otherwise it's truncated to milliseconds.
        DateTime Timestamp { get; set; }
        long OrderId { get; set; }
        OrderStatus OrderStatus { get; set; }
        Product Product { get; set; }
        Amount Amount { get; set; }
        decimal CumFillQuantity { get; set; }
        decimal AvgFillPrice { get; set; }
    }

    public class SpotState : Util.Printable<SpotState>, OrderState
    {
        public DateTime Timestamp { get; set; }
        public long OrderId { get; set; }
        public OrderStatus OrderStatus { get; set; }
        public Product Product { get; set; }
        public Amount Amount { get; set; }
        public decimal CumFillQuantity { get; set; }
        public decimal AvgFillPrice { get; set; }

        // Available only for spots.
        public decimal FillQuantity { get; set; }
        public decimal FillPrice { get; set; }
    }

    public class FutureState : Util.Printable<FutureState>, OrderState
    {
        public DateTime Timestamp { get; set; }
        public long OrderId { get; set; }
        public OrderStatus OrderStatus { get; set; }
        public Product Product { get; set; }
        public Amount Amount { get; set; }
        public decimal CumFillQuantity { get; set; }
        public decimal AvgFillPrice { get; set; }

        // Available only for futures.
        public PositionType PositionType { get; set; }
        public decimal Fee { get; set; }
        // Example: "20160212034" (settlement on 2016-02-12).
        public string ContractId { get; set; }
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
        Long,
        Short,
    }

    public enum Leverage
    {
        x10 = 10,
        x20 = 20,
    }

    public class NewFutureRequest : Util.Printable<NewFutureRequest>, IMessageOut
    {
        public CoinType CoinType { get; set; }
        public Currency Currency { get; set; }
        public OrderType OrderType { get; set; }
        public FutureType FutureType { get; set; }
        public Amount Amount { get; set; }
        public PositionType PositionType { get; set; }
        public Leverage Leverage { get; set; }

        public T Visit<T>(IVisitorOut<T> v)
        {
            return v.Visit(this);
        }
    }

    public class NewOrderResponse : Util.Printable<NewOrderResponse>, IMessageIn
    {
        public ErrorCode? Error { get; set; }

        public ProductType ProductType { get; set; }
        public Currency Currency { get; set; }
        public long OrderId { get; set; }

        public T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    public class CancelOrderRequest : Util.Printable<CancelOrderRequest>, IMessageOut
    {
        public Product Product { get; set; }
        public long OrderId { get; set; }

        public T Visit<T>(IVisitorOut<T> v)
        {
            return v.Visit(this);
        }
    }

    public class CancelOrderResponse : Util.Printable<CancelOrderResponse>, IMessageIn
    {
        public ErrorCode? Error { get; set; }

        public ProductType ProductType { get; set; }
        public Currency Currency { get; set; }
        public long OrderId { get; set; }

        public T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    public enum MarketData
    {
        Depth60,
        Trades,
    }

    public class MarketDataRequest : Util.Printable<MarketDataRequest>, IMessageOut
    {
        public Product Product { get; set; }
        public MarketData MarketData { get; set; }

        public T Visit<T>(IVisitorOut<T> v)
        {
            return v.Visit(this);
        }
    }

    public class MyOrdersRequest : Util.Printable<MyOrdersRequest>, IMessageOut
    {
        public ProductType ProductType { get; set; }
        public Currency Currency { get; set; }

        public T Visit<T>(IVisitorOut<T> v)
        {
            return v.Visit(this);
        }
    }

    public class MyOrderUpdate : Util.Printable<MyOrderUpdate>, IMessageIn
    {
        public ErrorCode? Error { get; set; }

        // Can be null. OkCoin sends us an empty response to confirm our subscription.
        public OrderState Order { get; set; }

        // These two fields are a little awkward. If Order is not null, then the same
        // data can be found inside of it. If Order is null, these fields are the only
        // means for identifying the channel.
        public ProductType ProductType { get; set; }
        public Currency Currency { get; set; }

        public T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }
}
