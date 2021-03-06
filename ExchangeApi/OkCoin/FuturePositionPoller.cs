﻿using NLog;
using ExchangeApi.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Conditions;

namespace ExchangeApi.OkCoin
{
    class FuturePositionPoller : IDisposable
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(60);

        readonly REST.RestClient _restClient;
        // {Currency, CoinType} pairs for futures without duplicates.
        readonly Tuple<Currency, CoinType>[] _products;
        readonly PeriodicAction[] _pollers;
        volatile bool _connected = false;

        public FuturePositionPoller(REST.RestClient restClient, Scheduler scheduler, IEnumerable<Product> products)
        {
            Condition.Requires(restClient, "restClient").IsNotNull();
            _restClient = restClient;
            _products = products
                .Where(p => p.ProductType == ProductType.Future)
                .Select(p => Tuple.Create(p.Currency, p.CoinType))
                .Dedup()
                .ToArray();

            _pollers = new PeriodicAction[_products.Length];
            try
            {
                for (int i = 0; i != _pollers.Length; ++i)
                {
                    var p = _products[i];
                    // Spread polling of different products over the polling period.
                    // While we are polling positions, we aren't processing market data and order updates.
                    // To reduce downtime we query positions for one product at a time.
                    var delay = PollPeriod.Mul(i + 1).Div(_pollers.Length);
                    // PollOne can throw. That's OK -- PeriodicAction will log it and continue.
                    _pollers[i] = new PeriodicAction(scheduler, delay, PollPeriod, () =>
                    {
                        if (Connected) PollOne(p.Item1, p.Item2);
                    });
                }
            }
            catch
            {
                foreach (PeriodicAction a in _pollers)
                {
                    if (a != null) a.Dispose();
                }
                throw;
            }
        }

        // If PollNow() is only ever called from the scheduler thread (this is true for all uses of this
        // class), OnFuturePositions is fired only on the scheduler thread.
        public event Action<TimestampedMsg<FuturePositionsUpdate>> OnFuturePositions;

        public void Connect() { _connected = true; }
        public void Disconnect() { _connected = false; }
        public bool Connected { get { return _connected; } }

        // Polls positions for all products synchronously. Blocks. Throws on IO error and parse errors.
        // Raises OnFuturePositions synchronously.
        public void PollNow()
        {
            foreach (var p in _products)
            {
                PollOne(p.Item1, p.Item2);
            }
        }

        public void Dispose()
        {
            foreach (PeriodicAction a in _pollers)
            {
                a.Dispose();
            }
        }

        // Called from the scheduler thread.
        void PollOne(Currency currency, CoinType coin)
        {
            foreach (var elem in _restClient.FuturePositions(currency, coin))
            {
                var update = new FuturePositionsUpdate()
                {
                    Currency = currency,
                    CoinType = coin,
                    FutureType = elem.Key,
                    Positions = elem.Value,
                };
                var msg = new TimestampedMsg<FuturePositionsUpdate>()
                {
                    Received = DateTime.UtcNow,
                    Value = update,
                };
                try { OnFuturePositions?.Invoke(msg); }
                catch (Exception e) { _log.Warn(e, "Ignoring exception from OnFuturePositions"); }
            }
        }
    }
}
