using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin.REST
{
    static class ErrorCode
    {
        public static string Describe(int code)
        {
            switch (code)
            {
                case 10000:
                    return "Required field, can not be null";
                case 10001:
                    return "Request frequency too high";
                case 10002:
                    return "System error";
                case 10003:
                    return "Not in reqest list, please try again later";
                case 10004:
                    return "IP not allowed to access the resource";
                case 10005:
                    return "'secretKey' does not exist";
                case 10006:
                    return "'partner' does not exist";
                case 10007:
                    return "Signature does not match";
                case 10008:
                    return "Illegal parameter";
                case 10009:
                    return "Order does not exist";
                case 10010:
                    return "Insufficient funds";
                case 10011:
                    return "Amount too low";
                case 10012:
                    return "Only btc_usd ltc_usd supported";
                case 10013:
                    return "Only support https request";
                case 10014:
                    return "Order price must be between 0 and 1,000,000";
                case 10015:
                    return "Order price differs from current market price too much";
                case 10016:
                    return "Insufficient coins balance";
                case 10017:
                    return "API authorization error";
                case 10018:
                    return "borrow amount less than lower limit [usd:100,btc:0.1,ltc:1]";
                case 10019:
                    return "loan agreement not checked";
                case 10020:
                    return "rate cannot exceed 1%";
                case 10021:
                    return "rate cannot less than 0.01%";
                case 10023:
                    return "fail to get latest ticker";
                case 10024:
                    return "balance not sufficient";
                case 10025:
                    return "quota is full, cannot borrow temporarily";
                case 10026:
                    return "Loan (including reserved loan) and margin cannot be withdrawn";
                case 10027:
                    return "Cannot withdraw within 24 hrs of authentication information modification";
                case 10028:
                    return "Withdrawal amount exceeds daily limit";
                case 10029:
                    return "Account has unpaid loan, please cancel/pay off the loan before withdraw";
                case 10031:
                    return "Deposits can only be withdrawn after 6 confirmations";
                case 10032:
                    return "Please enabled phone/google authenticator";
                case 10033:
                    return "Fee higher than maximum network transaction fee";
                case 10034:
                    return "Fee lower than minimum network transaction fee";
                case 10035:
                    return "Insufficient BTC/LTC";
                case 10036:
                    return "Withdrawal amount too low";
                case 10037:
                    return "Trade password not set";
                case 10040:
                    return "Withdrawal cancellation fails";
                case 10041:
                    return "Withdrawal address not approved";
                case 10042:
                    return "Admin password error";
                case 10043:
                    return "Account equity error, withdrawal failure";
                case 10044:
                    return "fail to cancel borrowing order";
                case 10047:
                    return "this function is disabled for sub-account";
                case 10048:
                    return "withdrawal information does not exist";
                case 10049:
                    return "User can not have more than 50 unfilled small orders (amount<0.5BTC)";
                case 10050:
                    return "can't cancel more than once";
                case 10100:
                    return "User account frozen";
                case 10216:
                    return "Non-available API";
                case 20001:
                    return "User does not exist";
                case 20002:
                    return "Account frozen";
                case 20003:
                    return "Account frozen due to liquidation";
                case 20004:
                    return "Futures account frozen";
                case 20005:
                    return "User futures account does not exist";
                case 20006:
                    return "Required field missing";
                case 20007:
                    return "Illegal parameter";
                case 20008:
                    return "Futures account balance is too low";
                case 20009:
                    return "Future contract status error";
                case 20010:
                    return "Risk rate ratio does not exist";
                case 20011:
                    return "Risk rate lower than 90% before opening position";
                case 20012:
                    return "Risk rate lower than 90% after opening position";
                case 20013:
                    return "Temporally no counter party price";
                case 20014:
                    return "System error";
                case 20015:
                    return "Order does not exist";
                case 20016:
                    return "Close amount bigger than your open positions";
                case 20017:
                    return "Not authorized/illegal operation";
                case 20018:
                    return "Order price cannot be more than 103% or less than 97% of the previous minute price";
                case 20019:
                    return "IP restricted from accessing the resource";
                case 20020:
                    return "secretKey does not exist";
                case 20021:
                    return "Index information does not exist";
                case 20022:
                    return "Wrong API interface (Cross margin mode shall call cross margin API, fixed margin mode shall call fixed margin API)";
                case 20023:
                    return "Account in fixed-margin mode";
                case 20024:
                    return "Signature does not match";
                case 20025:
                    return "Leverage rate error";
                case 20026:
                    return "API Permission Error";
                case 20027:
                    return "no transaction record";
                case 20028:
                    return "no such contract";
                case 20029:
                    return "Amount is large than available funds";
                case 20030:
                    return "Account still has debts";
                case 20038:
                    return "Due to regulation, this function is not availavle in the country/region your currently reside in.";
                case 503:
                    return "Too many requests (Http)";

                default:
                    return code.ToString();
            }
        }
    }
}
