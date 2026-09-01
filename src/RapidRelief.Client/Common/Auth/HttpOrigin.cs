namespace RapidRelief.Client.Common.Auth;

/// <summary>
/// Same-origin guard shared by <see cref="DevRoleHandler"/> and <see cref="AuthMessageHandler"/> —
/// auth headers must never leak to third-party origins (e.g. tile servers).
/// </summary>
public static class HttpOrigin
{
    public static bool IsRelativeOrSameOrigin(Uri? uri, Uri? baseAddress)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return true;
        }

        return baseAddress is not null && Uri.Compare(
            uri, baseAddress, UriComponents.SchemeAndServer, UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }
}
