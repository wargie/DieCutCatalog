namespace DieCutCatalog.Application.Security;

public static class ServerAddressPolicy
{
    public static bool TryCreateBaseUri(
        string? serverAddress,
        bool allowInsecureRemoteHttp,
        out Uri? baseUri,
        out string? error)
    {
        baseUri = null;
        error = null;

        var address = serverAddress?.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Укажите корректный адрес сервера, например https://catalog.company.ru.";
            return false;
        }

        var isLocalHttp = uri.Scheme == Uri.UriSchemeHttp
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host == "127.0.0.1");
        if (uri.Scheme == Uri.UriSchemeHttp && !isLocalHttp && !allowInsecureRemoteHttp)
        {
            error = "Для удалённого сервера требуется защищённое соединение HTTPS.";
            return false;
        }

        baseUri = new Uri(uri.ToString().TrimEnd('/') + "/");
        return true;
    }
}