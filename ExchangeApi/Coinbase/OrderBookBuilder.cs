using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    // Order book builder for a single product.
    class OrderBookBuilder
    {
        class Level
        {
            // Non-negative (zero is fine).
            public decimal TotalSize = 0m;
            // Not empty.
            public HashSet<string> OrderIds = new HashSet<string>();
        }

        static readonly Logger _log = LogManager.GetCurrentClassLogger();
        long _seqNum = -1;
        SortedDictionary<decimal, Level> _bids = new SortedDictionary<decimal, Level>();
        SortedDictionary<decimal, Level> _asks = new SortedDictionary<decimal, Level>();

        public OrderBookDelta OnSnapshot(REST.FullOrderBook snapshot)
        {
            Condition.Requires(snapshot, "snapshot").IsNotNull();
            Condition.Requires(snapshot.Sequence, "snapshot.Sequence").IsGreaterOrEqual(_seqNum);
            var bids = Aggregate(snapshot.Bids);
            var asks = Aggregate(snapshot.Asks);
            var delta = new OrderBookDelta()
            {
                Time = snapshot.Time,
                Bids = Diff(_bids, bids).ToList(),
                Asks = Diff(_asks, asks).ToList(),
                Sequence = snapshot.Sequence,
            };
            delta.Bids.Reverse();
            _seqNum = snapshot.Sequence;
            _bids = bids;
            _asks = asks;
            return delta;
        }

        public bool OnOrderUpdate(WebSocket.IMessageIn msg, out OrderBookDelta delta, out Trade trade)
        {
            delta = new OrderBookDelta()
            {
                Time = msg.Time,
                Bids = new List<PriceLevel>(),
                Asks = new List<PriceLevel>(),
                Sequence = msg.Sequence,
            };
            trade = new Trade()
            {
                Time = msg.Time,
                Side = msg.Side,
            };
            if (msg.Sequence > _seqNum + 1)
            {
                _log.Warn("Detected a gap in sequence numbers for {0}: {1} => {2}",
                          msg.ProductId, _seqNum, msg.Sequence);
                return false;
            }
            if (msg.Sequence <= _seqNum)
            {
                _log.Info("Ignoring message with sequence {0} for {1}: already at {2}",
                          msg.Sequence, msg.ProductId, _seqNum);
                return true;
            }
            var book_side = msg.Side == Side.Buy ? _bids : _asks;
            var delta_side = msg.Side == Side.Buy ? delta.Bids : delta.Asks;
            // It's important that Visit() doesn't modify book_side if it throws.
            msg.Visit(new MessageHandler(book_side, delta_side, trade));
            _seqNum = msg.Sequence;
            return true;
        }

        static SortedDictionary<decimal, Level> Aggregate(List<REST.Order> orders)
        {
            var res = new SortedDictionary<decimal, Level>();
            foreach (REST.Order order in orders)
            {
                Level level;
                if (!res.TryGetValue(order.Price, out level))
                {
                    level = new Level();
                    res.Add(order.Price, level);
                }
                Condition.Requires(order.Quantity, "order.Quantity").IsGreaterThan(0m);
                level.TotalSize += order.Quantity;
                if (!level.OrderIds.Add(order.Id))
                    throw new ArgumentException("Duplicate order ID in the full order book: " + order.Id);
            }
            return res;
        }

        static IEnumerable<PriceLevel> Diff(SortedDictionary<decimal, Level> prev, SortedDictionary<decimal, Level> cur)
        {
            using (var enumPrev = prev.GetEnumerator())
            using (var enumCur = cur.GetEnumerator())
            {
                bool validPrev = enumPrev.MoveNext();
                bool validCur = enumCur.MoveNext();
                while (validPrev && validCur)
                {
                    int cmp = Decimal.Compare(enumPrev.Current.Key, enumCur.Current.Key);
                    if (cmp < 0)
                    {
                        // A price level has disappeared from the order book.
                        var kv = enumPrev.Current;
                        if (kv.Value.TotalSize > 0)
                            yield return new PriceLevel() { Price = kv.Key, SizeDelta = -kv.Value.TotalSize };
                        validPrev = enumPrev.MoveNext();
                    }
                    else if (cmp > 0)
                    {
                        // A new price level has appeared in the order book.
                        var kv = enumCur.Current;
                        if (kv.Value.TotalSize > 0)
                            yield return new PriceLevel() { Price = kv.Key, SizeDelta = kv.Value.TotalSize };
                        validCur = enumCur.MoveNext();
                    }
                    else
                    {
                        // A potential change in size within a price level.
                        decimal sizePrev = enumPrev.Current.Value.TotalSize;
                        decimal sizeCur = enumCur.Current.Value.TotalSize;
                        if (sizeCur != sizePrev)
                        {
                            yield return new PriceLevel() { Price = enumCur.Current.Key, SizeDelta = sizeCur - sizePrev };
                        }
                        validPrev = enumPrev.MoveNext();
                        validCur = enumCur.MoveNext();
                    }
                }
                while (validPrev)
                {
                    // A price level has disappeared from the order book.
                    var kv = enumPrev.Current;
                    if (kv.Value.TotalSize > 0)
                        yield return new PriceLevel() { Price = kv.Key, SizeDelta = -kv.Value.TotalSize };
                    validPrev = enumPrev.MoveNext();
                }
                while (validCur)
                {
                    // A new price level has appeared in the order book.
                    var kv = enumCur.Current;
                    if (kv.Value.TotalSize > 0)
                        yield return new PriceLevel() { Price = kv.Key, SizeDelta = kv.Value.TotalSize };
                    validCur = enumCur.MoveNext();
                }
            }
        }

        class MessageHandler : WebSocket.IVisitorIn<object>
        {
            static readonly Logger _log = LogManager.GetCurrentClassLogger();

            readonly SortedDictionary<decimal, Level> _book;
            readonly List<PriceLevel> _delta;
            readonly Trade _trade;

            // Visit() may modify `book`, in which case it adds an element to `delta` and optionally
            // modifies `trade`. Visit() always returns null.
            // IMPORTANT: if Visit() throws, it leaves `book` unchanged.
            public MessageHandler(SortedDictionary<decimal, Level> book, List<PriceLevel> delta, Trade trade)
            {
                Condition.Requires(book, "book").IsNotNull();
                Condition.Requires(delta, "delta").IsNotNull();
                _book = book;
                _delta = delta;
                _trade = trade;
            }

            public object Visit(WebSocket.OrderReceived msg)
            {
                return null;
            }

            public object Visit(WebSocket.OrderOpen msg)
            {
                Condition.Requires(msg, "msg").IsNotNull();
                Condition.Requires(msg.OrderId, "msg.OrderId").IsNotNullOrEmpty();
                Condition.Requires(msg.Price, "msg.Price").IsGreaterThan(0m);
                Condition.Requires(msg.RemainingSize, "msg.RemainingSize").IsGreaterThan(0m);

                Level level;
                if (_book.TryGetValue(msg.Price, out level))
                {
                    Condition.Requires(level.TotalSize).IsGreaterOrEqual(0m);
                    Condition.Requires(level.OrderIds).IsNotEmpty();
                }
                else
                {
                    level = new Level();
                    _book.Add(msg.Price, level);
                }

                if (!level.OrderIds.Add(msg.OrderId))
                    throw new ArgumentException("Duplicate order ID: " + msg);
                level.TotalSize += msg.RemainingSize;
                _delta.Add(new PriceLevel() { Price = msg.Price, SizeDelta = msg.RemainingSize });
                return null;
            }

            public object Visit(WebSocket.OrderMatch msg)
            {
                Condition.Requires(msg, "msg").IsNotNull();
                Condition.Requires(msg.MakerOrderId, "msg.MakerOrderId").IsNotNullOrEmpty();
                Condition.Requires(msg.Price, "msg.Price").IsGreaterThan(0m);
                Condition.Requires(msg.Size, "msg.Size").IsGreaterThan(0m);

                Level level;
                if (!_book.TryGetValue(msg.Price, out level))
                    throw new ArgumentException("OrderMatch with price on empty level: " + msg);
                Condition.Requires(level.TotalSize).IsGreaterOrEqual(0m);
                Condition.Requires(level.OrderIds).IsNotEmpty();

                if (!level.OrderIds.Contains(msg.MakerOrderId))
                    throw new ArgumentException("OrderMatch for an order with unknown id: " + msg);
                if (msg.Size > level.TotalSize)
                    throw new ArgumentException("OrderMatch with the size exceeding total level size: " + msg);
                level.TotalSize -= msg.Size;
                _delta.Add(new PriceLevel() { Price = msg.Price, SizeDelta = -msg.Size });
                _trade.Price = msg.Price;
                _trade.Size = msg.Size;
                return null;
            }

            public object Visit(WebSocket.OrderDone msg)
            {
                Condition.Requires(msg, "msg").IsNotNull();
                Condition.Requires(msg.OrderId, "msg.OrderId").IsNotNullOrEmpty();

                if (!msg.Price.HasValue) return null;  // Market order.

                Level level;
                if (!_book.TryGetValue(msg.Price.Value, out level)) return null;  // Not an open order.
                Condition.Requires(level.TotalSize).IsGreaterOrEqual(0m);
                Condition.Requires(level.OrderIds).IsNotEmpty();
                if (!level.OrderIds.Contains(msg.OrderId)) return null;  // Not an open order.

                if (level.OrderIds.Count == 1)
                {
                    if (!msg.RemainingSize.HasValue)
                        throw new ArgumentException("Price level with no orders and non-zero total size: " + msg);
                    Condition.Requires(msg.RemainingSize.Value).IsEqualTo(level.TotalSize);
                    if (msg.RemainingSize.Value > 0)
                        _delta.Add(new PriceLevel() { Price = msg.Price.Value, SizeDelta = -msg.RemainingSize.Value });
                    Condition.Requires(_book.Remove(msg.Price.Value));  // Can't fail.
                    return null;
                }

                if (msg.RemainingSize.HasValue)
                {
                    Condition.Requires(msg.RemainingSize.Value, "msg.RemainingSize.Value").IsGreaterOrEqual(0m);
                    if (msg.RemainingSize.Value > level.TotalSize)
                        throw new ArgumentException("OrderDone with the remaining size exceeding total level size: " + msg);
                    if (msg.RemainingSize.Value > 0)
                    {
                        level.TotalSize -= msg.RemainingSize.Value;
                        _delta.Add(new PriceLevel() { Price = msg.Price.Value, SizeDelta = -msg.RemainingSize.Value });
                    }
                }
                Condition.Requires(level.OrderIds.Remove(msg.OrderId));  // Can't fail because of the `if` above.
                return null;
            }

            public object Visit(WebSocket.OrderChange msg)
            {
                Condition.Requires(msg, "msg").IsNotNull();
                Condition.Requires(msg.OrderId, "msg.OrderId").IsNotNullOrEmpty();

                if (!msg.Price.HasValue) return null;  // Market order.
                Condition.Requires(msg.OldSize.HasValue, "msg.OldSize.HasValue");
                Condition.Requires(msg.NewSize.HasValue, "msg.NewSize.HasValue");

                decimal sizeDelta = msg.NewSize.Value - msg.OldSize.Value;
                if (sizeDelta == 0m) return null;

                Level level;
                if (!_book.TryGetValue(msg.Price.Value, out level)) return null;  // Not an open order.
                Condition.Requires(level.TotalSize).IsGreaterOrEqual(0m);
                Condition.Requires(level.OrderIds).IsNotEmpty();
                if (!level.OrderIds.Contains(msg.OrderId)) return null;  // Not an open order.

                if (-sizeDelta > level.TotalSize)
                    throw new ArgumentException("OrderChange with the delta size exceeding total level size: " + msg);
                level.TotalSize += sizeDelta;
                _delta.Add(new PriceLevel() { Price = msg.Price.Value, SizeDelta = sizeDelta });
                return null;
            }

        }
    }
}
