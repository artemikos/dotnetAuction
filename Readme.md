# 🪷 Lily Market — Campus Auction Platform

Серверная часть кампусного маркетплейса, где верифицированные студенты могут выставлять товары на аукцион, делать ставки в реальном времени и получать живые уведомления прямо на телефоне.

## Быстрый старт

### Требования

- .NET 8 SDK
- PostgreSQL 14+

### Запуск

```bash
# 1. Клонировать репозиторий
git clone <repo-url>
cd Auction

# 2. Настроить строку подключения к базе данных в appsettings.json

# 3. Запустить приложение 
dotnet run
```

### Запуск тестов

```bash
dotnet test
```


## Архитектурное решение

### Выбранный подход: Гибридный (вариант Б)

Проект реализует гибридный подход: команды обрабатываются через REST-эндпоинты, а события реального времени через SignalR.

**REST отвечает за команды:**
- `POST /api/auctions` — создать аукцион
- `POST /api/auctions/{id}/bids` — сделать ставку
- `DELETE /api/auctions/{id}` — отменить аукцион
- `POST /api/auctions/{id}/confirm-sale` — подтвердить продажу

**SignalR отвечает за события:**
- `BidPlaced` — новая ставка принята, рассылается всем в группе аукциона
- `AuctionEnded` — аукцион завершён (по времени или через BuyNow)
- `AuctionSold` — продавец подтвердил сделку
- `Outbid` — персональное уведомление перебитому участнику
- `AuctionEnding` — предупреждение о завершении через 5 минут
- `AuctionWon` — персональное уведомление победителю
- `NoBidsEnded` — продавцу, если аукцион завершился без ставок
- `SaleConfirmed` — продавцу и победителю при подтверждении продажи

---

## Структура проекта

```
auction_full_refactored/
├── LilyMarket.sln
│
├── Auction/                          # Основной проект
│   ├── Controllers/
│   │   ├── AuctionsController.cs     # REST API аукционов и ставок
│   │   ├── AuthController.cs         # Регистрация и вход
│   │   └── PagesController.cs        # MVC-страницы (HTML)
│   │
│   ├── DTOs/
│   │   ├── AuctionDtos.cs            # AuctionSummaryDto, AuctionDetailDto, CreateAuctionRequest
│   │   └── BidDtos.cs                # BidDto, PagedResponse<T>
│   │
│   ├── Models/
│   │   └── DomainModels.cs           # User, AuctionItem, Bid, AuctionPhoto, AuctionStatus
│   │
│   ├── Data/
│   │   └── AuctionDbContext.cs       # EF Core контекст, конфигурация, seed-данные
│   │
│   ├── Services/
│   │   ├── AuctionService.cs         # CRUD аукционов, маппинг entity → DTO
│   │   ├── BidService.cs             # Валидация и сохранение ставок, concurrency-защита
│   │   ├── NotificationService.cs    # Персональные SignalR-уведомления
│   │   ├── AuctionHub.cs             # SignalR Hub: группы, реконнект, push состояния
│   │   ├── AuctionCompletionWorker.cs # BackgroundService: завершение по endTime
│   │   └── AuthService.cs            # JWT-генерация, валидация email
│   │
│   ├── Views/
│   │   ├── Auctions/
│   │   │   ├── Index.cshtml          # Список аукционов с фильтрами
│   │   │   ├── Detail.cshtml         # Страница аукциона с историей ставок
│   │   │   ├── Create.cshtml         # Форма создания
│   │   │   └── Edit.cshtml           # Форма редактирования
│   │   └── Shared/
│   │       ├── _Layout.cshtml        # Общий layout
│   │       └── _AuthModal.cshtml     # Модальное окно авторизации
│   │
│   ├── Program.cs                    # Конфигурация DI, middleware, маршруты
│   ├── appsettings.json
│   └── appsettings.Development.json
│
└── Auction.Tests/                    # Тестовый проект
    ├── TestBase.cs                   # InMemory БД, моки SignalR, фабрики данных
    ├── BidValidationTests.cs         # Все правила валидации ставок
    ├── AuctionCrudTests.cs           # CRUD операции и queries
    ├── AuctionCompletionTests.cs     # Логика завершения аукционов
    ├── ConcurrentBiddingTests.cs     # Одновременные ставки, целостность состояния
    ├── NotificationTests.cs          # Проверка SignalR-событий
    └── AuctionLifecycleTests.cs      # Сквозные сценарии полного цикла
```

### API

#### Получить список аукционов

```
GET /api/auctions
```

