using System;

namespace OrderDesk.Domain
{
    public class OrderValidationException : Exception
    {
        public OrderValidationException(string message) : base(message) { }
    }

    public class OrderConflictException : Exception
    {
        public OrderConflictException(string message) : base(message) { }
    }
}
