namespace LedSupBot.Desktop.Services;

public sealed class LedSupBotApiException : Exception
{
    public LedSupBotApiException(string userMessage, int? statusCode = null, Exception? inner = null)
        : base(userMessage, inner)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