Параметры запроса:

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| page | int | 1 | Номер страницы |
| pageSize | int | 10 | Размер страницы |
| status | string | Active | Active / Ended / Sold / Canceled |
| category | string | — | Tech, Books, Furniture, Clothing, Sports, Art |
| sort | string | ending_soon | ending_soon / newest / price_asc / price_desc / most_bids |
| search | string | — | Поиск по названию (substring, case-insensitive) |

Ответ `200`:
```json
{
  "total": 47,
  "page": 1,
  "pageSize": 10,
  "items": [
    {
      "id": 1,
      "title": "MacBook Pro 13\" M1",
      "category": "Tech",
      "condition": "Good",
      "coverImageUrl": "https://...",
      "currentBid": 450.00,
      "startingBid": 300.00,
      "buyNowPrice": 800.00,
      "bidCount": 7,
      "endTime": "2025-06-15T18:00:00Z",
      "status": "Active",
      "sellerName": "Alice Johnson",
      "winnerName": null
    }
  ]
}
```

#### Получить детали аукциона

```
GET /api/auctions/{id}
```

Ответ `200` включает полное описание, все фото и последние 20 ставок (в убывающем порядке).

```json
{
  "id": 1,
  "title": "MacBook Pro 13\" M1",
  "description": "Great condition...",
  "category": "Tech",
  "condition": "Good",
  "photoUrls": ["https://..."],
  "currentBid": 450.00,
  "startingBid": 300.00,
  "minimumIncrement": 25.00,
  "buyNowPrice": 800.00,
  "bidCount": 7,
  "endTime": "2025-06-15T18:00:00Z",
  "status": "Active",
  "pickupLocation": "Main Library Entrance",
  "sellerName": "Alice Johnson",
  "sellerId": 1,
  "winnerId": null,
  "winnerName": null,
  "recentBids": [
    {
      "id": 23,
      "auctionId": 1,
      "bidderName": "Bob Smith",
      "amount": 450.00,
      "placedAt": "2025-06-14T12:34:56Z",
      "currentHighestBid": 450.00,
      "bidCount": 7
    }
  ]
}
```

#### Создать аукцион

```
POST /api/auctions
Authorization: Bearer <token>
Content-Type: application/json

{
  "title": "MacBook Pro 13\" M1",
  "description": "Great condition, barely used",
  "category": "Tech",
  "condition": "Good",
  "startingBid": 300.00,
  "minimumIncrement": 25.00,
  "buyNowPrice": 800.00,
  "endTime": "2025-06-15T18:00:00Z",
  "pickupLocation": "Main Library Entrance"
}
```

Ответ `201 Created` с телом `AuctionSummaryDto`.

#### Обновить аукцион

```
PUT /api/auctions/{id}
Authorization: Bearer <token>
Content-Type: application/json
```

Тело аналогично созданию. Доступно только продавцу, только до первой ставки.

#### Отменить аукцион

```
DELETE /api/auctions/{id}
Authorization: Bearer <token>
```

Доступно только продавцу, только до первой ставки, только для активных аукционов.

Ответ `200`:
```json
{ "message": "Auction cancelled" }
```

#### Сделать ставку

```
POST /api/auctions/{id}/bids
Authorization: Bearer <token>
Content-Type: application/json

{
  "amount": 475.00
}
```

Ответ `200` — `BidDto`:
```json
{
  "id": 24,
  "auctionId": 1,
  "bidderName": "Bob Smith",
  "amount": 475.00,
  "placedAt": "2025-06-14T13:00:00Z",
  "currentHighestBid": 475.00,
  "bidCount": 8,
  "isBuyNow": false,
  "winnerId": null
}
```


#### Подтвердить продажу

```
POST /api/auctions/{id}/confirm-sale
Authorization: Bearer <token>
```

Переводит аукцион из `Ended` в `Sold`. Доступно только продавцу после завершения.

---

## SignalR Hub

### Подключение

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/auctionHub", {
        accessTokenFactory: () => localStorage.getItem('token')
    })
    .withAutomaticReconnect()
    .build();
