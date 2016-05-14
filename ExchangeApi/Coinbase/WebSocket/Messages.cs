using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase.WebSocket
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

        // Server time.
        DateTime Time { get; set; }
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        string ProductId { get; set; }
        long Sequence { get; set; }
        Side Side { get; set; }
    }

    public interface IVisitorIn<T>
    {
        T Visit(OrderReceived msg);
        T Visit(OrderOpen msg);
        T Visit(OrderDone msg);
        T Visit(OrderMatch msg);
        T Visit(OrderChange msg);
    }

    public enum OrderType
    {
        Limit,
        Market,
    }

    public enum DoneReason
    {
        Filled,
        Canceled,
    }

    public class SubscribeRequest : Util.Printable<SubscribeRequest>, IMessageOut
    {
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        public string ProductId { get; set; }

        public T Visit<T>(IVisitorOut<T> v)
        {
            return v.Visit(this);
        }
    }

    public abstract class OrderBase<TDerived> : Util.Printable<TDerived>, IMessageIn
    {
        // Server time.
        public DateTime Time { get; set; }
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        public string ProductId { get; set; }
        public long Sequence { get; set; }
        public Side Side { get; set; }

        public abstract T Visit<T>(IVisitorIn<T> v);
    }

    // See https://docs.exchange.coinbase.com/#received.
    //
    // A valid order has been received and is now active.This message is emitted for every single valid order
    // as soon as the matching engine receives it whether it fills immediately or not.
    //
    // The received message does not indicate a resting order on the order book.It simply indicates a new incoming
    // order which as been accepted by the matching engine for processing.Received orders may cause match message
    // to follow if they are able to begin being filled(taker behavior). Self-trade prevention may also trigger
    // change messages to follow if the order size needs to be adjusted.Orders which are not fully filled or
    // canceled due to self-trade prevention result in an open message and become resting orders on the order book.
    //
    // Market orders (indicated by the order_type field) may have an optional funds field which indicates how much
    // quote currency will be used to buy or sell.For example, a funds field of 100.00 for the BTC-USD product would
    // indicate a purchase of up to 100.00 USD worth of bitcoin.
    public class OrderReceived : OrderBase<OrderReceived>
    {
        public string OrderId { get; set; }
        // Set only for limit orders.
        public decimal? Size { get; set; }
        // Always set for limit order. May be missing for market orders, in which case Funds
        // is set.
        public decimal? Price { get; set; }
        // Never set for limit orders. If set, Price is missing.
        public decimal? Funds { get; set; }
        public OrderType OrderType { get; set; }
        // Equal to client_oid in the new order request. Null if client_oid wasn't specified.
        // This field isn't properly documented: search for client_oid on https://docs.exchange.coinbase.com/.
        public string ClientOrderId { get; set; }

        public override T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    // See https://docs.exchange.coinbase.com/#open.
    //
    // The order is now open on the order book. This message will only be sent for orders which are not fully filled
    // immediately. remaining_size will indicate how much of the order is unfilled and going on the book.
    //
    // There will be no open message for orders which will be filled immediately. There will be no open message for
    // market orders since they are filled immediately.
    public class OrderOpen : OrderBase<OrderOpen>
    {
        public string OrderId { get; set; }
        public decimal Price { get; set; }
        public decimal RemainingSize { get; set; }

        public override T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    // See https://docs.exchange.coinbase.com/#done.
    //
    // The order is no longer on the order book. Sent for all orders for which there was a received message. This
    // message can result from an order being canceled or filled. There will be no more messages for this order_id
    // after a done message. remaining_size indicates how much of the order went unfilled; this will be 0 for
    // filled orders.
    //
    // market orders will not have a remaining_size or price field as they are never on the open order book at a
    // given price.
    //
    // A done message will be sent for received orders which are fully filled or canceled due to self-trade
    // prevention. There will be no open message for such orders. done messages for orders which are not on the
    // book should be ignored when maintaining a real-time order book.
    public class OrderDone : OrderBase<OrderDone>
    {
        public string OrderId { get; set; }
        // Set only for limit orders.
        public decimal? Price { get; set; }
        // Set only for limit orders. Zero if Reason is Filled.
        public decimal? RemainingSize { get; set; }
        public DoneReason Reason { get; set; }
        public OrderType OrderType { get; set; }

        public override T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    // See https://docs.exchange.coinbase.com/#match.
    //
    // A trade occurred between two orders. The aggressor or taker order is the one executing immediately after
    // being received and the maker order is a resting order on the book. The side field indicates the maker
    // order side. If the side is sell this indicates the maker was a sell order and the match is considered an
    // up-tick. A buy side match is a down-tick.
    public class OrderMatch : OrderBase<OrderMatch>
    {
        public long TradeId { get; set; }
        public string MakerOrderId { get; set; }
        public string TakerOrderId { get; set; }
        public decimal Price { get; set; }
        public decimal Size { get; set; }

        public override T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }

    // See https://docs.exchange.coinbase.com/#change.
    //
    // An order has changed. This is the result of self-trade prevention adjusting the order size or available funds.
    // Orders can only decrease in size or funds. change messages are sent anytime an order changes in size; this
    // includes resting orders (open) as well as received but not yet open. change messages are also sent when a
    // new market order goes through self trade prevention and the funds for the market order have changed.
    //
    // change messages for received but not yet open orders can be ignored when building a real-time order book.
    // Any change message where the price is null indicates that the change message is for a market order. Change
    // messages for limit orders will always have a price specified.
    public class OrderChange : OrderBase<OrderChange>
    {
        public string OrderId { get; set; }
        // Set only for limit orders.
        public decimal? Price { get; set; }
        // Either NewSize and OldSize are set, or NewFunds and OldFunds are set.
        public decimal? NewSize { get; set; }
        public decimal? OldSize { get; set; }
        public decimal? NewFunds { get; set; }
        public decimal? OldFunds { get; set; }

        public override T Visit<T>(IVisitorIn<T> v)
        {
            return v.Visit(this);
        }
    }
}
