using LedSupBot.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.WhatsApp;

public enum WhatsAppOutboundDestinationSource
{
    ConversationLiveRoute = 0,
    InboundRemoteJid = 1,
    LinkedWhatsAppLid = 2,
    ProvenPnJid = 3,
    Reconstructed = 4,
    CustomerAddress = 5
}

public sealed record WhatsAppOutboundDestination(
    string Address,
    WhatsAppOutboundDestinationSource Source,
    string DestinationType,
    bool ExactRoutePreserved,
    bool SelfMatch);

public static class WhatsAppOutboundDestinationResolver
{
    public static async Task<WhatsAppOutboundDestination> ResolveAsync(
        IApplicationDbContext db,
        IWhatsAppSelfIdentityStore selfIdentity,
        Guid conversationId,
        string? whatsAppDeliveryRoute,
        string customerIdentity,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new ConversationRouteRow(
                c.LastInboundWhatsAppAddress,
                c.Customer.PhoneNumber,
                c.Customer.LinkedWhatsAppLid))
            .FirstOrDefaultAsync(cancellationToken);

        foreach (var candidate in BuildCandidates(row, whatsAppDeliveryRoute, customerIdentity))
        {
            if (selfIdentity.IsSelfJid(candidate.Address))
            {
                continue;
            }

            return Create(candidate.Address, candidate.Source, whatsAppDeliveryRoute, selfIdentity);
        }

        throw new InvalidOperationException("Cannot resolve non-self WhatsApp outbound destination.");
    }

    private static IEnumerable<(string Address, WhatsAppOutboundDestinationSource Source)> BuildCandidates(
        ConversationRouteRow? row,
        string? whatsAppDeliveryRoute,
        string customerIdentity)
    {
        if (!string.IsNullOrWhiteSpace(row?.LastInboundWhatsAppAddress) &&
            row.LastInboundWhatsAppAddress.Contains('@', StringComparison.Ordinal))
        {
            yield return (
                WhatsAppEventMapper.NormalizeWhatsAppAddress(row.LastInboundWhatsAppAddress),
                WhatsAppOutboundDestinationSource.ConversationLiveRoute);
        }

        if (!string.IsNullOrWhiteSpace(whatsAppDeliveryRoute) &&
            whatsAppDeliveryRoute.Contains('@', StringComparison.Ordinal))
        {
            yield return (
                WhatsAppEventMapper.NormalizeWhatsAppAddress(whatsAppDeliveryRoute),
                WhatsAppOutboundDestinationSource.InboundRemoteJid);
        }

        if (!string.IsNullOrWhiteSpace(row?.LinkedWhatsAppLid))
        {
            yield return (
                WhatsAppEventMapper.NormalizeWhatsAppAddress(row.LinkedWhatsAppLid),
                WhatsAppOutboundDestinationSource.LinkedWhatsAppLid);
        }

        if (!string.IsNullOrWhiteSpace(customerIdentity) &&
            customerIdentity.Contains('@', StringComparison.Ordinal) &&
            !WhatsAppEventMapper.IsGroupJid(customerIdentity))
        {
            var normalized = WhatsAppEventMapper.NormalizeWhatsAppAddress(customerIdentity);
            yield return (
                normalized,
                WhatsAppEventMapper.IsLidJid(normalized)
                    ? WhatsAppOutboundDestinationSource.LinkedWhatsAppLid
                    : WhatsAppOutboundDestinationSource.ProvenPnJid);
        }

        if (!string.IsNullOrWhiteSpace(row?.PhoneNumber) &&
            !row.PhoneNumber.Contains('@', StringComparison.Ordinal))
        {
            var digits = new string(row.PhoneNumber.Where(char.IsDigit).ToArray());
            yield return ($"{digits}@s.whatsapp.net", WhatsAppOutboundDestinationSource.Reconstructed);
        }

        if (!string.IsNullOrWhiteSpace(customerIdentity))
        {
            var digits = new string(customerIdentity.Where(char.IsDigit).ToArray());
            if (digits.Length > 0)
            {
                yield return ($"{digits}@s.whatsapp.net", WhatsAppOutboundDestinationSource.CustomerAddress);
            }
        }
    }

    private static WhatsAppOutboundDestination Create(
        string address,
        WhatsAppOutboundDestinationSource source,
        string? requestedRoute,
        IWhatsAppSelfIdentityStore selfIdentity)
    {
        var destinationType = WhatsAppEventMapper.IsGroupJid(address)
            ? "g.us"
            : WhatsAppEventMapper.IsLidJid(address)
                ? "lid"
                : "s.whatsapp.net";

        var exactRoutePreserved = string.IsNullOrWhiteSpace(requestedRoute) ||
            !requestedRoute.Contains('@', StringComparison.Ordinal) ||
            string.Equals(
                WhatsAppEventMapper.NormalizeWhatsAppAddress(requestedRoute),
                address,
                StringComparison.OrdinalIgnoreCase);

        return new WhatsAppOutboundDestination(
            address,
            source,
            destinationType,
            exactRoutePreserved,
            selfIdentity.IsSelfJid(address));
    }

    private sealed record ConversationRouteRow(
        string? LastInboundWhatsAppAddress,
        string PhoneNumber,
        string? LinkedWhatsAppLid);
}