```

JWT передаётся через query string `?access_token=...`


### События, получаемые клиентом

| Событие | Кому | Данные | Описание |
|---|---|---|---|
| `AuctionDetails` | Вызывающему | `AuctionDetailDto` | Актуальное состояние при `JoinAuctionGroup` |
| `BidPlaced` | Группе аукциона | `BidDto` | Новая ставка принята |
| `AuctionEnded` | Группе аукциона | `{ id, title, winnerId?, winnerName?, finalPrice?, status }` | Аукцион завершён |
| `AuctionSold` | Группе аукциона | `{ auctionId }` | Продавец подтвердил сделку |
| `AuctionCanceled` | Группе аукциона | `{ auctionId }` | Аукцион отменён |
| `NewAuction` | Всем клиентам | `AuctionSummaryDto` | Создан новый аукцион |
| `Outbid` | Перебитому участнику | `{ type, message, auctionId }` | Персональное: вашу ставку перебили |
| `AuctionEnding` | Всем биддерам аукциона | `{ type, message, auctionId }` | Завершение через 5 минут |
| `AuctionWon` | Победителю | `{ type, message, auctionId }` | Персональное: вы победили |
| `NoBidsEnded` | Продавцу | `{ type, message, auctionId }` | Аукцион завершился без ставок |
| `SaleConfirmed` | Продавцу и победителю | `{ type, message, auctionId }` | Продажа подтверждена |


---

## Аутентификация

Используется JWT Bearer токен. Токен включает клеймы:
- `NameIdentifier` — числовой id пользователя
- `Name` — displayName
- `Email` — email

Токен необходимо передавать в заголовке:
```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

---

## Тестирование

### Запуск

```bash
dotnet test
dotnet test LilyMarket.sln
```

### Покрытие

#### BidValidationTests 

Корректное принятие ставки, ставка в стартовую и минимальную цена, ставка на свой аукцион, ставка на завершённый аукцион, ставка по кнопке BuyNow, ставка выше чем сумма BuyNow, ставка после своей прошлой ставки.

#### AuctionCrudTests 

CRUD операции: создание с невалидными параметрами, редактирование/отмена чужого или после ставок, подтверждение продажи, запросы с фильтрацией, поиском и пагинацией.

#### AuctionCompletionTests

Завершение аукциона, корректный победитель аукциона, Если ставок не было отсутствие победителя, оповещения после завершения аукциона, продавца если небыло ставок, покупателя если выиграл аукцион, несколько аукционов одновременно заканчиваются

#### ConcurrentBiddingTests 
Тесты на параллельные ставки, одинаковые суммы в один момент, первого пропустит, второго отклонит если ставка меньше минимальной новой.


#### NotificationTests 

Проверяет каждый метод NotificationService : правильное имя SignalR-события, правильное количество вызовов, содержимое сообщений.

#### AuctionLifecycleTests — 7 тестов

Полный жизненный цикл аукциона от создания до завершения с полной историей ставок и правильными уведомлениями, цикл отмены, BuyNow, цикл без ставок.

---


### База данных

База данных создаётся автоматически при первом запуске через `EnsureCreatedAsync()`.

### AuctionItem

| Поле | Тип | Описание |
|---|---|---|
| Id | int | Первичный ключ |
| Title | string(200) | Название лота |
| Description | string(2000) | Описание лота |
| Category | string(50) | Tech, Books, Furniture, Clothing, Sports, Art |
| Condition | string(50) | New, Like New, Good, Fair |
| StartingBid | decimal | Начальная ставка |
| MinimumIncrement | decimal | Минимальный шаг ставки |
| BuyNowPrice | decimal? | Цена "купить сейчас" |
| EndTime | DateTime | Время завершения аукциона |
| Status | AuctionStatus | Active / Ended / Sold / Canceled |
| CurrentHighestBid | decimal? | Текущая наивысшая ставка|
| BidCount | int | Количество ставок|
| SellerId | int | Номер продавца |
| WinnerId | int? | Номер победителя аукциона |

### Bid

| Поле | Тип | Описание |
|---|---|---|
| Id | int | Первичный ключ |
| AuctionId | int | Номер аукциона |
| BidderId | int | Номер участника аукциона |
| Amount | decimal | Сумма ставки |
| PlacedAt | DateTime | Время размещения ставки|

---

## Логирование

Логирование покрывает полный жизненный цикл аукциона. По логам можно восстановить всю историю без чтения кода:

[INF] POST /api/auctions
[INF] Auction 42 created by user 1
[INF] POST /api/auctions/42/bids
[INF] Bid accepted: AuctionId=42 BidderId=3 Amount=150 IsBuyNow=False
[INF] Outbid notification: UserId=2 AuctionId=42 NewAmount=150
[INF] AuctionCompletionWorker: Processing 1 expired auctions
[INF] Auction 42 ended. Winner=3 FinalPrice=150
[INF] Winner notification: UserId=3 AuctionId=42
[INF] Auction 42 confirmed as sold by seller 1
[INF] SaleConfirmed notification: AuctionId=42 Seller=1 Winner=3


## Использование ИИ

При разработке проекта использовался Claude (Anthropic) в качестве справочного инструмента для уточнений, консультаций, исправления ошибок компиляции и проверки корректности кода
