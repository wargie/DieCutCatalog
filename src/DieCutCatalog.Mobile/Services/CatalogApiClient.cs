using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DieCutCatalog.Mobile.Models;

namespace DieCutCatalog.Mobile.Services;

public sealed class CatalogApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = new()
    {
        BaseAddress = new Uri("https://diecutcatalog.duckdns.org/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static CatalogApiClient Current { get; } = new();
    public string? AccessToken { get; private set; }
    public EmployeeProfileDto? Profile { get; private set; }

    public async Task<EmployeeProfileDto> LoginAsync(string email, string password)
    {
        var result = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", new { email, password }, false);
        AccessToken = result.AccessToken;
        Profile = result.Profile;
        SessionProfile.Apply(result.Profile);
        return result.Profile;
    }

    public async Task<IReadOnlyList<KnifeListItem>> GetCatalogAsync()
    {
        var items = new List<KnifeListItem>();
        var page = 1;
        while (true)
        {
            var result = await SendAsync<PagedResultDto<DieCutDto>>(
                HttpMethod.Get, $"api/die-cuts/?page={page}&pageSize=200");
            items.AddRange(result.Items.Select(item => item.ToListItem()));
            if (items.Count >= result.Total || result.Items.Count == 0) break;
            page++;
        }
        return items;
    }

    public async Task<KnifeListItem> GetKnifeAsync(Guid id) =>
        (await SendAsync<DieCutDto>(HttpMethod.Get, $"api/die-cuts/{id}")).ToListItem();

    public async Task<KnifeListItem> AddCirculationAsync(Guid id, long? quantity, decimal? runLengthMeters) =>
        (await SendAsync<DieCutDto>(HttpMethod.Post, $"api/die-cuts/{id}/circulations",
            new { quantity, runLengthMeters })).ToListItem();

    public async Task<EmployeeProfileDto> SaveProfileAsync(
        string password, string firstName, string lastName, string? position,
        string email, string? phone, string? additionalContacts)
    {
        var currentEmail = Profile?.Email ?? SessionProfile.Email;
        await LoginAsync(currentEmail, password);
        var updated = await SendAsync<EmployeeProfileDto>(HttpMethod.Put, "api/employees/me",
            new { firstName, lastName, position, phone, additionalContacts });

        if (!string.Equals(updated.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync(HttpMethod.Post, "api/employees/me/change-email", new { currentPassword = password, newEmail = email });
            updated = updated with { Email = email };
        }

        Profile = updated;
        SessionProfile.Apply(updated);
        return updated;
    }

    public async Task<EmployeeProfileDto> UploadPhotoAsync(FileResult photo)
    {
        await using var stream = await photo.OpenReadAsync();
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(photo.ContentType ?? "application/octet-stream");
        form.Add(file, "photo", photo.FileName);
        var profile = await SendAsync<EmployeeProfileDto>(HttpMethod.Post, "api/employees/me/photo", form);
        Profile = profile;
        SessionProfile.Apply(profile);
        return profile;
    }

    public Uri? GetPhotoUri() => string.IsNullOrWhiteSpace(Profile?.PhotoUrl)
        ? null
        : new Uri(http.BaseAddress!, Profile.PhotoUrl.TrimStart('/'));

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool authorize = true)
    {
        using var response = await SendCoreAsync(method, path, body, authorize);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new ApiException("Сервер вернул пустой ответ.");
    }

    private async Task SendAsync(HttpMethod method, string path, object? body = null) =>
        (await SendCoreAsync(method, path, body, true)).Dispose();

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, bool authorize)
    {
        using var request = new HttpRequestMessage(method, path);
        if (authorize && AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, options: JsonOptions)
        };

        HttpResponseMessage response;
        try { response = await http.SendAsync(request); }
        catch (TaskCanceledException exception) { throw new ApiException("Сервер не ответил вовремя.", exception); }
        catch (HttpRequestException exception) { throw new ApiException("Не удалось подключиться к серверу.", exception); }

        if (response.IsSuccessStatusCode) return response;
        var message = await ReadErrorAsync(response) ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Неверная электронная почта или пароль.",
            HttpStatusCode.PreconditionRequired => "Сначала необходимо сменить временный пароль в ПК-клиенте.",
            HttpStatusCode.TooManyRequests => "Слишком много попыток. Повторите позже.",
            _ => $"Сервер отклонил запрос ({(int)response.StatusCode})."
        };
        response.Dispose();
        throw new ApiException(message);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        try { return (await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions))?.Error; }
        catch (JsonException) { return null; }
    }

    private sealed record ApiError(string Error);
}

public sealed class ApiException(string message, Exception? inner = null) : Exception(message, inner);
