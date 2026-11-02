using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Miniblog.Core.Controllers;

/// <summary>Verifies SDK purchases; the game owns durable, idempotent fulfillment.</summary>
[ApiController]
[EnableCors("PikabuPayments")]
public sealed class PikabuPaymentsController(IConfiguration config) : ControllerBase
{
    private static readonly HashSet<string> Products = new(StringComparer.Ordinal)
    {
        "com.candysmith.popblocks.pack1",
        "com.candysmith.popblocks.pack2",
        "com.candysmith.popblockspack3",
        "com.candysmith.popblocks.pack4",
    };

    [HttpPost("/pikabu/payments")]
    [RequestSizeLimit(32768)]
    public IActionResult Handle([FromBody] PurchaseRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var secret = config["Pikabu:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return StatusCode(503, new { success = false, error = "not_configured" });
        }

        var purchase = Verify(request.SignedData, secret);
        var player = Verify(request.SignedPlayerData, secret);
        if (purchase is null || player is null)
        {
            return Unauthorized(new { success = false, error = "invalid_signature" });
        }

        if (!GetBoolean(player, "isAuthorized", out var authorized) || !authorized ||
            !GetString(player, "id", out var playerId) ||
            !GetString(purchase, "playerId", out var ownerId) || playerId != ownerId ||
            !GetString(purchase, "purchaseId", out var purchaseId) ||
            !GetString(purchase, "productId", out var productId) ||
            !GetBoolean(purchase, "isConsumable", out var consumable) || !consumable ||
            !GetBoolean(purchase, "isTestPurchase", out var test))
        {
            return BadRequest(new { success = false, error = "invalid_purchase" });
        }

        if (!Products.Contains(productId))
        {
            return BadRequest(new { success = false, error = "unknown_product" });
        }

        if (test && !config.GetValue<bool>("Pikabu:AllowTestPurchases"))
        {
            return BadRequest(new { success = false, error = "test_purchase_disabled" });
        }

        // This is verification, not fulfillment. Never consume before the game
        // durably grants the product exactly once for this purchaseId.
        return Ok(new { success = true, purchaseId, productId, playerId, isConsumable = consumable, isTestPurchase = test });
    }

    private static Dictionary<string, JsonElement>? Verify(string? token, string secret)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 12000)
        {
            return null;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            using var header = JsonDocument.Parse(Decode(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "HS256")
            {
                return null;
            }

            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
            if (!CryptographicOperations.FixedTimeEquals(expected, Decode(parts[2])))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Decode(parts[1]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Decode(string value) => Convert.FromBase64String(
        value.Replace('-', '+').Replace('_', '/').PadRight(value.Length + (4 - value.Length % 4) % 4, '='));

    private static bool GetString(Dictionary<string, JsonElement> data, string key, out string value)
    {
        value = string.Empty;
        if (!data.TryGetValue(key, out var field) || field.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = field.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool GetBoolean(Dictionary<string, JsonElement> data, string key, out bool value)
    {
        value = false;
        if (!data.TryGetValue(key, out var field) || field.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = field.GetBoolean();
        return true;
    }

    public sealed record PurchaseRequest(string? SignedData, string? SignedPlayerData);
}
