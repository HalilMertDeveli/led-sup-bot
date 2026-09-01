using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;

namespace LedSupBot.Desktop.Services;

public sealed class AdminSignalRService : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly LedSupBotApiClient _apiClient;
    private readonly string _hubUrl;

    public event Action? ConversationUpdated;
    public event Action? MessageReceived;
    public event Action<AnyDeskAlertEvent>? AnyDeskReady;

    public AdminSignalRService(LedSupBotApiClient apiClient, string apiBaseUrl)
    {
        _apiClient = apiClient;
        _hubUrl = apiBaseUrl.TrimEnd('/') + "/hubs/admin";
    }

    public HubConnectionState ConnectionState =>
        _connection?.State ?? HubConnectionState.Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { State: HubConnectionState.Connected })
        {
            return;
        }

        _connection?.Remove("ConversationUpdated");
        _connection?.Remove("MessageReceived");
        _connection?.Remove("AnyDeskReadyForHandoff");

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    CookieContainer = _apiClient.Cookies,
                    UseCookies = true,
                };
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On("ConversationUpdated", () => ConversationUpdated?.Invoke());
        _connection.On("MessageReceived", () => MessageReceived?.Invoke());
        _connection.On<AnyDeskAlertEvent>("AnyDeskReadyForHandoff", evt => AnyDeskReady?.Invoke(evt));

        await _connection.StartAsync(cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}

public sealed class AnyDeskAlertEvent
{
    public Guid ConversationId { get; set; }
    public string? CustomerDisplayName { get; set; }
    public string? IssueSummary { get; set; }
    public string? ScreenSummary { get; set; }
    public string? ReceiverSummary { get; set; }
    public string? PanelStatus { get; set; }
    public string? ConfigStatus { get; set; }
    public string? AnyDeskStatus { get; set; }
}
