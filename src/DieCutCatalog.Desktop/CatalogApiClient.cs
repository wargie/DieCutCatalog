using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Application.Security;

namespace DieCutCatalog.Desktop;

internal sealed partial class CatalogApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public CatalogApiClient() : this(new HttpClient())
    {
    }

    internal CatalogApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }
    private Uri? _baseAddress;

    public string? AccessToken { get; private set; }
    public DateTimeOffset? SessionExpiresAt { get; private set; }
    public string? ServerAddress => _baseAddress?.ToString().TrimEnd('/');

    public void Configure(string serverAddress)
    {
        var developmentMode = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
        if (!ServerAddressPolicy.TryCreateBaseUri(serverAddress, developmentMode, out var uri, out var error))
            throw new CatalogApiException(error!);

        _baseAddress = uri;
        AccessToken = null;
        SessionExpiresAt = null;
    }

    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        var result = await SendAsync<LoginResult>(HttpMethod.Post, "api/auth/login", new { email, password }, false);
        AccessToken = result.AccessToken;
        SessionExpiresAt = result.ExpiresAt;
        return result;
    }

    public async Task<EmployeeProfile> RestoreSessionAsync(StoredClientSession session)
    {
        Configure(session.ServerAddress);
        AccessToken = session.AccessToken;
        SessionExpiresAt = session.ExpiresAt;
        try
        {
            return await SendAsync<EmployeeProfile>(HttpMethod.Post, "api/auth/resume");
        }
        catch
        {
            AccessToken = null;
            SessionExpiresAt = null;
            throw;
        }
    }

    public Task DisconnectAsync() =>
        AccessToken is null ? Task.CompletedTask : SendAsync(HttpMethod.Post, "api/auth/disconnect");

    public StoredClientSession? GetStoredSession(string clientVersion) =>
        ServerAddress is not null && AccessToken is not null && SessionExpiresAt is not null
            ? new StoredClientSession(clientVersion, ServerAddress, AccessToken, SessionExpiresAt.Value)
            : null;
    public async Task LogoutAsync()
    {
        if (AccessToken is null) return;
        try { await SendAsync(HttpMethod.Post, "api/auth/logout"); }
        finally
        {
            AccessToken = null;
            SessionExpiresAt = null;
        }
    }

    public Task<EmployeeProfile> UpdateProfileAsync(string firstName, string lastName, string? position, string? phone, string? additionalContacts) =>
        SendAsync<EmployeeProfile>(HttpMethod.Put, "api/employees/me", new { firstName, lastName, position, phone, additionalContacts });

    public Task ChangePasswordAsync(string currentPassword, string newPassword) =>
        SendAsync(HttpMethod.Post, "api/employees/me/change-password", new { currentPassword, newPassword });

    public Task ChangeEmailAsync(string currentPassword, string newEmail) =>
        SendAsync(HttpMethod.Post, "api/employees/me/change-email", new { currentPassword, newEmail });

    public Task<IReadOnlyList<EmployeeActivityReport>> GetEmployeeDirectoryAsync(string password) =>
        SendAsync<IReadOnlyList<EmployeeActivityReport>>(HttpMethod.Post, "api/employees/directory", new { password });

    public Task<EmployeeProfile> DeleteEmployeeAsync(Guid employeeId, string password) =>
        SendAsync<EmployeeProfile>(HttpMethod.Delete, $"api/employees/{employeeId}", new { password });
    public Task<CreateEmployeeResult> CreateEmployeeAsync(string email, string firstName, string lastName, string? position, string? phone, bool isAdministrator) =>
        SendAsync<CreateEmployeeResult>(HttpMethod.Post, "api/employees", new { email, firstName, lastName, position, phone, isAdministrator });

    public async Task<EmployeeProfile> UploadPhotoAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
        content.Add(fileContent, "photo", Path.GetFileName(filePath));
        return await SendAsync<EmployeeProfile>(HttpMethod.Post, "api/employees/me/photo", content);
    }

    public async Task<byte[]?> DownloadPhotoAsync(string? photoUrl)
    {
        if (string.IsNullOrWhiteSpace(photoUrl)) return null;
        using var request = CreateRequest(HttpMethod.Get, photoUrl);
        using var response = await SendCoreAsync(request, allowNotFound: true);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool authorize = true)
    {
        using var request = CreateRequest(method, path, authorize);
        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, options: _jsonOptions)
        };
        using var response = await SendCoreAsync(request);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions)
            ?? throw new CatalogApiException("Сервер вернул пустой ответ.");
    }

    private async Task SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = CreateRequest(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: _jsonOptions);
        using var response = await SendCoreAsync(request);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool authorize = true)
    {
        if (_baseAddress is null) throw new CatalogApiException("Адрес сервера не задан.");
        var request = new HttpRequestMessage(method, new Uri(_baseAddress, path.TrimStart('/')));
        if (authorize && AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, bool allowNotFound = false)
    {
        try
        {
            var response = await _httpClient.SendAsync(request);
            try
            {
                if (!allowNotFound || response.StatusCode != HttpStatusCode.NotFound)
                    await EnsureSuccessAsync(response);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogApiException("Не удалось подключиться к серверу. Проверьте адрес и сеть.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new CatalogApiException("Сервер не ответил вовремя.", exception);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string? message = null;
        try { message = (await response.Content.ReadFromJsonAsync<ApiError>())?.Error; }
        catch (JsonException) { }
        message ??= response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Неверная почта или пароль.",
            HttpStatusCode.Forbidden => "Недостаточно прав для выполнения операции.",
            HttpStatusCode.TooManyRequests => "Слишком много попыток. Повторите позже.",
            _ => $"Сервер отклонил запрос ({(int)response.StatusCode})."
        };
        throw new CatalogApiException(message);
    }

    private static string GetContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    public void Dispose() => _httpClient.Dispose();
    private sealed record ApiError(string Error);
}

internal sealed class CatalogApiException(string message, Exception? innerException = null)
    : Exception(message, innerException);
