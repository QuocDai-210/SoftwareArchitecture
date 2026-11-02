using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SwiftRide.Matching.Infrastructure;

public sealed class KeycloakDriverIdentity(IHttpClientFactory clients, IConfiguration config) : IDriverIdentity
{
    private async Task<HttpClient> Client(CancellationToken ct)
    {
        var client = clients.CreateClient("identity-admin");
        using var token = await client.PostAsync("realms/swiftride/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "client_credentials", ["client_id"] = "swiftride-ops", ["client_secret"] = config["Auth:AdminClientSecret"] ?? "" }), ct);
        if (!token.IsSuccessStatusCode) throw new RuleException("Chưa cấu hình quyền cấp tài khoản Driver.", ErrorKind.Unavailable);
        var data = await token.Content.ReadFromJsonAsync<JsonElement>(ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("access_token").GetString());
        return client;
    }

    private static void Ensure(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new RuleException("Keycloak chưa hoàn tất thao tác tài khoản. Có thể thử lại.", ErrorKind.Unavailable);
    }

    public async Task<string> Provision(DriverApplication application, string temporaryPassword, CancellationToken ct)
    {
        using var client = await Client(ct);
        var path = "admin/realms/swiftride/users";
        using var response = await client.GetAsync(path + "?exact=true&username=" + Uri.EscapeDataString(application.Username), ct);
        Ensure(response);
        var users = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (users.GetArrayLength() == 0)
        {
            using var created = await client.PostAsJsonAsync(path, new { username = application.Username, email = application.Email,
                firstName = application.Name, lastName = "Driver", enabled = false,
                attributes = new { swiftrideApplicationId = new[] { application.Id.ToString() } },
                credentials = new[] { new { type = "password", value = temporaryPassword, temporary = true } } }, ct);
            if (created.StatusCode != HttpStatusCode.Conflict) Ensure(created);
            using var lookup = await client.GetAsync(path + "?exact=true&username=" + Uri.EscapeDataString(application.Username), ct);
            Ensure(lookup); users = await lookup.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        if (users.GetArrayLength() != 1 || !users[0].TryGetProperty("attributes", out var attributes)
            || !attributes.TryGetProperty("swiftrideApplicationId", out var ids) || ids[0].GetString() != application.Id.ToString())
            throw new RuleException("Tên đăng nhập đã thuộc tài khoản khác.");
        var subject = users[0].GetProperty("id").GetString()!;
        using var roles = await client.GetAsync(path + "/" + subject + "/role-mappings/realm", ct);
        Ensure(roles); var existing = await roles.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var remove = new HttpRequestMessage(HttpMethod.Delete, path + "/" + subject + "/role-mappings/realm") { Content = JsonContent.Create(existing) };
        using var removed = await client.SendAsync(remove, ct); Ensure(removed);
        using var roleResponse = await client.GetAsync("admin/realms/swiftride/roles/driver", ct); Ensure(roleResponse);
        var role = await roleResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var assigned = await client.PostAsJsonAsync(path + "/" + subject + "/role-mappings/realm", new[] { role }, ct); Ensure(assigned);
        return subject;
    }

    public async Task Enable(string subject, bool enabled, CancellationToken ct)
    {
        if (!Guid.TryParse(subject, out _)) throw new RuleException("Danh tính không hợp lệ.", ErrorKind.Validation);
        using var client = await Client(ct);
        using var response = await client.PutAsJsonAsync("admin/realms/swiftride/users/" + subject, new { enabled }, ct); Ensure(response);
    }
}
