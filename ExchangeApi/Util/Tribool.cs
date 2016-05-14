using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    // From https://msdn.microsoft.com/en-us/library/aa664483(v=vs.71).aspx.
    public struct Tribool
    {
        // The three possible Tribool values.
        public static readonly Tribool Null = new Tribool(0);
        public static readonly Tribool False = new Tribool(-1);
        public static readonly Tribool True = new Tribool(1);
        // Private field that stores –1, 0, 1 for False, Null, True.
        sbyte value;
        // Private instance constructor. The value parameter must be –1, 0, or 1.
        Tribool(int value)
        {
            this.value = (sbyte)value;
        }
        // Properties to examine the value of a Tribool. Return true if this
        // Tribool has the given value, false otherwise.
        public bool IsNull { get { return value == 0; } }
        public bool IsFalse { get { return value < 0; } }
        public bool IsTrue { get { return value > 0; } }
        // Implicit conversion from bool to Tribool. Maps true to Tribool.True and
        // false to Tribool.False.
        public static implicit operator Tribool(bool x)
        {
            return x ? True : False;
        }
        // Explicit conversion from Tribool to bool. Throws an exception if the
        // given Tribool is Null, otherwise returns true or false.
        public static explicit operator bool(Tribool x)
        {
            if (x.value == 0) throw new InvalidOperationException();
            return x.value > 0;
        }
        // Equality operator. Returns Null if either operand is Null, otherwise
        // returns True or False.
        public static Tribool operator ==(Tribool x, Tribool y)
        {
            if (x.value == 0 || y.value == 0) return Null;
            return x.value == y.value ? True : False;
        }
        // Inequality operator. Returns Null if either operand is Null, otherwise
        // returns True or False.
        public static Tribool operator !=(Tribool x, Tribool y)
        {
            if (x.value == 0 || y.value == 0) return Null;
            return x.value != y.value ? True : False;
        }
        // Logical negation operator. Returns True if the operand is False, Null
        // if the operand is Null, or False if the operand is True.
        public static Tribool operator !(Tribool x)
        {
            return new Tribool(-x.value);
        }
        // Logical AND operator. Returns False if either operand is False,
        // otherwise Null if either operand is Null, otherwise True.
        public static Tribool operator &(Tribool x, Tribool y)
        {
            return new Tribool(x.value < y.value ? x.value : y.value);
        }
        // Logical OR operator. Returns True if either operand is True, otherwise
        // Null if either operand is Null, otherwise False.
        public static Tribool operator |(Tribool x, Tribool y)
        {
            return new Tribool(x.value > y.value ? x.value : y.value);
        }
        // Definitely true operator. Returns true if the operand is True, false
        // otherwise.
        public static bool operator true(Tribool x)
        {
            return x.value > 0;
        }
        // Definitely false operator. Returns true if the operand is False, false
        // otherwise.
        public static bool operator false(Tribool x)
        {
            return x.value < 0;
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Tribool)) return false;
            return value == ((Tribool)obj).value;
        }
        public override int GetHashCode()
        {
            return value;
        }
        public override string ToString()
        {
            if (value > 0) return "Tribool.True";
            if (value < 0) return "Tribool.False";
            return "Tribool.Null";
        }
    }
}
