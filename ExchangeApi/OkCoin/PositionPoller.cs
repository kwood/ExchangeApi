using NLog;
using ExchangeApi.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    class PositionPoller : IDisposable
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(60);

        readonly REST.RestClient _restClient;
        // {Currency, CoinType} pairs for futures without duplicates.
        readonly Tuple<Currency, CoinType>[] _products;
        readonly PeriodicAction[] _pollers;
        volatile bool _connected = false;

        public PositionPoller(string restEndpoint, Keys keys, Scheduler scheduler, IEnumerable<Product> products)
        {
            _restClient = new REST.RestClient(restEndpoint, keys);
            _products = products
                .Where(p => p.ProductType == ProductType.Future)
                .Select(p => Tuple.Create(p.Currency, p.CoinType))
                .GroupBy(p => p)
                .Select(p => p.First())
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
                    _pollers[i] = new PeriodicAction(scheduler, delay, PollPeriod, isLast =>
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
        public event Action<TimestampedMsg<FuturePositionsUpdate>, bool> OnFuturePositions;

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
            var update = new FuturePositionsUpdate()
            {
                Currency = currency,
                CoinType = coin,
                Positions = _restClient.FuturePosition(currency, coin),
            };
            var msg = new TimestampedMsg<FuturePositionsUpdate>()
            {
                Received = DateTime.UtcNow,
                Value = update,
            };
            // TODO: get rid of the second arg.
            try { OnFuturePositions?.Invoke(msg, false); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from OnFuturePositions"); }
        }
    }
}
