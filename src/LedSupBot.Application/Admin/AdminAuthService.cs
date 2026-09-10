using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Admin;

public interface IAdminAuthService
{
    bool IsConfigured { get; }

    bool TryCreateSessionToken(out string token, out DateTimeOffset expiresAt);

    bool TryValidateSessionToken(string? token);

    bool ValidatePassword(string password);
}

public sealed class AdminAuthService : IAdminAuthService
{
    private readonly AdminOptions _options;

    public AdminAuthService(IOptions<AdminOptions> options) => _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.DashboardPassword) &&
        !string.IsNullOrWhiteSpace(_options.SessionSecret);

    public bool ValidatePassword(string password) =>
        IsConfigured &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password ?? string.Empty),
            Encoding.UTF8.GetBytes(_options.DashboardPassword));

    public bool TryCreateSessionToken(out string token, out DateTimeOffset expiresAt)
    {
        token = string.Empty;
        expiresAt = default;

        if (!IsConfigured)
        {
            return false;
        }

        expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(_options.SessionHours, 1, 168));
        var payload = JsonSerializer.Serialize(new SessionPayload(expiresAt.ToUnixTimeSeconds()));
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var payloadB64 = Convert.ToBase64String(payloadBytes);
        var signature = Sign(payloadB64);
        token = $"{payloadB64}.{signature}";
        return true;
    }

    public bool TryValidateSessionToken(string? token)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        var expected = Sign(parts[0]);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(parts[1])))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            var payload = JsonSerializer.Deserialize<SessionPayload>(json);
            if (payload is null)
            {
                return false;
            }

            return payload.Exp >= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        catch
        {
            return false;
        }
    }

    private string Sign(string payloadB64)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SessionSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        return Convert.ToHexString(hash);
    }

    private sealed record SessionPayload(long Exp);
}
