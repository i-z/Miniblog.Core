// Callback платежей VK Игр для POPBlocks (ASP.NET Core, без внешних зависимостей).
//
// Подключение к существующему сайту:
//   1. Скопировать файл в папку Controllers (или любую — важен только namespace/маршрут).
//   2. В appsettings.json (или переменную окружения Vk__AppSecret) добавить:
//        "Vk": { "AppSecret": "<Защищённый ключ из настроек VK-приложения>" }
//   3. В настройках приложения VK (раздел «Платежи») указать Callback URL:
//        https://<ваш-домен>/vk/payments
//
// VK шлёт POST (application/x-www-form-urlencoded), подписанный md5-подписью.
// Протокол: https://dev.vk.com/ru/api/payments/notifications/overview

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VkPayments
{
    [ApiController]
    public class VkPaymentsController : ControllerBase
    {
        // Каталог товаров — должен совпадать с productID в ShopSettings игры
        // и со StoreAssets/purchases_vk.csv. Цены в голосах (1 голос ≈ 7 ₽).
        private static readonly Dictionary<string, (int ItemId, string Title, int PriceVotes)> Items = new()
        {
            ["com.candysmith.popblocks.pack1"] = (1, "Горсть монет", 2),
            ["com.candysmith.popblocks.pack2"] = (2, "Мешочек монет", 9),
            ["com.candysmith.popblockspack3"] = (3, "Сундук монет", 14),
            ["com.candysmith.popblocks.pack4"] = (4, "Гора монет", 29),
        };

        private readonly string _appSecret;
        private readonly ILogger<VkPaymentsController> _logger;

        public VkPaymentsController(IConfiguration config, ILogger<VkPaymentsController> logger)
        {
            _appSecret = config["Vk:AppSecret"] ?? string.Empty;
            _logger = logger;
        }

        // Публичный каталог цен для игрового клиента (WebGL забирает его с
        // домена vk-apps.com, поэтому нужен CORS). Секретов не содержит.
        [HttpGet("/vk/payments/catalog")]
        public IActionResult Catalog()
        {
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cache-Control"] = "public, max-age=300";

            return Json(new
            {
                items = Items
                    .Select(kv => new { item = kv.Key, price = kv.Value.PriceVotes, title = kv.Value.Title })
                    .ToArray()
            });
        }

        [HttpPost("/vk/payments")]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult Handle()
        {
            var p = Request.Form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

            if (!CheckSignature(p))
            {
                return Error(10, "Несовпадение вычисленной и переданной подписи.");
            }

            var type = p.GetValueOrDefault("notification_type", "");

            switch (type)
            {
                case "get_item":
                case "get_item_test":
                    {
                        if (!Items.TryGetValue(p.GetValueOrDefault("item", ""), out var item))
                        {
                            return Error(20, "Товара не существует.");
                        }

                        return Json(new
                        {
                            response = new
                            {
                                item_id = item.ItemId,
                                title = item.Title,
                                price = item.PriceVotes,
                            }
                        });
                    }

                case "order_status_change":
                case "order_status_change_test":
                    {
                        if (p.GetValueOrDefault("status") != "chargeable")
                        {
                            return Error(100, $"Неожиданный status: {p.GetValueOrDefault("status")}");
                        }

                        long orderId = long.Parse(p["order_id"]);

                        // Товар выдаёт клиент по событию успеха; здесь достаточно
                        // подтвердить заказ. Для идемповентности/сверок пишем лог —
                        // при желании замените на запись в БД.
                        _logger.LogInformation("VK order {OrderId}: user {UserId} buys {Item}",
                            orderId, p.GetValueOrDefault("user_id"), p.GetValueOrDefault("item"));

                        return Json(new
                        {
                            response = new
                            {
                                order_id = orderId,
                                app_order_id = orderId,
                            }
                        });
                    }

                default:
                    return Error(100, $"Неизвестный notification_type: {type}");
            }
        }

        // sig = md5(конкатенация отсортированных key=value без разделителей + app_secret)
        private bool CheckSignature(Dictionary<string, string> p)
        {
            if (string.IsNullOrEmpty(_appSecret) || !p.TryGetValue("sig", out var sig))
            {
                return false;
            }

            var payload = string.Concat(
                p.Where(kv => kv.Key != "sig")
                 .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                 .Select(kv => $"{kv.Key}={kv.Value}"));

            var hash = Convert.ToHexString(
                MD5.HashData(Encoding.UTF8.GetBytes(payload + _appSecret))).ToLowerInvariant();

            return hash == sig;
        }

        private IActionResult Error(int code, string message) =>
            Json(new { error = new { error_code = code, error_msg = message, critical = true } });

        private IActionResult Json(object value) =>
            new JsonResult(value) { ContentType = "application/json; charset=utf-8" };
    }
}
