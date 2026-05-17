namespace Miniblog.Core.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Miniblog.Core.Models;

using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

[AllowAnonymous]
public class GeoInfoController(IHttpClientFactory httpClientFactory) : Controller
{
    [Route("/geoinfo")]
    public async Task<IActionResult> Index()
    {
        var ip = GetClientIp();

        var model = new GeoInfoViewModel { IpAddress = ip };

        var isLocal = ip is "::1" or "127.0.0.1" or "unknown"
            || ip.StartsWith("10.", StringComparison.Ordinal)
            || ip.StartsWith("192.168.", StringComparison.Ordinal)
            || ip.StartsWith("172.", StringComparison.Ordinal);

        if (isLocal)
        {
            model.ErrorMessage = "Local IP address — geolocation is not available.";
            return this.View(model);
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // ip-api.com — free geolocation API, no key required
            var response = await client.GetAsync(
                new Uri($"http://ip-api.com/json/{ip}?fields=status,message,country,city"))
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    .ConfigureAwait(false);

                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
                {
                    model.Country = root.TryGetProperty("country", out var country) ? country.GetString() : null;
                    model.City = root.TryGetProperty("city", out var city) ? city.GetString() : null;
                }
                else
                {
                    model.ErrorMessage = "Could not determine location for this IP address.";
                }
            }
        }
        catch (Exception)
        {
            model.ErrorMessage = "Failed to contact geolocation service.";
        }

        return this.View(model);
    }

    private string GetClientIp()
    {
        // Check proxy headers (X-Forwarded-For, X-Real-IP) first
        var headers = this.HttpContext.Request.Headers;

        if (headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            // X-Forwarded-For may contain multiple IPs: "client, proxy1, proxy2"
            var first = forwardedFor.ToString().Split(',', StringSplitOptions.TrimEntries)[0];
            if (!string.IsNullOrEmpty(first))
            {
                return first;
            }
        }

        if (headers.TryGetValue("X-Real-IP", out var realIp))
        {
            var value = realIp.ToString().Trim();
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return this.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
