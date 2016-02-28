using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public static class Settlement
    {
        public static FutureType FutureTypeFromContractId(string contractId, DateTime now)
        {
            TimeSpan settlement = SettlementTimeFromContractId(contractId) - now;
            if (settlement < TimeSpan.FromDays(7))
                return FutureType.ThisWeek;
            if (settlement < TimeSpan.FromDays(14))
                return FutureType.NextWeek;
            return FutureType.Quarter;
        }

        static DateTime SettlementTimeFromContractId(string contractId)
        {
            // Example contractId: 20160304013.
            // The first 8 characters specify the settlement date.
            // Settlement is always at 08:00 UTC (4:00 PM China CST).
            var date = DateTime.ParseExact(
                contractId.Substring(0, 8),
                "yyyyMMdd",
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal);
            return date + TimeSpan.FromHours(8);
        }
    }
}
