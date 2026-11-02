namespace SwiftRide.SharedKernel;

public enum ErrorKind
{
    Validation,
    Unauthenticated,
    Forbidden,
    NotFound,
    Conflict,
    Unavailable,
}

public sealed class RuleException(string message, ErrorKind kind = ErrorKind.Conflict)
    : Exception(message)
{
    public ErrorKind Kind { get; } = kind;
}
