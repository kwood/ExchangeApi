using ExchangeApi;
using ExchangeApi.Coinbase;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoinbaseMarketFeedTest
{
    public class OrderBook : ExchangeApi.Util.Printable<OrderBook>
    {
        public SortedDictionary<decimal, decimal> Bids = new SortedDictionary<decimal, decimal>();
        public SortedDictionary<decimal, decimal> Asks = new SortedDictionary<decimal, decimal>();
        public long Sequence = -1;
    }

    class Program
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static void ApplyDelta(OrderBook book, OrderBookDelta delta)
        {
            Action<SortedDictionary<decimal, decimal>, List<PriceLevel>> Merge = (prev, cur) =>
            {
                foreach (PriceLevel level in cur)
                {
                    if (level.SizeDelta == 0)
                        _log.Error("Zero delta on price level {0}", level.Price);
                    if (prev.ContainsKey(level.Price))
                    {
                        decimal size = prev[level.Price] += level.SizeDelta;
                        if (size < 0)
                            _log.Error("Negative quantity on price level {0}: {1}", level.Price, size);
                        else if (size == 0)
                            prev.Remove(level.Price);
                    }
                    else
                    {
                        if (level.SizeDelta < 0)
                            _log.Error("Negative quantity on price level {0}: {1}", level.Price, level.SizeDelta);
                        prev[level.Price] = level.SizeDelta;
                    }
                }
            };
            if (delta.Sequence < book.Sequence)
                _log.Error("Sequence number went backwards from {0} to {1}", book.Sequence, delta.Sequence);
            else if (delta.Sequence == book.Sequence && (delta.Asks.Any() || delta.Bids.Any()))
                _log.Error("Received non-empty delta but the sequence number didnt' advance: {0}", delta);
            book.Sequence = delta.Sequence;
            Merge(book.Bids, delta.Bids);
            Merge(book.Asks, delta.Asks);
        }

        static void Convert(ExchangeApi.Coinbase.REST.FullOrderBook src, OrderBook dst)
        {
            Action<List<ExchangeApi.Coinbase.REST.Order>, SortedDictionary<decimal, decimal>> Cvt = (from, to) =>
            {
                to.Clear();
                foreach (var order in from)
                {
                    if (to.ContainsKey(order.Price))
                        to[order.Price] += order.Quantity;
                    else
                        to[order.Price] = order.Quantity;
                }
            };
            dst.Sequence = src.Sequence;
            Cvt(src.Bids, dst.Bids);
            Cvt(src.Asks, dst.Asks);
        }

        static void Compare(OrderBook expected, OrderBook actual)
        {
            if (expected.Sequence != actual.Sequence) return;
            Func<SortedDictionary<decimal, decimal>, SortedDictionary<decimal, decimal>, bool> Eq = (a, b) =>
            {
                using (var e1 = a.GetEnumerator())
                using (var e2 = b.GetEnumerator())
                {
                    while (e1.MoveNext())
                    {
                        if (!e2.MoveNext())
                        {
                            _log.Error("Key mismatch at {0}", e1.Current.Key);
                            return false;
                        }
                        if (e1.Current.Key != e2.Current.Key)
                        {
                            _log.Error("Key mismatch: {0} vs {1}", e1.Current.Key, e2.Current.Key);
                            return false;
                        }
                        if (e1.Current.Value != e2.Current.Value)
                        {
                            _log.Error("Value mismatch at {0}: {1} vs {2}",
                                       e1.Current.Key, e1.Current.Value, e2.Current.Value);
                            return false;
                        }
                    }
                    if (e2.MoveNext())
                    {
                        _log.Error("Key mismatch at {0}", e2.Current.Key);
                        return false;
                    }
                }
                // Redundant sanity check.
                if (!a.Keys.SequenceEqual(b.Keys) || !a.Values.SequenceEqual(b.Values))
                {
                    _log.Error("Internal error in the test");
                    return false;
                }
                return true;
            };
            if (Eq(expected.Bids, actual.Bids) && Eq(expected.Asks, actual.Asks))
                _log.Info("The actual order book matches the expected");
            else
                _log.Error("The actual order book doesn't match the expected");
        }

        static void ListenToMarketData()
        {
            var cfg = new Config()
            {
                Endpoint = Instance.Prod,
                Products = new List<string>() { "BTC-USD" },
            };
            using (var restClient = new ExchangeApi.Coinbase.REST.RestClient(cfg.Endpoint.REST, null))
            using (var client = new Client(cfg))
            {
                var book = new OrderBook();
                var expectedBook = new OrderBook();
                Action poll = () =>
                {
                    if (book.Sequence < 0)
                    {
                        _log.Info("Haven't received any messages yet");
                        return;
                    }
                    // Sleep on the scheduler thread in order to fall behind on the order book.
                    Thread.Sleep(20000);
                    ExchangeApi.Coinbase.REST.FullOrderBook full = restClient.GetProductOrderBook("BTC-USD");
                    if (full.Sequence <= book.Sequence)
                    {
                        _log.Info("Order book isn't behind");
                        return;
                    }
                    _log.Info("Expecting sequence {0} in the near future", full.Sequence);
                    Convert(full, expectedBook);
                };
                client.OnOrderBook += (string product, TimestampedMsg<OrderBookDelta> msg) =>
                {
                    if (msg.Value.Bids.Count + msg.Value.Asks.Count > 10)
                    {
                        _log.Info("OnOrderBook({0}, IsLast={1}): {2} bid(s), {3} ask(s)",
                                  product, !client.Scheduler.HasReady(), msg.Value.Bids.Count, msg.Value.Asks.Count);
                    }
                    else
                    {
                        _log.Info("OnOrderBook({0}, IsLast={1}): {2}", product, !client.Scheduler.HasReady(), msg.Value);
                    }
                    ApplyDelta(book, msg.Value);
                    Compare(expectedBook, book);

                };
                client.Connect();
                using (var timer = new PeriodicAction(cfg.Scheduler, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), poll))
                    while (true) Thread.Sleep(1000);
            }
        }

        static void Main(string[] args)
        {
            try
            {
                ListenToMarketData();
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
