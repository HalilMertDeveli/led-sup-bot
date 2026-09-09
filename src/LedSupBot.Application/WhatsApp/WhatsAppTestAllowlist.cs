using System.Collections.Concurrent;
using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.WhatsApp;

public sealed class WhatsAppTestAllowlist : IWhatsAppTestAllowlist
{
    private readonly WhatsAppTestModeOptions _testModeOptions;
    private readonly WhatsAppAutomationOptions _automationOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppTestAllowlist> _logger;

    private readonly ConcurrentDictionary<string, byte> _allowedLidAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, byte> _allowedCustomerIds = new();

    private readonly ConcurrentDictionary<string, byte> _excludedCanonicalPhones = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _excludedLidAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, byte> _excludedCustomerIds = new();

    private readonly object _preloadSync = new();
    private bool _preloaded;

    public WhatsAppTestAllowlist(
        IOptions<WhatsAppTestModeOptions> testModeOptions,
        IOptions<WhatsAppAutomationOptions> automationOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<WhatsAppTestAllowlist> logger)
    {
        _testModeOptions = testModeOptions.Value;
        _automationOptions = automationOptions.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;

        foreach (var phone in _automationOptions.ExcludedPhoneNumbers)
        {
            if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(phone) is { } canonical)
            {
                _excludedCanonicalPhones.TryAdd(canonical, 0);
            }
        }
    }

    public bool IsEnabled => _testModeOptions.Enabled;

    public bool HasConfiguredIdentity =>
        PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(_testModeOptions.AllowedPhoneNumber) is not null;

    public bool IsAutoReplyAllowed(
        WhatsAppInboundEvent inbound,
        string customerAddress,
        Customer? customer) =>
        Evaluate(inbound, customerAddress, customer, logMismatch: true);

    public bool IsOutboundAllowed(string customerAddress, Customer? customer = null) =>
        Evaluate(inbound: null, customerAddress, customer, logMismatch: false);

    public bool IsExcluded(string customerAddress, Customer? customer = null, WhatsAppInboundEvent? inbound = null)
    {
        EnsurePreloaded();
        return MatchesExcludedIdentity(inbound, customerAddress, customer);
    }

    private bool Evaluate(
        WhatsAppInboundEvent? inbound,
        string customerAddress,
        Customer? customer,
        bool logMismatch)
    {
        // 1. Group check: Groups NEVER receive bot automation
        if (WhatsAppEventMapper.IsGroupJid(inbound?.RemoteJid ?? customerAddress))
        {
            return false;
        }

        EnsurePreloaded();

        // 2. Exclusion check: Excluded numbers ALWAYS win (ZERO bot automation)
        if (MatchesExcludedIdentity(inbound, customerAddress, customer))
        {
            RememberExcludedIdentity(customerAddress, customer);
            if (logMismatch)
            {
                _logger.LogInformation("Excluded phone blocked from bot automation");
            }

            return false;
        }

        // 3. TestMode check
        if (!_testModeOptions.Enabled)
        {
            return true;
        }

        var allowedCanonical = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(_testModeOptions.AllowedPhoneNumber);
        if (allowedCanonical is null)
        {
            if (logMismatch)
            {
                _logger.LogWarning("WhatsApp test mode enabled but allowed identity is not configured; auto-reply blocked");
            }

            return false;
        }

        if (MatchesAllowedIdentity(inbound, customerAddress, customer, allowedCanonical))
        {
            RememberAllowedIdentity(customerAddress, customer);
            if (logMismatch)
            {
                _logger.LogInformation("Test allowlist matched=true");
            }

            return true;
        }

        // LID routes: an authorized test customer must not be rejected merely
        // because the active WhatsApp route is @lid and the inbound event does
        // not carry a sender phone (Baileys often omits sender PN on LID chats).
        // Ask the database whether this LID belongs to the configured allowed
        // identity. This is bounded to already-linked identities only, so it
        // does NOT weaken test-mode security.
        if (TryResolveAllowedLidFromDatabase(customerAddress, allowedCanonical))
        {
            RememberAllowedIdentity(customerAddress, customer);
            if (logMismatch)
            {
                _logger.LogInformation("Test allowlist matched via persisted LID link=true");
            }

            return true;
        }

        if (logMismatch)
        {
            if (WhatsAppEventMapper.IsLidJid(inbound?.RemoteJid) &&
                (inbound is null || !HasSenderPnProof(inbound, allowedCanonical)))
            {
                _logger.LogInformation("Test allowlist identity unresolved");
            }
            else
            {
                _logger.LogInformation("Test allowlist matched=false");
            }
        }

        return false;
    }

    /// <summary>
    /// Bounded database check for LID-routed identities: a customer already
    /// linked to the allowed phone number (either directly through
    /// <see cref="Customer.LinkedWhatsAppLid"/> or because the customer row
    /// itself IS the allowed number) may only be considered allowed when the
    /// persisted canonical phone equals the configured test identity.
    /// </summary>
    private bool TryResolveAllowedLidFromDatabase(string customerAddress, string allowedCanonical)
    {
        if (!WhatsAppEventMapper.IsLidJid(customerAddress))
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<IApplicationDbContext>();
        if (db is null)
        {
            return false;
        }

        try
        {
            var address = customerAddress.Trim();
            foreach (var row in db.Customers.AsNoTracking()
                .Where(c => c.LinkedWhatsAppLid == address || c.PhoneNumber == address)
                .Select(c => new { c.Id, c.PhoneNumber, c.LinkedWhatsAppLid })
                .ToList())
            {
                var canonical = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(row.PhoneNumber);
                if (canonical is not null && canonical == allowedCanonical)
                {
                    _allowedLidAddresses.TryAdd(address, 0);
                    _allowedCustomerIds.TryAdd(row.Id, 0);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Test allowlist LID database resolution unavailable");
        }

        return false;
    }

    private bool MatchesExcludedIdentity(
        WhatsAppInboundEvent? inbound,
        string customerAddress,
        Customer? customer)
    {
        if (_excludedCanonicalPhones.IsEmpty && _excludedCustomerIds.IsEmpty && _excludedLidAddresses.IsEmpty)
        {
            return false;
        }

        if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(customerAddress) is { } addressCanonical &&
            _excludedCanonicalPhones.ContainsKey(addressCanonical))
        {
            return true;
        }

        if (inbound is not null)
        {
            if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(inbound.RemotePhoneNumber) is { } remoteCanonical &&
                _excludedCanonicalPhones.ContainsKey(remoteCanonical))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(inbound.SenderPnJid))
            {
                var senderUser = inbound.SenderPnJid.Split('@')[0]?.Split(':')[0];
                if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(senderUser) is { } senderCanonical &&
                    _excludedCanonicalPhones.ContainsKey(senderCanonical))
                {
                    return true;
                }
            }
        }

        if (customer is not null)
        {
            if (_excludedCustomerIds.ContainsKey(customer.Id))
            {
                return true;
            }

            if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(customer.PhoneNumber) is { } customerCanonical &&
                _excludedCanonicalPhones.ContainsKey(customerCanonical))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(customer.LinkedWhatsAppLid) &&
                _excludedLidAddresses.ContainsKey(customer.LinkedWhatsAppLid.Trim()))
            {
                return true;
            }
        }

        if (_excludedLidAddresses.ContainsKey(customerAddress.Trim()))
        {
            return true;
        }

        return false;
    }

    private bool MatchesAllowedIdentity(
        WhatsAppInboundEvent? inbound,
        string customerAddress,
        Customer? customer,
        string allowedCanonical)
    {
        if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(customerAddress) is { } addressCanonical &&
            addressCanonical == allowedCanonical)
        {
            return true;
        }

        if (inbound is not null)
        {
            if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(inbound.RemotePhoneNumber) is { } remoteCanonical &&
                remoteCanonical == allowedCanonical)
            {
                return true;
            }

            if (HasSenderPnProof(inbound, allowedCanonical))
            {
                return true;
            }
        }

        if (customer is not null)
        {
            if (_allowedCustomerIds.ContainsKey(customer.Id))
            {
                return true;
            }

            if (PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(customer.PhoneNumber) is { } customerCanonical &&
                customerCanonical == allowedCanonical)
            {
                return true;
            }

            if (WhatsAppEventMapper.IsLidJid(customer.PhoneNumber) &&
                string.Equals(customer.PhoneNumber, customerAddress, StringComparison.OrdinalIgnoreCase) &&
                _allowedLidAddresses.ContainsKey(customerAddress))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(customer.LinkedWhatsAppLid) &&
                string.Equals(customer.LinkedWhatsAppLid, customerAddress, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (_allowedLidAddresses.ContainsKey(customerAddress))
        {
            return true;
        }

        return false;
    }

    private static bool HasSenderPnProof(WhatsAppInboundEvent inbound, string allowedCanonical)
    {
        if (string.IsNullOrWhiteSpace(inbound.SenderPnJid))
        {
            return false;
        }

        var senderUser = inbound.SenderPnJid.Split('@')[0]?.Split(':')[0];
        return PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(senderUser) == allowedCanonical;
    }

    private void RememberExcludedIdentity(string customerAddress, Customer? customer)
    {
        if (WhatsAppEventMapper.IsLidJid(customerAddress))
        {
            _excludedLidAddresses.TryAdd(customerAddress.Trim(), 0);
        }

        if (customer is not null)
        {
            _excludedCustomerIds.TryAdd(customer.Id, 0);
            if (WhatsAppEventMapper.IsLidJid(customer.PhoneNumber))
            {
                _excludedLidAddresses.TryAdd(customer.PhoneNumber.Trim(), 0);
            }

            if (!string.IsNullOrWhiteSpace(customer.LinkedWhatsAppLid))
            {
                _excludedLidAddresses.TryAdd(customer.LinkedWhatsAppLid.Trim(), 0);
            }
        }
    }

    private void RememberAllowedIdentity(string customerAddress, Customer? customer)
    {
        if (WhatsAppEventMapper.IsLidJid(customerAddress))
        {
            _allowedLidAddresses.TryAdd(customerAddress.Trim(), 0);
        }

        if (customer is not null)
        {
            _allowedCustomerIds.TryAdd(customer.Id, 0);
            if (WhatsAppEventMapper.IsLidJid(customer.PhoneNumber))
            {
                _allowedLidAddresses.TryAdd(customer.PhoneNumber.Trim(), 0);
            }

            if (!string.IsNullOrWhiteSpace(customer.LinkedWhatsAppLid))
            {
                _allowedLidAddresses.TryAdd(customer.LinkedWhatsAppLid.Trim(), 0);
            }
        }
    }

    private void EnsurePreloaded()
    {
        if (_preloaded)
        {
            return;
        }

        lock (_preloadSync)
        {
            if (_preloaded)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<IApplicationDbContext>();
            if (db is null)
            {
                _preloaded = true;
                return;
            }

            try
            {
                var allowedCanonical = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(_testModeOptions.AllowedPhoneNumber);
                var customers = db.Customers.AsNoTracking()
                    .Select(c => new { c.Id, c.PhoneNumber, c.LinkedWhatsAppLid })
                    .ToList();

                foreach (var row in customers)
                {
                    var canonical = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(row.PhoneNumber);

                    // Preload allowlist cache if test mode enabled
                    if (allowedCanonical is not null && canonical == allowedCanonical)
                    {
                        _allowedCustomerIds.TryAdd(row.Id, 0);
                        if (WhatsAppEventMapper.IsLidJid(row.PhoneNumber))
                        {
                            _allowedLidAddresses.TryAdd(row.PhoneNumber.Trim(), 0);
                        }

                        if (!string.IsNullOrWhiteSpace(row.LinkedWhatsAppLid))
                        {
                            _allowedLidAddresses.TryAdd(row.LinkedWhatsAppLid.Trim(), 0);
                        }
                    }

                    // Preload exclusion cache
                    if (canonical is not null && _excludedCanonicalPhones.ContainsKey(canonical))
                    {
                        _excludedCustomerIds.TryAdd(row.Id, 0);
                        if (WhatsAppEventMapper.IsLidJid(row.PhoneNumber))
                        {
                            _excludedLidAddresses.TryAdd(row.PhoneNumber.Trim(), 0);
                        }

                        if (!string.IsNullOrWhiteSpace(row.LinkedWhatsAppLid))
                        {
                            _excludedLidAddresses.TryAdd(row.LinkedWhatsAppLid.Trim(), 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WhatsApp test allowlist and exclusion preload skipped");
            }

            _preloaded = true;
        }
    }
}
