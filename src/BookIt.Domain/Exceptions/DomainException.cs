namespace BookIt.Domain.Exceptions;

/// <summary>Thrown when an operation would violate a domain invariant (e.g. an invalid booking state transition).</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
