namespace OrderDesk.Domain;

public class OrderValidationException(string message) : Exception(message);

public class OrderConflictException(string message) : Exception(message);
