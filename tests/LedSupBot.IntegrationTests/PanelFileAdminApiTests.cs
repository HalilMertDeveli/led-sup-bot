using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LedSupBot.IntegrationTests;

[Collection("whatsapp-webhook")]
public sealed class PanelFileAdminApiTests(WhatsAppWebhookFixture fixture)
{
    [Fact]
    public async Task PF2_Mutating_routes_require_admin_authentication()
    {
        var client = fixture.CreateAdminClient();
        var body = new { pixelPitch = "P2.5", panelCode = "1065S", secondaryCode = "2012" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/admin/panel-file-packages", body)).StatusCode);
        using var form = FileForm("a.hex", [1]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/admin/panel-file-packages/{Guid.NewGuid()}/assets/firmware", form)).StatusCode);
    }

    [Fact]
    public async Task PF2_Admin_create_filter_upload_activate_detail_and_archive_flow()
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        var create = await client.PostAsJsonAsync("/api/admin/panel-file-packages", new { pixelPitch = "p2.5", panelCode = code.ToLowerInvariant(), secondaryCode = "2012" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"P2.5-{code}-2012", created.GetProperty("canonicalKey").GetString());
        Assert.Equal("NOT_DELIVERY_ELIGIBLE", created.GetProperty("readiness").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/admin/panel-file-packages", new { pixelPitch = "P2.5", panelCode = code, secondaryCode = "2012" })).StatusCode);

        using (var wrong = FileForm("firmware.hex.exe", [1]))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", wrong)).StatusCode);
        using (var mismatch = FileForm("receiver.rcvbp", [1]))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", mismatch)).StatusCode);
        using (var traversal = FileForm("../firmware.hex", [1]))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", traversal)).StatusCode);
        using (var oversized = FileForm("large.hex", [1, 2, 3, 4, 5]))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", oversized)).StatusCode);
        using (var hex = FileForm("firmware.hex", "abc"u8.ToArray()))
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", hex)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/activate", null)).StatusCode);
        using (var duplicate = FileForm("firmware-copy.hex", "abc"u8.ToArray()))
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", duplicate)).StatusCode);
        using (var config = FileForm("scan.rcvbp", [4, 5, 6]))
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/receiver-config", config)).StatusCode);
        var activate = await client.PostAsync($"/api/admin/panel-file-packages/{id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.Equal("READY", JsonDocument.Parse(await activate.Content.ReadAsStringAsync()).RootElement.GetProperty("readiness").GetString());

        var activeRewrite = await client.PutAsJsonAsync($"/api/admin/panel-file-packages/{id}", new
        {
            pixelPitch = "P2.5", panelCode = code, secondaryCode = "2013", version = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, activeRewrite.StatusCode);

        using (var replacement = FileForm("firmware-v2.hex", "abcd"u8.ToArray()))
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", replacement)).StatusCode);

        var list = await client.GetAsync($"/api/admin/panel-file-packages?search={code}&pitch=P2.5&status=Active");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var item = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement.GetProperty("items")[0];
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.False(item.TryGetProperty("storageKey", out _));
        var assets = item.GetProperty("assets").EnumerateArray().ToList();
        Assert.Contains(assets, x => x.GetProperty("sha256").GetString()!.StartsWith("ba7816bf") && x.GetProperty("status").GetString() == "Archived");
        Assert.Contains(assets, x => x.GetProperty("originalFileName").GetString() == "firmware-v2.hex" && x.GetProperty("version").GetInt32() == 2 && x.GetProperty("status").GetString() == "Active");

        var readyFilter = await client.GetFromJsonAsync<JsonElement>("/api/admin/panel-file-packages?readiness=READY&limit=1");
        Assert.Contains(readyFilter.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);

        var archived = await client.PostAsync($"/api/admin/panel-file-packages/{id}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal("NOT_DELIVERY_ELIGIBLE", JsonDocument.Parse(await archived.Content.ReadAsStringAsync()).RootElement.GetProperty("readiness").GetString());

        var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/admin/panel-file-packages/{id}");
        Assert.Equal($"P2.5-{code}-2012", reloaded.GetProperty("canonicalKey").GetString());
        Assert.Equal(3, reloaded.GetProperty("assets").GetArrayLength());
    }

    [Fact]
    public async Task PF2_Storage_failure_creates_no_asset_row_and_returns_bounded_error()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMediaStorage>();
            services.AddSingleton<IMediaStorage, ThrowingMediaStorage>();
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        var create = await client.PostAsJsonAsync("/api/admin/panel-file-packages", new { pixelPitch = "P2.5", panelCode = code, secondaryCode = "2012" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        using var file = FileForm("firmware.hex", [1]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", file)).StatusCode);
        await using var db = fixture.CreateDb();
        Assert.False(await db.PanelFilePackageAssets.AnyAsync(x => x.PanelFilePackageId == id));
    }

    [Fact]
    public async Task PF2_Database_failure_removes_newly_stored_asset_and_creates_no_row()
    {
        var filesBefore = Directory.Exists(fixture.MediaRoot)
            ? Directory.GetFiles(fixture.MediaRoot, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(fixture.ConnectionString).AddInterceptors(new FailPanelAssetSaveInterceptor()));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        var create = await client.PostAsJsonAsync("/api/admin/panel-file-packages", new { pixelPitch = "P2.5", panelCode = code, secondaryCode = "2012" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        using var file = FileForm("firmware.hex", [1]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync($"/api/admin/panel-file-packages/{id}/assets/firmware", file)).StatusCode);

        await using var db = fixture.CreateDb();
        Assert.False(await db.PanelFilePackageAssets.AnyAsync(x => x.PanelFilePackageId == id));
        var filesAfter = Directory.GetFiles(fixture.MediaRoot, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(filesBefore.SetEquals(filesAfter));
    }

    private static MultipartFormDataContent FileForm(string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", name); return form;
    }
}

internal sealed class ThrowingMediaStorage : IMediaStorage
{
    public Task<StoredMediaReference> StoreAsync(Stream content, string contentType, MediaStoreRequest request, CancellationToken cancellationToken = default) =>
        throw new IOException("simulated_storage_failure");
    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool Exists(string storageKey) => false;
}

internal sealed class FailPanelAssetSaveInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.ChangeTracker.Entries<LedSupBot.Domain.Entities.PanelFilePackageAsset>()
            .Any(x => x.State == EntityState.Added) == true)
        {
            throw new DbUpdateException("simulated_panel_asset_persistence_failure");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
