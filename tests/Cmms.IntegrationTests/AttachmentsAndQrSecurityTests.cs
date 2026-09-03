using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cmms.Modules.Assets.Domain;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.Attachments.Infrastructure;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// M4's Definition of Done, security half: "QR scan never grants authorization beyond what the
/// technician's role already allows (proven by a negative test); attachment upload rejects path
/// traversal / disallowed types / oversized files" (docs/06-milestones.md § M4).
/// </summary>
[Collection("Postgres")]
public sealed class AttachmentsAndQrSecurityTests : IAsyncLifetime
{
    private const string Password = "T3st!Password#1";

    private readonly PostgresFixture _postgres;
    private CmmsWebApplicationFactory _factory = null!;
    private Guid _siteAId;
    private Guid _siteBId;
    private Guid _assetAId;
    private string _plannerEmail = string.Empty;
    private string _technicianEmail = string.Empty;
    private string _siteBTechnicianEmail = string.Empty;

    public AttachmentsAndQrSecurityTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _factory = new CmmsWebApplicationFactory(_postgres.ConnectionString);
        using (_factory.CreateClient())
        {
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _plannerEmail = $"planner.att.{suffix}@example.test";
        _technicianEmail = $"tech.att.{suffix}@example.test";
        _siteBTechnicianEmail = $"tech.att.b.{suffix}@example.test";

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
        var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        var siteA = new Site($"SITE-ATT-A-{suffix}", "Site Att A", "UTC");
        var siteB = new Site($"SITE-ATT-B-{suffix}", "Site Att B", "UTC");
        identityDb.Sites.AddRange(siteA, siteB);
        await identityDb.SaveChangesAsync();
        _siteAId = siteA.Id;
        _siteBId = siteB.Id;

        var asset = new Asset(_siteAId, $"PUMP-{suffix}", "Attachments Test Pump", "Rotating Equipment", AssetCriticality.B);
        assetsDb.Assets.Add(asset);
        await assetsDb.SaveChangesAsync();
        _assetAId = asset.Id;

        var planner = new ApplicationUser(_plannerEmail, "Planner Att");
        Assert.True((await userManager.CreateAsync(planner, Password)).Succeeded);
        var technician = new ApplicationUser(_technicianEmail, "Technician Att");
        Assert.True((await userManager.CreateAsync(technician, Password)).Succeeded);
        var siteBTechnician = new ApplicationUser(_siteBTechnicianEmail, "Technician Att B");
        Assert.True((await userManager.CreateAsync(siteBTechnician, Password)).Succeeded);

        identityDb.SiteMemberships.Add(new SiteMembership(planner.Id, _siteAId, RoleCode.Planner));
        identityDb.SiteMemberships.Add(new SiteMembership(technician.Id, _siteAId, RoleCode.Technician));
        identityDb.SiteMemberships.Add(new SiteMembership(siteBTechnician.Id, _siteBId, RoleCode.Technician));
        await identityDb.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> CreateInProgressWorkOrderAsync(HttpClient plannerClient, HttpClient technicianClient)
    {
        var createResponse = await plannerClient.PostJsonWithCsrfAsync("/work-orders", new
        {
            siteId = _siteAId,
            title = $"Att test {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            assetId = _assetAId,
            locationId = (Guid?)null
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = created.GetProperty("id").GetGuid();
        await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/publish", new { });
        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/self-claim", new { });
        await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/start", new { });
        return workOrderId;
    }

    private static byte[] BuildValidJpeg()
    {
        using var image = new Image<Rgba32>(20, 15);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    // ---------- Storage-layer path traversal (unit-level: no DB/HTTP needed) ----------

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("clean/../../../etc/passwd")]
    [InlineData("quarantine/../secrets")]
    [InlineData("not-a-valid-key-format")]
    [InlineData("clean/tooshort")]
    public async Task LocalDiskAttachmentStorage_rejects_any_key_that_is_not_its_own_generated_format(string maliciousKey)
    {
        var storage = new LocalDiskAttachmentStorage(new FakeConfiguration(
            Path.Combine(Path.GetTempPath(), "cmms-attachments-test-" + Guid.NewGuid().ToString("N"))));

        await Assert.ThrowsAsync<ArgumentException>(() => storage.WriteAsync(maliciousKey, new MemoryStream([1, 2, 3])));
    }

    private sealed class FakeConfiguration(string localStorageRoot) : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key]
        {
            get => key == "Attachments:LocalStorageRoot" ? localStorageRoot : null;
            set => throw new NotSupportedException();
        }

        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
    }

    // ---------- Full pipeline: upload -> finalize -> link to a PhotoRequired checklist item ----------

    [Fact]
    public async Task Full_upload_finalize_flow_produces_an_active_attachment_that_satisfies_a_photorequired_item()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        var createItem = await plannerClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/checklist-items", new
        {
            itemType = "PhotoRequired",
            label = "Before photo",
            isRequired = true
        });
        var item = await createItem.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = item.GetProperty("id").GetGuid();

