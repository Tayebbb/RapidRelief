using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ⑳–㉗.</summary>
public sealed class ProfileTests : IClassFixture<TestingWebAppFactory>
{
    // 1×1 transparent PNG.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly TestingWebAppFactory _factory;

    public ProfileTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact] // ⑳
    public async Task Get_profile_with_bearer_returns_own_data_including_roles()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, email) = await AuthTestClient.RegisterFreshUserAsync(client);

        var response = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/profile", session.AccessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(session.UserId, data.GetProperty("id").GetGuid());
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal("Test User", data.GetProperty("displayName").GetString());
        Assert.False(data.GetProperty("hasPhoto").GetBoolean());
        Assert.Equal([Roles.Citizen], data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    [Fact] // ㉑ — FakeAuth coexistence pin: same GUID as the seeded admin row
    public async Task Get_profile_with_dev_role_header_returns_seeded_admin_row()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/profile");
        request.Headers.Add(FakeAuthHandler.HeaderName, Roles.Admin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(FakeAuthHandler.SeedUserIds[Roles.Government], data.GetProperty("id").GetGuid());
        Assert.Equal("government1@rr.dev", data.GetProperty("email").GetString());
    }

    [Fact] // ㉒ — PUT happy + persistence + email immutability
    public async Task Put_profile_updates_mutable_fields_and_email_stays_immutable()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, email) = await AuthTestClient.RegisterFreshUserAsync(client);

        var put = AuthTestClient.BearerRequest(HttpMethod.Put, "/api/auth/profile", session.AccessToken);
        put.Content = JsonContent.Create(new
        {
            displayName = "Renamed Person",
            phoneNumber = "01911111111",
            emergencyContact = "New contact",
        });
        var putResponse = await client.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var get = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/profile", session.AccessToken));
        using var body = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("Renamed Person", data.GetProperty("displayName").GetString());
        Assert.Equal("01911111111", data.GetProperty("phoneNumber").GetString());
        Assert.Equal("New contact", data.GetProperty("emergencyContact").GetString());
        Assert.Equal(email, data.GetProperty("email").GetString());
    }

    [Fact] // ㉒b
    public async Task Put_profile_with_empty_display_name_returns_400()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var put = AuthTestClient.BearerRequest(HttpMethod.Put, "/api/auth/profile", session.AccessToken);
        put.Content = JsonContent.Create(new { displayName = "" });
        var response = await client.SendAsync(put);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("DisplayName", out _));
    }

    [Fact] // ㉓ — photo upload happy + byte roundtrip
    public async Task Photo_upload_png_returns_has_photo_true_and_get_streams_identical_bytes()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var upload = await UploadPhotoAsync(client, session.AccessToken, TinyPng, "avatar.png", "image/png");

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        using (var body = JsonDocument.Parse(await upload.Content.ReadAsStringAsync()))
        {
            Assert.True(body.RootElement.GetProperty("data").GetProperty("hasPhoto").GetBoolean());
        }

        var get = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/profile/photo", session.AccessToken));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TinyPng, await get.Content.ReadAsByteArrayAsync());
    }

    [Fact] // ㉔ — extension whitelist via IFileStorage
    public async Task Photo_upload_exe_returns_400_keyed_file()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var response = await UploadPhotoAsync(client, session.AccessToken, [1, 2, 3], "malware.exe", "application/octet-stream");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("file", out _));
    }

    [Fact] // post-review item 5 — endpoint is image-only even though IFileStorage allows more (.pdf/.mp4)
    public async Task Photo_upload_pdf_returns_400_keyed_file()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var response = await UploadPhotoAsync(client, session.AccessToken, [1, 2, 3], "scan.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("file", out _));
    }

    [Fact] // ㉕ — oversize (factory caps FileStorage:MaxSizeBytes at 64 KiB)
    public async Task Photo_upload_oversize_returns_400_keyed_file()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);
        var oversize = new byte[70_000];

        var response = await UploadPhotoAsync(client, session.AccessToken, oversize, "big.png", "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("file", out _));
    }

    [Fact] // ㉔b — missing form file
    public async Task Photo_upload_without_file_field_returns_400()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var request = AuthTestClient.BearerRequest(HttpMethod.Post, "/api/auth/profile/photo", session.AccessToken);
        request.Content = new MultipartFormDataContent { { new StringContent("no file"), "note" } };
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact] // ㉖
    public async Task Get_photo_when_none_uploaded_returns_404()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var response = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/profile/photo", session.AccessToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory] // ㊗ — anonymous requests that ROUTE to the endpoint are challenged (415-shaped
    // requests without a matching Content-Type fall to the /api 404 fallback by design).
    [InlineData("GET", "/api/auth/profile", null)]
    [InlineData("PUT", "/api/auth/profile", "json")]
    [InlineData("POST", "/api/auth/profile/photo", "multipart")]
    [InlineData("GET", "/api/auth/profile/photo", null)]
    public async Task Anonymous_profile_requests_return_401(string method, string url, string? contentKind)
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = contentKind switch
            {
                "json" => JsonContent.Create(new { displayName = "x" }),
                "multipart" => new MultipartFormDataContent { { new ByteArrayContent([1]), "file", "x.png" } },
                _ => null,
            },
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<HttpResponseMessage> UploadPhotoAsync(HttpClient client, string accessToken,
        byte[] bytes, string fileName, string contentType)
    {
        var request = AuthTestClient.BearerRequest(HttpMethod.Post, "/api/auth/profile/photo", accessToken);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        request.Content = new MultipartFormDataContent { { fileContent, "file", fileName } };
        return client.SendAsync(request);
    }
}
