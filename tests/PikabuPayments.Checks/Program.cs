using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Miniblog.Core.Controllers;

const string secret = "test-only-secret";
string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
string Sign(object payload, string key = secret, string alg = "HS256")
{
    var data = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg, typ = "JWT" })) + "." + Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
    return data + "." + Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes(data)));
}
object Purchase(string product = "com.candysmith.popblocks.pack1", string player = "player1", bool test = false) =>
    new { productId = product, playerId = player, purchaseId = "purchase1", isConsumable = true, isTestPurchase = test };
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Pikabu:SecretKey"] = secret }).Build();
var controller = new PikabuPaymentsController(config) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
var playerToken = Sign(new { id = "player1", isAuthorized = true });
var count = 0;
void Check(string name, string? token, int expected, string? player = null)
{
    var result = (ObjectResult)controller.Handle(new(token, player ?? playerToken));
    if (result.StatusCode != expected) throw new Exception($"{name}: expected {expected}, got {result.StatusCode}");
    count++;
}
Check("valid", Sign(Purchase()), 200);
Check("retries verify successfully", Sign(Purchase()), 200);
Check("wrong signature", Sign(Purchase(), "wrong-secret"), 401);
Check("algorithm restriction", Sign(Purchase(), alg: "none"), 401);
Check("broken JWT", "a.b.c", 401);
Check("missing JWT", null, 401);
Check("oversized JWT", new string('a', 12001), 401);
Check("unknown product", Sign(Purchase(product: "unknown")), 400);
Check("wrong player", Sign(Purchase(player: "someone-else")), 400);
Check("player token cannot be used as purchase", playerToken, 400);
Check("unauthorized player", Sign(Purchase()), 400, Sign(new { id = "player1", isAuthorized = false }));
Check("invalid player signature", Sign(Purchase()), 401, Sign(new { id = "player1", isAuthorized = true }, "bad"));
Check("sandbox disabled", Sign(Purchase(test: true)), 400);
config["Pikabu:AllowTestPurchases"] = "true";
Check("sandbox enabled", Sign(Purchase(test: true)), 200);
config["Pikabu:SecretKey"] = "";
Check("missing configuration", Sign(Purchase()), 503);
Console.WriteLine($"Passed {count} payment verification checks.");
