using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.PanelFiles;
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
    public async Task Exact_real_P186_numeric_chipset_folder_import_succeeds_through_desktop_request_shape()
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        const string originalFolderName = "P1.86+1065+2018";
        var desktopRoot = Path.Combine(Path.GetTempPath(), "ledsup-desktop-import-tests", Guid.NewGuid().ToString("N"), originalFolderName);
        Directory.CreateDirectory(desktopRoot);
        await File.WriteAllBytesAsync(Path.Combine(desktopRoot, "3.38.hex"), "hex"u8.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(desktopRoot, "P1.86-1065-2018-43S-Flex-E410.rcvbp"), "cfg"u8.ToArray());
        Guid id;
        try
        {
            using var first = PanelFolderImportMultipartFactory.Create(desktopRoot);
            Assert.Equal("multipart/form-data", first.Headers.ContentType!.MediaType);
            var parts = first.ToList();
            Assert.Equal("folderName", parts[0].Headers.ContentDisposition!.Name);
            Assert.Equal(originalFolderName, await parts[0].ReadAsStringAsync());
            Assert.All(parts.Skip(1), part => Assert.Equal("files", part.Headers.ContentDisposition!.Name));
            Assert.Equal(new[] { "3.38.hex", "P1.86-1065-2018-43S-Flex-E410.rcvbp" },
                parts.Skip(1).Select(part => part.Headers.ContentDisposition!.FileName));
            var imported = await client.PostAsync("/api/admin/panel-file-packages/import-folder", first);
            Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
            var body = JsonDocument.Parse(await imported.Content.ReadAsStringAsync()).RootElement;
            id = body.GetProperty("id").GetGuid();
            Assert.Equal(originalFolderName, body.GetProperty("originalFolderName").GetString());
            Assert.Equal("P1.86-1065-2018", body.GetProperty("canonicalKey").GetString());
            Assert.Equal("P1.86", body.GetProperty("pixelPitch").GetString());
            Assert.Equal("1065", body.GetProperty("panelCode").GetString());
            Assert.Equal("2018", body.GetProperty("secondaryCode").GetString());
            Assert.Equal("READY", body.GetProperty("readiness").GetString());
            Assert.Equal(2, body.GetProperty("assets").EnumerateArray().Count(x => x.GetProperty("status").GetString() == "Active"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(desktopRoot)!, true);
        }

        await using (var verificationDb = fixture.CreateDb())
        {
            var persisted = await verificationDb.PanelFilePackages.AsNoTracking().Include(x => x.Assets).SingleAsync(x => x.Id == id);
            Assert.Equal(originalFolderName, persisted.OriginalFolderName);
            Assert.Equal("P1.86", persisted.PixelPitch);
            Assert.Equal("1065", persisted.PanelCode);
            Assert.Equal("2018", persisted.SecondaryCode);
            Assert.Equal("P1.86-1065-2018", persisted.CanonicalKey);
            var active = persisted.Assets.Where(x => x.Status == LedSupBot.Domain.PanelFiles.PanelFileAssetStatus.Active).ToList();
            Assert.Equal(2, active.Count);
            Assert.Contains(active, x => x.AssetType == LedSupBot.Domain.PanelFiles.PanelFileAssetType.FirmwareHex && x.OriginalFileName == "3.38.hex");
            Assert.Contains(active, x => x.AssetType == LedSupBot.Domain.PanelFiles.PanelFileAssetType.ReceiverConfigRcvbp && x.OriginalFileName == "P1.86-1065-2018-43S-Flex-E410.rcvbp");
            Assert.All(active, x => Assert.Contains("panel-packages", x.StorageKey, StringComparison.OrdinalIgnoreCase));
            using var storageScope = fixture.Factory.Services.CreateScope();
            var storage = storageScope.ServiceProvider.GetRequiredService<IMediaStorage>();
            Assert.All(active, x => Assert.True(storage.Exists(x.StorageKey)));
            var expectedBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["3.38.hex"] = "hex"u8.ToArray(),
                ["P1.86-1065-2018-43S-Flex-E410.rcvbp"] = "cfg"u8.ToArray()
            };
            foreach (var asset in active)
            {
                await using var storedFile = await storage.OpenReadAsync(asset.StorageKey);
                await using var bytes = new MemoryStream();
                await storedFile.CopyToAsync(bytes);
                Assert.Equal(expectedBytes[asset.OriginalFileName], bytes.ToArray());
            }
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var reimported = await scope.ServiceProvider.GetRequiredService<IPanelFolderImportService>().ImportAsync(
            originalFolderName,
            [
                new PanelFolderImportFile("one.dat", "one.dat", "application/octet-stream", new MemoryStream("replacement-1"u8.ToArray())),
                new PanelFolderImportFile("two.cfg", "two.cfg", "application/octet-stream", new MemoryStream("replacement-2"u8.ToArray())),
                new PanelFolderImportFile("three.txt", "three.txt", "application/octet-stream", new MemoryStream("replacement-3"u8.ToArray())),
                new PanelFolderImportFile("four.bin", "four.bin", "application/octet-stream", new MemoryStream("replacement-4"u8.ToArray())),
                new PanelFolderImportFile("five.raw", "five.raw", "application/octet-stream", new MemoryStream("replacement-5"u8.ToArray()))
            ]);
        Assert.Equal(id, reimported.Id);
        Assert.Equal(2, reimported.Version);
        Assert.Equal(5, reimported.Assets.Count(x => x.Status == LedSupBot.Domain.PanelFiles.PanelFileAssetStatus.Active));
    }

    [Theory]
    [InlineData("P2.5+1065s+2012", "P2.5-1065S-2012")]
    [InlineData("P4+2038S+2012", "P4-2038S-2012")]
    [InlineData("P1.86+FM6126A+138", "P1.86-FM6126A-138")]
    public async Task Folder_import_accepts_plus_separator_and_case_variants(string folderName, string canonical)
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(folderName), "folderName");
        form.Add(new ByteArrayContent([1]), "files", "random.hex");
        form.Add(new ByteArrayContent([2]), "files", "other.rcvbp");
        var response = await client.PostAsync("/api/admin/panel-file-packages/import-folder", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(canonical, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("canonicalKey").GetString());
    }

    [Theory]
    [InlineData()]
    [InlineData("only.bin")]
    public async Task Folder_import_rejects_fewer_than_two_files(params string[] names)
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent($"P2.5+{Random.Shared.Next(100000, 999999)}S+2012"), "folderName");
        for (var i = 0; i < names.Length; i++) form.Add(new ByteArrayContent([(byte)(i + 1)]), "files", names[i]);
        var response = await client.PostAsync("/api/admin/panel-file-packages/import-folder", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("en az 2 dosya", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Folder_import_accepts_two_three_and_five_arbitrary_files()
    {
        foreach (var count in new[] { 2, 3, 5 })
        {
            var client = fixture.CreateAdminClient();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent($"P2.5+{Random.Shared.Next(100000, 999999)}S+2012"), "folderName");
            for (var i = 0; i < count; i++) form.Add(new ByteArrayContent([(byte)(i + 1)]), "files", $"opaque-{i}.data{i}");
            var response = await client.PostAsync("/api/admin/panel-file-packages/import-folder", form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(count, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
                .GetProperty("assets").EnumerateArray().Count(x => x.GetProperty("status").GetString() == "Active"));
        }
    }

    [Fact]
    public async Task Folder_import_rejects_non_plus_package_separator()
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        using var form = FolderForm("P2.5-1065S-2012");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/admin/panel-file-packages/import-folder", form)).StatusCode);
    }

    [Fact]
    public async Task Folder_import_rejects_nested_relative_paths()
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent($"P2.5+{Random.Shared.Next(100000, 999999)}S+2012"), "folderName");
        form.Add(new ByteArrayContent([1]), "files", "nested/a.hex");
        form.Add(new ByteArrayContent([2]), "files", "b.rcvbp");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/admin/panel-file-packages/import-folder", form)).StatusCode);
    }

    [Fact]
    public async Task Folder_import_second_storage_failure_cleans_first_asset_and_exposes_no_package()
    {
        var storage = new FailSecondPanelFolderStorage();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMediaStorage>();
            services.AddSingleton<IMediaStorage>(storage);
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        using var form = FolderForm($"P2.5+{code}+2012");

        var response = await client.PostAsync("/api/admin/panel-file-packages/import-folder", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(storage.StoredKeys);
        await using var db = fixture.CreateDb();
        Assert.False(await db.PanelFilePackages.AnyAsync(x => x.CanonicalKey == $"P2.5-{code}-2012"));
    }

    [Fact]
    public async Task Folder_import_database_failure_removes_both_private_files_and_exposes_no_package()
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
        using var form = FolderForm($"P2.5+{code}+2012");

        var response = await client.PostAsync("/api/admin/panel-file-packages/import-folder", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var db = fixture.CreateDb();
        Assert.False(await db.PanelFilePackages.AnyAsync(x => x.CanonicalKey == $"P2.5-{code}-2012"));
        var filesAfter = Directory.GetFiles(fixture.MediaRoot, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(filesBefore.SetEquals(filesAfter));
    }

    [Fact]
    public async Task PF2_Mutating_routes_require_admin_authentication()
    {
        var client = fixture.CreateAdminClient();
        var body = new { pixelPitch = "P2.5", panelCode = "1065S", secondaryCode = "2012" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/admin/panel-file-packages", body)).StatusCode);
        using var form = FileForm("a.hex", [1]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/admin/panel-file-packages/{Guid.NewGuid()}/assets/firmware", form)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/admin/panel-file-packages/{Guid.NewGuid()}")).StatusCode);
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
        Assert.Equal(HttpStatusCode.OK, activeRewrite.StatusCode);

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
        Assert.Equal($"P2.5-{code}-2013", reloaded.GetProperty("canonicalKey").GetString());
        Assert.Equal(3, reloaded.GetProperty("assets").GetArrayLength());

        var defaultList = await client.GetFromJsonAsync<JsonElement>($"/api/admin/panel-file-packages?search={code}");
        Assert.DoesNotContain(defaultList.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        var archivedList = await client.GetFromJsonAsync<JsonElement>($"/api/admin/panel-file-packages?search={code}&status=Archived");
        Assert.Contains(archivedList.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task PF2_Admin_delete_soft_deletes_and_preserves_audit_record()
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        var code = $"{Random.Shared.Next(100000, 999999)}D";
        var create = await client.PostAsJsonAsync("/api/admin/panel-file-packages", new { pixelPitch = "P2.5", panelCode = code, secondaryCode = "2012" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/panel-file-packages/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/admin/panel-file-packages/{Guid.NewGuid()}")).StatusCode);

        await using var db = fixture.CreateDb();
        var package = await db.PanelFilePackages.SingleAsync(x => x.Id == id);
        Assert.Equal(PanelFilePackageStatus.Archived, package.Status);
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/admin/panel-file-packages?search={code}");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
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

    [Fact]
    public async Task UNDO_01_04_reimport_can_restore_previous_assets_without_deleting_history()
    {
        var client = fixture.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new { password = fixture.AdminPassword })).StatusCode);
        var code = $"{Random.Shared.Next(100000, 999999)}S";
        using (var first = FolderForm($"P2.5+{code}+2012"))
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/admin/panel-file-packages/import-folder", first)).StatusCode);
        using (var second = FolderForm($"P2.5+{code}+2012"))
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/admin/panel-file-packages/import-folder", second)).StatusCode);

        await using var beforeDb = fixture.CreateDb();
        var package = await beforeDb.PanelFilePackages.AsNoTracking().SingleAsync(x => x.PanelCode == code);
        Assert.Equal(2, package.Version);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/admin/panel-file-packages/{package.Id}/rollback", null)).StatusCode);

        await using var afterDb = fixture.CreateDb();
        var restored = await afterDb.PanelFilePackages.Include(x => x.Assets).AsNoTracking().SingleAsync(x => x.Id == package.Id);
        Assert.Equal(1, restored.Version);
        Assert.Equal(2, restored.Assets.Count(x => x.Version == 1 && x.Status == PanelFileAssetStatus.Active));
        Assert.Equal(2, restored.Assets.Count(x => x.Version == 2 && x.Status == PanelFileAssetStatus.Archived));
    }

    private static MultipartFormDataContent FileForm(string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", name); return form;
    }

    private static MultipartFormDataContent FolderForm(string folderName)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(folderName), "folderName");
        form.Add(new ByteArrayContent("hex"u8.ToArray()), "files", "unrelated-name.hex");
        form.Add(new ByteArrayContent("cfg"u8.ToArray()), "files", "other-random-name.rcvbp");
        return form;
    }
}

internal sealed class FailSecondPanelFolderStorage : IMediaStorage
{
    private int _calls;
    public HashSet<string> StoredKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    public async Task<StoredMediaReference> StoreAsync(Stream content, string contentType, MediaStoreRequest request, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _calls) == 2) throw new IOException("simulated_second_storage_failure");
        await content.CopyToAsync(Stream.Null, cancellationToken);
        var key = $"private/panel-packages/{Guid.NewGuid():N}{request.FileExtension}";
        StoredKeys.Add(key);
        return new StoredMediaReference(key, contentType, content.CanSeek ? content.Length : 1);
    }
    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) { StoredKeys.Remove(storageKey); return Task.CompletedTask; }
    public bool Exists(string storageKey) => StoredKeys.Contains(storageKey);
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
