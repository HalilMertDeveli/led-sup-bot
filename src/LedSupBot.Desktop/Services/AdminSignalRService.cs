using System.Net.Http;
using LedSupBot.Desktop.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace LedSupBot.Desktop.Services;

public sealed class AdminSignalRService : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly LedSupBotApiClient _apiClient;
    private readonly string _hubUrl;
    private readonly object _lock = new();
    private CancellationTokenSource? _reconnectLoopCts;
    private bool _isDisposed;

    public event Action? StateChanged;
    public event Action<Guid?>? ConversationUpdated;
    public event Action<Guid?, Guid?>? MessageReceived;
    public event Action<Guid?, string?>? PanelPackagesChanged;
    public event Action<string?>? WhatsAppStatusChanged;
    public event Action<AnyDeskAlertEvent>? AnyDeskReady;
    public event Action? Reconnected;
    public event Action<HubConnectionState, string?>? ConnectionStateChanged;

    public AdminSignalRService(LedSupBotApiClient apiClient, string apiBaseUrl)
    {
        _apiClient = apiClient;
        _hubUrl = apiBaseUrl.TrimEnd('/') + "/hubs/admin";
    }

    public HubConnectionState ConnectionState =>
        _connection?.State ?? HubConnectionState.Disconnected;

    public bool IsConnected => ConnectionState == HubConnectionState.Connected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            if (_connection is { State: HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting })
            {
                return;
            }

            BuildConnectionLocked();
        }

        try
        {
            ConnectionStateChanged?.Invoke(HubConnectionState.Connecting, "Bağlanıyor...");
            await _connection!.StartAsync(cancellationToken);
            DesktopLog.Info("SignalR", "connected successfully to " + _hubUrl);
            ConnectionStateChanged?.Invoke(HubConnectionState.Connected, "Canlı");
            StopReconnectLoop();
        }
        catch (Exception ex)
        {
            DesktopLog.Warn("SignalR", "StartAsync failed: " + ex.Message);
            ConnectionStateChanged?.Invoke(HubConnectionState.Disconnected, "Bağlantı Yok");
            StartReconnectLoop();
        }
    }

    private void BuildConnectionLocked()
    {
        if (_connection is not null)
        {
            try
            {
                _connection.Reconnecting -= OnReconnecting;
                _connection.Reconnected -= OnReconnected;
                _connection.Closed -= OnClosed;
            }
            catch
            {
                /* ignore */
            }
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    CookieContainer = _apiClient.Cookies,
                    UseCookies = true,
                };
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
            })
            .Build();

        _connection.Reconnecting += OnReconnecting;
        _connection.Reconnected += OnReconnected;
        _connection.Closed += OnClosed;

        // Typed and fallback payload registrations
        _connection.On<ConversationEventPayload>("ConversationUpdated", payload =>
        {
            ConversationUpdated?.Invoke(payload?.ConversationId);
            StateChanged?.Invoke();
        });
        _connection.On("ConversationUpdated", () =>
        {
            ConversationUpdated?.Invoke(null);
            StateChanged?.Invoke();
        });

        _connection.On<MessageEventPayload>("MessageReceived", payload =>
        {
            MessageReceived?.Invoke(payload?.ConversationId, payload?.MessageId);
            StateChanged?.Invoke();
        });
        _connection.On("MessageReceived", () =>
        {
            MessageReceived?.Invoke(null, null);
            StateChanged?.Invoke();
        });

        _connection.On<WhatsAppStatusEventPayload>("WhatsAppStatusChanged", payload =>
        {
            WhatsAppStatusChanged?.Invoke(payload?.Status);
            StateChanged?.Invoke();
        });

        _connection.On<PanelPackagesChangedEventPayload>("PanelPackagesChanged", payload =>
        {
            PanelPackagesChanged?.Invoke(payload?.PackageId, payload?.Change);
            StateChanged?.Invoke();
        });

        _connection.On<OperatorStateEventPayload>("OperatorStateChanged", _ =>
        {
            StateChanged?.Invoke();
        });
        _connection.On("OperatorStateChanged", () =>
        {
            StateChanged?.Invoke();
        });

        _connection.On<AnyDeskAlertEvent>("AnyDeskReadyForHandoff", evt =>
        {
            if (evt is not null)
            {
                AnyDeskReady?.Invoke(evt);
            }
            StateChanged?.Invoke();
        });
    }

    private Task OnReconnecting(Exception? ex)
    {
        DesktopLog.Warn("SignalR", "Reconnecting: " + ex?.Message);
        ConnectionStateChanged?.Invoke(HubConnectionState.Reconnecting, "Canlı bağlantı yeniden kuruluyor...");
        return Task.CompletedTask;
    }

    private Task OnReconnected(string? connectionId)
    {
        DesktopLog.Info("SignalR", "Reconnected with connectionId=" + connectionId);
        ConnectionStateChanged?.Invoke(HubConnectionState.Connected, "Canlı");
        Reconnected?.Invoke();
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private Task OnClosed(Exception? ex)
    {
        DesktopLog.Warn("SignalR", "Closed: " + ex?.Message);
        ConnectionStateChanged?.Invoke(HubConnectionState.Disconnected, "Bağlantı Yok");
        StartReconnectLoop();
        return Task.CompletedTask;
    }

    private void StartReconnectLoop()
    {
        lock (_lock)
        {
            if (_isDisposed || _reconnectLoopCts is not null)
            {
                return;
            }

            _reconnectLoopCts = new CancellationTokenSource();
            var token = _reconnectLoopCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_isDisposed)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (_connection is { State: HubConnectionState.Connected })
                    {
                        break;
                    }

                    try
                    {
                        ConnectionStateChanged?.Invoke(HubConnectionState.Connecting, "Bağlanıyor...");
                        if (_connection is null || _connection.State == HubConnectionState.Disconnected)
                        {
                            lock (_lock)
                            {
                                BuildConnectionLocked();
                            }
                        }

                        await _connection!.StartAsync(token);
                        DesktopLog.Info("SignalR", "reconnect loop succeeded");
                        ConnectionStateChanged?.Invoke(HubConnectionState.Connected, "Canlı");
                        Reconnected?.Invoke();
                        StateChanged?.Invoke();
                        break;
                    }
                    catch (Exception ex)
                    {
                        DesktopLog.Debug("SignalR", "reconnect loop retry: " + ex.Message);
                        ConnectionStateChanged?.Invoke(HubConnectionState.Disconnected, "Bağlantı Yok");
                    }
                }
            }, token);
        }
    }

    private void StopReconnectLoop()
    {
        lock (_lock)
        {
            _reconnectLoopCts?.Cancel();
            _reconnectLoopCts?.Dispose();
            _reconnectLoopCts = null;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        StopReconnectLoop();
        if (_connection is null)
        {
            return;
        }

        try
        {
            await _connection.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            DesktopLog.Debug("SignalR", "DisconnectAsync: " + ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        StopReconnectLoop();

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                DesktopLog.Debug("SignalR", "DisposeAsync: " + ex.Message);
            }
        }
    }
}
