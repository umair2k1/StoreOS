namespace StoreOS.Domain.Exceptions;

public class DomainException : Exception
{
    public string Code { get; }
    public DomainException(string code, string message) : base(message) => Code = code;
}

public class DomainValidationException : DomainException
{
    public DomainValidationException(string code, string message) : base(code, message) { }
}
