using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;
using UserManagement.Contracts.Auth;
using UserManagement.Contracts.DTO;
using UserManagement.Sdk.Abstractions;

namespace UserManagement.Sdk
{
    internal sealed class UserManagementClient : IUserManagementClient
    {
        private readonly HttpClient _http;

        public UserManagementClient(HttpClient http) => _http = http;

        public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            var resp = await _http.PostAsJsonAsync("api/users/authenticate", request, ct);
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";

            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Try to parse ProblemDetails for a better message
                ProblemDetails? prob = null;
                if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    try { prob = JsonSerializer.Deserialize<ProblemDetails>(body); } catch { /* ignore */ }
                }
                var msg = prob?.Detail ?? prob?.Title ?? $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                throw new HttpRequestException(msg);
            }

            // Success → parse AuthResponse
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Expected JSON but got '{contentType}'. Body: {body[..Math.Min(120, body.Length)]}");

            var auth = JsonSerializer.Deserialize<AuthResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return auth;
        }

        public async Task<UpdatePermissionsResponse> UpdatePermissions(UpdatePermissionsRequest operations, CancellationToken ct = default)
        {
            var resp = await _http.PostAsJsonAsync("api/users/updatepermissions", operations, ct);

            resp.EnsureSuccessStatusCode(); // throw if failed

            // if you expect a structured response, deserialize it
            var result = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to update permissions: {result}");
            }
            else
            {
                return new UpdatePermissionsResponse(result);
            }

        }

        public async Task<UserPermissionsDto> GetPermissions(int userId, CancellationToken ct = default)
        {
            var resp = await _http.GetAsync($"api/users/{userId}/permissions", ct);

            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadFromJsonAsync<UserPermissionsDto>(cancellationToken: ct);

        }

        public async Task<object> GetState(int userId, CancellationToken ct = default)
        {
            var resp = await _http.GetAsync($"api/users/{userId}/getstate", ct);

            // Throws if 4xx/5xx
            resp.EnsureSuccessStatusCode();

            // Read JSON
            var json = await resp.Content.ReadAsStringAsync(ct);

            // Optional extra defensive check
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to fetch permissions for user {userId}: {json}");

            // Deserialize to your DTO
            var dto = System.Text.Json.JsonSerializer.Deserialize<object>(json);

            // Safety check (should not happen unless server returns null JSON)
            if (dto == null)
                throw new InvalidOperationException($"Empty or invalid permissions payload for user {userId}.");

            return dto;
        }
    }
}
