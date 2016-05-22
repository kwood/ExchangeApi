using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    public class Keys
    {
        public string Key { get; set; }
        public string Secret { get; set; }
        public string Passphrase { get; set; }

        public Keys Clone()
        {
            return (Keys)MemberwiseClone();
        }
    }
}
