namespace N2.Documents.Exceptions;
public class N2DocumentException : Exception
{
    public int ErrorCode { get; protected set; } = 500;

    public N2DocumentException(string message) : base(message)
    {
    }

    public N2DocumentException()
    {
    }

    public N2DocumentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}