        var intentResponse = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/attachments/upload-intents",
            new { declaredContentType = "image/jpeg", originalFileName = "evidence.jpg" });
        Assert.Equal(HttpStatusCode.Created, intentResponse.StatusCode);
        var intent = await intentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var intentId = intent.GetProperty("id").GetGuid();

        var uploadResponse = await technicianClient.PutBytesWithCsrfAsync($"/attachments/upload-intents/{intentId}/bytes", BuildValidJpeg(), "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        var finalizeResponse = await technicianClient.PostJsonWithCsrfAsync($"/attachments/upload-intents/{intentId}/finalize", new { });
        Assert.Equal(HttpStatusCode.Created, finalizeResponse.StatusCode);
        var attachment = await finalizeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = attachment.GetProperty("id").GetGuid();
        Assert.Equal("image/jpeg", attachment.GetProperty("contentType").GetString());

        var resolve = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/checklist-items/{itemId}/resolve",
            new { booleanValue = (bool?)null, numericValue = (decimal?)null, selectedOption = (string?)null, noteText = (string?)null, attachmentId });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        var complete = await technicianClient.PostJsonWithCsrfAsync($"/work-orders/{workOrderId}/complete", new { });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var download = await technicianClient.GetAsync($"/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task Finalize_rejects_bytes_that_are_not_actually_a_recognizable_image()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        var intentResponse = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/attachments/upload-intents",
            new { declaredContentType = "image/jpeg", originalFileName = (string?)null });
        var intent = await intentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var intentId = intent.GetProperty("id").GetGuid();

        // Not an image at all — an executable-looking blob claiming to be a JPEG. This is the
        // "magic-byte verification" boundary docs/02 asks for: ImageSharp's decoder itself must
        // reject this, not a filename/extension check.
        var fakeBytes = "<script>alert(1)</script> MZ\x90\x00 fake-payload"u8.ToArray();
        var uploadResponse = await technicianClient.PutBytesWithCsrfAsync($"/attachments/upload-intents/{intentId}/bytes", fakeBytes, "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        var finalizeResponse = await technicianClient.PostJsonWithCsrfAsync($"/attachments/upload-intents/{intentId}/finalize", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, finalizeResponse.StatusCode);
    }

    [Fact]
    public async Task Create_upload_intent_rejects_a_disallowed_declared_content_type()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        // No PDFs/manuals/SVG in v1 (docs/02's raster-only narrowing) — this must be rejected up
        // front, before an upload is ever attempted.
        var intentResponse = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/attachments/upload-intents",
            new { declaredContentType = "application/pdf", originalFileName = "manual.pdf" });
        Assert.Equal(HttpStatusCode.BadRequest, intentResponse.StatusCode);

        var svgIntentResponse = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/attachments/upload-intents",
            new { declaredContentType = "image/svg+xml", originalFileName = "evil.svg" });
        Assert.Equal(HttpStatusCode.BadRequest, svgIntentResponse.StatusCode);
    }

    [Fact]
    public async Task Uploading_more_bytes_than_the_intents_max_is_rejected_as_too_large()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);

        var intentResponse = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/attachments/upload-intents",
            new { declaredContentType = "image/jpeg", originalFileName = (string?)null });
        var intent = await intentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var intentId = intent.GetProperty("id").GetGuid();

        // Hard cap is 15 MB (AttachmentsEndpoints.HardMaxBytes) — 16 MB of junk must be rejected
        // as 413 without ever reaching image decode.
        var oversized = new byte[16 * 1024 * 1024];
        var uploadResponse = await technicianClient.PutBytesWithCsrfAsync($"/attachments/upload-intents/{intentId}/bytes", oversized, "image/jpeg");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, uploadResponse.StatusCode);
    }

    [Fact]
    public async Task A_technician_from_a_different_site_cannot_download_another_sites_attachment()
    {
        using var plannerClient = _factory.CreateClient();
        await plannerClient.LoginAsync(_plannerEmail, Password);
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);
        using var siteBTechnicianClient = _factory.CreateClient();
        await siteBTechnicianClient.LoginAsync(_siteBTechnicianEmail, Password);

        var workOrderId = await CreateInProgressWorkOrderAsync(plannerClient, technicianClient);
        var intentResponse = await technicianClient.PostJsonWithCsrfAsync(
            $"/work-orders/{workOrderId}/attachments/upload-intents",
            new { declaredContentType = "image/jpeg", originalFileName = (string?)null });
        var intent = await intentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var intentId = intent.GetProperty("id").GetGuid();
        await technicianClient.PutBytesWithCsrfAsync($"/attachments/upload-intents/{intentId}/bytes", BuildValidJpeg(), "image/jpeg");
        var finalizeResponse = await technicianClient.PostJsonWithCsrfAsync($"/attachments/upload-intents/{intentId}/finalize", new { });
        var attachment = await finalizeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = attachment.GetProperty("id").GetGuid();

        var crossSiteDownload = await siteBTechnicianClient.GetAsync($"/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.NotFound, crossSiteDownload.StatusCode);
    }

    // ---------- QR: scanning a tag is never a capability ----------

    [Fact]
    public async Task Scanning_an_asset_qr_locator_while_unauthenticated_returns_401_not_asset_data()
    {
        using var anonymousClient = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var asset = await assetsDb.Assets.FindAsync(_assetAId);

        var response = await anonymousClient.GetAsync($"/assets/by-qr/{asset!.QrLocator}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Scanning_an_asset_qr_locator_grants_exactly_the_same_visibility_ordinary_asset_read_would()
    {
        using var technicianClient = _factory.CreateClient();
        await technicianClient.LoginAsync(_technicianEmail, Password);
        using var siteBTechnicianClient = _factory.CreateClient();
        await siteBTechnicianClient.LoginAsync(_siteBTechnicianEmail, Password);

        await using var scope = _factory.Services.CreateAsyncScope();
        var assetsDb = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var asset = await assetsDb.Assets.FindAsync(_assetAId);

        var sameSiteScan = await technicianClient.GetAsync($"/assets/by-qr/{asset!.QrLocator}");
        Assert.Equal(HttpStatusCode.OK, sameSiteScan.StatusCode);
        var scanned = await sameSiteScan.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_assetAId, scanned.GetProperty("id").GetGuid());

        // The QR locator is opaque but not a bearer capability — a technician from a different
        // site who scans (or is handed) the same tag gets exactly the 404 an ordinary cross-site
        // GET /assets/{id} would, never asset data.
        var crossSiteScan = await siteBTechnicianClient.GetAsync($"/assets/by-qr/{asset.QrLocator}");
        Assert.Equal(HttpStatusCode.NotFound, crossSiteScan.StatusCode);
    }
}
