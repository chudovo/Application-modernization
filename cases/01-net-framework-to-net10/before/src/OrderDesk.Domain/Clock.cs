using System;

namespace OrderDesk.Domain
{
    /// <summary>Ambient clock. Tests overwrite the static delegate, which makes them order-dependent.</summary>
    public static class Clock
    {
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    }
}
