# Покупки Пикабу Игры

Официальная документация (проверена 2 ноября 2026):

- https://games.pikabu.ru/sdk/docs/sdk/iap
- https://games.pikabu.ru/sdk/docs/sdk/verification

В отличие от VK, Пикабу не вызывает callback сервера. Игра получает покупку
через SDK и отправляет `purchase.getSignedData()` на сервер. Это JWT HS256
с полями `purchaseId`, `productId`, `playerId`, `isConsumable`, `isTestPurchase`.
Сервер проверяет подпись секретным ключом игры. Срок действия токена документацией
не задан; подпись сама по себе не защищает от повторного начисления.

## Настройка

1. В Студии Пикабу создайте четыре **расходуемых** товара с теми же ID, что у VK:
   `com.candysmith.popblocks.pack1`, `com.candysmith.popblocks.pack2`,
   `com.candysmith.popblockspack3`, `com.candysmith.popblocks.pack4`.
   Отсутствующая точка в третьем ID сохранена для совместимости.
2. Цены задаются в Студии. Для отображения используйте `sdk.store.getProducts()`:
   `price` выражена в копейках; `formattedPrice` уже содержит валюту.
   Цены VK в голосах здесь не используются.
3. На сервере задайте переменную окружения `Pikabu__SecretKey` секретным ключом
   этой игры из Студии. Не добавляйте ключ в клиент или репозиторий.
4. В `Pikabu:AllowedOrigins` укажите точный origin страницы, из которой игра
   обращается к серверу (схема и хост, без пути). По умолчанию доступ из другого
   origin закрыт. Это origin iframe игры, он может отличаться от games.pikabu.ru.
5. Для sandbox на отдельном тестовом сервере включите
   `Pikabu__AllowTestPurchases=true`. По умолчанию тестовые покупки отклоняются.

## Серверный контракт

`POST /pikabu/payments`, `Content-Type: application/json`:

```json
{ "signedData": "<purchase JWT>", "signedPlayerData": "<player JWT>" }
```

Подписанные данные игрока получаются через `sdk.player.getSignedData()`.
Сервер проверяет авторизацию и совпадение игрока с владельцем покупки,
каталог, тип товара и режим тестирования. Ответ успеха:

```json
{ "success": true, "purchaseId": "...", "productId": "...", "playerId": "...", "isConsumable": true, "isTestPurchase": false }
```

Это **проверка покупки**, а не начисление. В этом проекте нет игрового баланса
или базы покупок; как и с VK, награду выдаёт сама игра. Повторный запрос возвращает
успех. Не подключайте endpoint к безусловному начислению монет.

## Подключение в игре

Подключите `/js/pikabu-payments.js` с этого сайта или скопируйте его в игровой
билд. После инициализации SDK:

```javascript
const payments = createPikabuPayments(sdk, 'https://YOUR_HOST/pikabu/payments',
  async (verified) => {
    // Реализуется в проекте игры: сохранить награду И purchaseId одной операцией.
    // Если purchaseId уже обработан — успешно завершиться без повторной награды.
    // Баланс должен принадлежать verified.playerId. Тестовый баланс изолировать.
    await gameSave.grantProductOnce(verified);
  });
const recovery = await payments.restore(); // При каждом запуске и после авторизации.
// Проверить rejected в recovery и показать возможность повторить обработку.
const products = await payments.getProducts();
const result = await payments.purchase('com.candysmith.popblocks.pack1');
// result.success === false: отмена/ошибка SDK, подробности в result.reason.
```

`gameSave.grantProductOnce` — контракт для реализации в игре, не готовая функция
этого сервера. До её реализации интеграция не готова к реальным начислениям.
Для серверного баланса перенесите атомарное начисление и уникальность purchaseId
в БД сервера и возвращайте успех только после транзакции.

Помощник вызывает `consumePurchase` только после успешного сохранения награды.
При сбое сети или consume покупка остаётся для повторной обработки; сохранённый
purchaseId защищает от повторного начисления. Постоянные товары пока не поддержаны.

## Проверка

`dotnet run --project tests/PikabuPayments.Checks` проверяет серверную валидацию
без сторонних тестовых пакетов (требуется .NET 10 SDK).
`node --test tests/pikabu-payments.test.cjs` проверяет порядок начисления,
восстановление после сбоя consume, ошибки подписи/сохранения и отмену покупки.
Реальные покупки проверяются отдельно в sandbox Студии с игровым билдом.
