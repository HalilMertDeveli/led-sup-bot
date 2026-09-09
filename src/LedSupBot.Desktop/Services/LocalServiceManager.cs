using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using LedSupBot.Desktop.Models;

namespace LedSupBot.Desktop.Services;

public sealed class LocalServiceManager : IDisposable
{
    private readonly DesktopSettings _settings;
    private readonly RuntimeEnvironmentLoader _runtimeEnv;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly List<ManagedProcess> _started = [];
    private readonly string _logDirectory;

    public LocalServiceManager(DesktopSettings settings)
    {
        _settings = settings;
        _runtimeEnv = new RuntimeEnvironmentLoader(settings);
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LedSupport",
            "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public async Task EnsureRuntimeAsync(
        Action<RuntimeComponentStatus> report,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProjectRoot))
        {
            FailAll(report, "Proje kökü bulunamadı (ProjectRoot)");
            return;
        }

        await EnsurePostgreSqlAsync(report, cancellationToken);
        await EnsureApiAsync(report, cancellationToken);
        await EnsureConnectorAsync(report, cancellationToken);
        await CheckWhatsAppAsync(report, cancellationToken);
    }

    private async Task EnsurePostgreSqlAsync(Action<RuntimeComponentStatus> report, CancellationToken ct)
    {
        var item = new RuntimeComponentStatus("PostgreSQL", ComponentState.Checking);
        report(item);

        if (await IsPostgreSqlAvailableAsync(ct))
        {
            item.State = ComponentState.Ready;
            item.Detail = "Çalışıyor";
            report(item);
            return;
        }

        if (!_settings.AutoStartPostgreSql)
        {
            item.State = ComponentState.Failed;
            item.Detail = "Erişilemiyor";
            report(item);
            return;
        }

        item.State = ComponentState.Starting;
        item.Detail = "Docker kontrol ediliyor";
        report(item);

        var dockerResult = await TryStartPostgreSqlDockerAsync(ct);
        if (!dockerResult.Success)
        {
            item.State = ComponentState.Failed;
            item.Detail = dockerResult.Message;
            report(item);
            return;
        }

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(1000, ct);
            if (await IsPostgreSqlAvailableAsync(ct))
            {
                item.State = ComponentState.Ready;
                item.Detail = "Çalışıyor";
                report(item);
                return;
            }
        }

        item.State = ComponentState.Failed;
        item.Detail = "PostgreSQL hazır olmadı";
        report(item);
    }

    private async Task EnsureConnectorAsync(Action<RuntimeComponentStatus> report, CancellationToken ct)
    {
        var item = new RuntimeComponentStatus("Connector", ComponentState.Checking);
        report(item);

        if (await IsConnectorHealthyAsync(ct))
        {
            item.State = ComponentState.Ready;
            item.Detail = "Çalışıyor";
            report(item);
            return;
        }

        if (await IsPortOpenAsync("127.0.0.1", _settings.ConnectorPort, ct))
        {
            item.State = ComponentState.Failed;
            item.Detail = $"Port {_settings.ConnectorPort} başka servis tarafından kullanılıyor";
            report(item);
            return;
        }

        if (!_settings.AutoStartConnector)
        {
            item.State = ComponentState.Failed;
            item.Detail = "Auto-start kapalı";
            report(item);
            return;
        }

        item.State = ComponentState.Starting;
        item.Detail = "Başlatılıyor";
        report(item);

        var start = TryStartConnector();
        if (!start.Success)
        {
            item.State = ComponentState.Failed;
            item.Detail = start.Message;
            report(item);
            return;
        }

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(1000, ct);
            if (await IsConnectorHealthyAsync(ct))
            {
                item.State = ComponentState.Ready;
                item.Detail = "Çalışıyor";
                report(item);
                return;
            }

            if (start.Process.HasExited)
            {
                item.State = ComponentState.Failed;
                item.Detail = start.Process.GetExitSummary() ?? "Connector beklenmedik şekilde kapandı";
                report(item);
                return;
            }
        }

        item.State = ComponentState.Failed;
        item.Detail = "Health check başarısız";
        report(item);
    }

    private async Task EnsureApiAsync(Action<RuntimeComponentStatus> report, CancellationToken ct)
    {
        var item = new RuntimeComponentStatus("API", ComponentState.Checking);
        report(item);

        if (await IsApiHealthyAsync(ct))
        {
            var connectorLive = await IsConnectorStatusConnectedAsync(ct);
            var apiWhatsApp = await GetFoundationWhatsAppStatusAsync(ct);
            var apiConnected = string.Equals(apiWhatsApp, "Connected", StringComparison.OrdinalIgnoreCase);

            if (connectorLive && !apiConnected && TryStopProjectApiOnPort())
            {
                DesktopLog.Info("LocalServiceManager", "Restarting misconfigured LedSupBot.Api on API port");
                await Task.Delay(1500, ct);
            }
            else if (await IsApiHealthyAsync(ct))
            {
                item.State = ComponentState.Ready;
                item.Detail = "Çalışıyor";
                report(item);
                return;
            }
        }

        if (await IsPortOpenAsync("127.0.0.1", GetApiPort(), ct))
        {
            item.State = ComponentState.Failed;
            item.Detail = $"Port {GetApiPort()} başka servis tarafından kullanılıyor";
            report(item);
            return;
        }

        if (!_settings.AutoStartApi)
        {
            item.State = ComponentState.Failed;
            item.Detail = "Auto-start kapalı";
            report(item);
            return;
        }

        if (!_runtimeEnv.HasDatabaseConnectionString)
        {
            item.State = ComponentState.Failed;
            item.Detail = _runtimeEnv.DatabaseConfigMissingMessage;
            report(item);
            return;
        }

        item.State = ComponentState.Starting;
        item.Detail = "Başlatılıyor";
        report(item);

        var start = TryStartApi();
        if (!start.Success)
        {
            item.State = ComponentState.Failed;
            item.Detail = start.Message;
            report(item);
            return;
        }

        for (var i = 0; i < 45; i++)
        {
            await Task.Delay(1000, ct);
            if (await IsApiHealthyAsync(ct))
            {
                item.State = ComponentState.Ready;
                item.Detail = "Çalışıyor";
                report(item);
                return;
            }

            if (start.Process.HasExited)
            {
                item.State = ComponentState.Failed;
                item.Detail = start.Process.GetExitSummary() ?? "API beklenmedik şekilde kapandı";
                report(item);
                return;
            }
        }

        item.State = ComponentState.Failed;
        item.Detail = "Health check başarısız";
        report(item);
    }

    private async Task CheckWhatsAppAsync(Action<RuntimeComponentStatus> report, CancellationToken ct)
    {
        var item = new RuntimeComponentStatus("WhatsApp", ComponentState.Checking);
        report(item);

        for (var i = 0; i < 25; i++)
        {
            try
            {
                var url = $"{_settings.ApiBaseUrl.TrimEnd('/')}/api/foundation/status";
                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var raw = doc.RootElement.TryGetProperty("whatsAppConnection", out var prop)
                    ? prop.GetString()
                    : null;

                if (string.Equals(raw, "Connected", StringComparison.OrdinalIgnoreCase))
                {
                    item.State = ComponentState.Ready;
                    item.Detail = "Bağlı";
                    report(item);
                    return;
                }

                item.Detail = MapWhatsApp(raw);
            }
            catch
            {
                item.Detail = "Kontrol ediliyor";
            }

            await Task.Delay(1000, ct);
            item.State = ComponentState.Checking;
            report(item);
        }

        item.State = ComponentState.Failed;
        item.Detail = string.IsNullOrWhiteSpace(item.Detail) ? "Bağlantı yok" : item.Detail;
        report(item);
    }

    private async Task<bool> IsPostgreSqlAvailableAsync(CancellationToken ct) =>
        await IsPortOpenAsync(_settings.PostgreSqlHost, _settings.PostgreSqlPort, ct);

    private async Task<(bool Success, string Message)> TryStartPostgreSqlDockerAsync(CancellationToken ct)
    {
        var docker = FindExecutable("docker.exe") ?? "docker";
        if (!await CanRunCommandAsync(docker, "version --format \"{{.Server.Version}}\"", ct))
        {
            return (false, "Docker çalışmıyor");
        }

        var container = _settings.PostgreSqlDockerContainer;
        var inspect = await RunCommandCaptureAsync(docker, $"inspect -f \"{{{{.State.Running}}}}\" {container}", ct);
        if (inspect.ExitCode != 0)
        {
            if (inspect.Output.Contains("No such object", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Docker container bulunamadı");
            }

            return (false, "Docker container kontrol edilemedi");
        }

        if (inspect.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "already running");
        }

        var start = await RunCommandCaptureAsync(docker, $"start {container}", ct);
        return start.ExitCode == 0
            ? (true, "started")
            : (false, "PostgreSQL başlatılamadı");
    }

    private async Task<bool> IsConnectorHealthyAsync(CancellationToken ct)
    {
        if (!await IsPortOpenAsync("127.0.0.1", _settings.ConnectorPort, ct))
        {
            return false;
        }

        var secret = _runtimeEnv.GetConnectorSharedSecret();
        if (string.IsNullOrWhiteSpace(secret))
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_settings.ConnectorPort}/status");
            request.Headers.TryAddWithoutValidation("X-Connector-Secret", secret);
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsConnectorStatusConnectedAsync(CancellationToken ct)
    {
        var secret = _runtimeEnv.GetConnectorSharedSecret();
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_settings.ConnectorPort}/status");
            request.Headers.TryAddWithoutValidation("X-Connector-Secret", secret);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var raw = doc.RootElement.TryGetProperty("status", out var prop) ? prop.GetString() : null;
            return string.Equals(raw, "CONNECTED", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetFoundationWhatsAppStatusAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{_settings.ApiBaseUrl.TrimEnd('/')}/api/foundation/status";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("whatsAppConnection", out var prop)
                ? prop.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private bool TryStopProjectApiOnPort()
    {
        try
        {
            var port = GetApiPort();
            var connections = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(e => e.Port == port)
                .ToList();

            if (connections.Count == 0)
            {
                return false;
            }

            var pid = GetOwningProcessId(port);
            if (pid is null or <= 0)
            {
                return false;
            }

            using var process = Process.GetProcessById(pid.Value);
            var path = process.MainModule?.FileName ?? process.ProcessName;
            if (!path.Contains("led-sup-bot", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("LedSupBot.Api", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex)
        {
            DesktopLog.Error("LocalServiceManager", ex);
            return false;
        }
    }

    private static int? GetOwningProcessId(int port)
    {
        try
        {
            var output = RunCommandCaptureAsync(
                "powershell",
                $"-NoProfile -Command \"(Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess)\"",
                CancellationToken.None).GetAwaiter().GetResult();

            return int.TryParse(output.Output.Trim(), out var pid) ? pid : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> IsApiHealthyAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{_settings.ApiBaseUrl.TrimEnd('/')}/health";
            using var response = await _http.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private StartProcessResult TryStartConnector()
    {
        var connectorDir = Path.Combine(_settings.ProjectRoot, "connector");
        var entry = Path.Combine(connectorDir, "src", "index.js");
        if (!File.Exists(entry))
        {
            return StartProcessResult.Fail("connector/src/index.js bulunamadı");
        }

        var node = FindNodeExecutable();
        if (node is null)
        {
            return StartProcessResult.Fail("Node.js bulunamadı");
        }

        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = "src/index.js",
            WorkingDirectory = connectorDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        ApplyEnvironment(psi, _runtimeEnv.BuildConnectorProcessEnvironment(_settings));
        return StartManagedProcess(psi, "connector");
    }

    private StartProcessResult TryStartApi()
    {
        var project = Path.Combine(_settings.ProjectRoot, "src", "LedSupBot.Api", "LedSupBot.Api.csproj");
        if (!File.Exists(project))
        {
            return StartProcessResult.Fail("LedSupBot.Api.csproj bulunamadı");
        }

        var dotnet = FindExecutable("dotnet.exe") ?? "dotnet";
        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            Arguments = $"run --project \"{project}\" --no-launch-profile",
            WorkingDirectory = _settings.ProjectRoot,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        ApplyEnvironment(psi, _runtimeEnv.BuildApiProcessEnvironment(_settings));
        return StartManagedProcess(psi, "api");
    }

    private StartProcessResult StartManagedProcess(ProcessStartInfo psi, string name)
    {
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return StartProcessResult.Fail($"{name} başlatılamadı: {ex.Message}");
        }

        if (process is null)
        {
            return StartProcessResult.Fail($"{name} process oluşturulamadı");
        }

        var managed = new ManagedProcess(process, Path.Combine(_logDirectory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.log"));
        managed.StartLogCapture();
        _started.Add(managed);
        return StartProcessResult.Ok(managed);
    }

    private static void ApplyEnvironment(ProcessStartInfo psi, IReadOnlyDictionary<string, string> env)
    {
        foreach (var kv in env)
        {
            psi.Environment[kv.Key] = kv.Value;
        }
    }

    private static string? FindNodeExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidate = Path.Combine(programFiles, "nodejs", "node.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return FindExecutable("node.exe");
    }

    private static string? FindExecutable(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    private static async Task<bool> CanRunCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        var result = await RunCommandCaptureAsync(fileName, arguments, ct);
        return result.ExitCode == 0;
    }

    private static async Task<CommandResult> RunCommandCaptureAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new CommandResult(-1, "", "process start failed");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private int GetApiPort()
    {
        if (Uri.TryCreate(_settings.ApiBaseUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            return uri.Port;
        }

        return 5080;
    }

    private static string MapWhatsApp(string? raw) => raw?.ToUpperInvariant() switch
    {
        "CONNECTING" => "Bağlanıyor",
        "AUTHENTICATION_REQUIRED" => "QR gerekli",
        "ERROR" => "Hata",
        _ => "Bağlantı yok",
    };

    private static void FailAll(Action<RuntimeComponentStatus> report, string message)
    {
        foreach (var name in new[] { "PostgreSQL", "Connector", "API", "WhatsApp" })
        {
            report(new RuntimeComponentStatus(name, ComponentState.Failed, message));
        }
    }

    public void StopStartedServices()
    {
        foreach (var process in _started.ToArray())
        {
            process.Dispose();
        }

        _started.Clear();
    }

    public void Dispose()
    {
        StopStartedServices();
        _http.Dispose();
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class StartProcessResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public ManagedProcess Process { get; init; } = null!;

        public static StartProcessResult Ok(ManagedProcess process) =>
            new() { Success = true, Process = process };

        public static StartProcessResult Fail(string message) =>
            new() { Success = false, Message = message };
    }

    private sealed class ManagedProcess : IDisposable
    {
        private readonly Process _process;
        private readonly string _logPath;

        public ManagedProcess(Process process, string logPath)
        {
            _process = process;
            _logPath = logPath;
        }

        public bool HasExited => _process.HasExited;

        public string? GetExitSummary()
        {
            if (!_process.HasExited)
            {
                return null;
            }

            return SanitizeForUi(ReadTail(_logPath));
        }

        public void StartLogCapture()
        {
            _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
            _process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _process.EnableRaisingEvents = true;
        }

        private void AppendLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            try
            {
                File.AppendAllText(_logPath, ProcessLogSanitizer.Redact(line) + Environment.NewLine);
            }
            catch
            {
                /* ignore log failures */
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static string ReadTail(string path, int maxChars = 240)
        {
            if (!File.Exists(path))
            {
                return "Process kapandı";
            }

            var text = File.ReadAllText(path);
            if (text.Length <= maxChars)
            {
                return text.Trim();
            }

            return text[^maxChars..].Trim();
        }

        private static string SanitizeForUi(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Process kapandı";
            }

            var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return oneLine.Length > 120 ? oneLine[..120] + "…" : oneLine;
        }
    }
}

internal static class ProcessLogSanitizer
{
    private static readonly string[] SensitiveKeys =
    [
        "SECRET", "PASSWORD", "API_KEY", "TOKEN", "CONNECTION_STRING"
    ];

    public static string Redact(string line)
    {
        var result = line;
        foreach (var key in SensitiveKeys)
        {
            if (result.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                result = $"[redacted:{key}]";
                break;
            }
        }

        return result;
    }
}
