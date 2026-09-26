using CentralizedLogging.Contracts.DTO;
using CentralizedLogging.Contracts.Models;
using CentralizedLogging.Sdk.Abstractions;
using Microsoft.AspNetCore.Mvc;
using SharedLibrary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CentralizedLogging.Sdk
{
    internal sealed class CentralizedLoggingClient : ICentralizedLoggingClient
    {
        private readonly HttpClient _http;

        public CentralizedLoggingClient(HttpClient http) => _http = http;

        public async Task<List<GetAllErrorsResponseModel>> GetAllErrorAsync(CancellationToken ct = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/errorlogs");
            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {                
                throw new PermissionDeniedException((int)response.StatusCode);                
            }

            response.EnsureSuccessStatusCode(); // for 200 OK only

            return await response.Content.ReadFromJsonAsync<List<GetAllErrorsResponseModel>>(cancellationToken: ct);
        }

        public async Task LogErrorAsync(CreateErrorLogDto request, CancellationToken ct)
        {            
            var resp = await _http.PostAsJsonAsync("api/errorlogs", request, ct);            
        }
    }
}
