using Conditions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    public class Json
    {
        public static JObject ParseObject(string s)
        {
            Condition.Requires(s, "s").IsNotNull();
            JsonReader reader = new JsonTextReader(new StringReader(s));
            reader.DateParseHandling = DateParseHandling.DateTime;
            reader.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
            reader.DateFormatString = "yyyy-MM-ddTHH:mm:ss.FFFFFFZ";
            reader.FloatParseHandling = FloatParseHandling.Decimal;
            return JObject.Load(reader);
        }
    }
}
