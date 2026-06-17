using Auction.Data;
using Auction.Models;
using Auction.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auction.Tests;

/// <summary>
/// Общая база: каждый тест получает чистую изолированную InMemory-базу.
/// Реальный PostgreSQL не нужен — dotnet test работает без настройки окружения.
/// </summary>
public abstract class TestBase : IDisposable
{
    protected readonly AuctionDbContext Db;
    protected readonly BidService BidService;
    protected readonly AuctionService AuctionService;
    protected readonly Mock<IHubContext<AuctionHub>> HubMock;
    protected readonly Mock<IClientProxy> ClientProxyMock;

    protected TestBase()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // уникальная БД на каждый тест
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Db = new AuctionDbContext(options);

        // Мок SignalR — перехватывает все SendAsync вызовы
        ClientProxyMock = new Mock<IClientProxy>();
        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.User(It.IsAny<string>())).Returns(ClientProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(ClientProxyMock.Object);
        clientsMock.Setup(c => c.All).Returns(ClientProxyMock.Object);

        HubMock = new Mock<IHubContext<AuctionHub>>();
        HubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        BidService = new BidService(Db, NullLogger<BidService>.Instance);
        AuctionService = new AuctionService(Db, NullLogger<AuctionService>.Instance);
    }

    // ─── Фабрики тестовых данных ──────────────────────────────────────────────

    protected User CreateUser(string name = "Alice", string email = "alice@uni.edu")
    {
        var user = new User
        {
            DisplayName = name,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass"),
            CreatedAt = DateTime.UtcNow
        };
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    protected AuctionItem CreateAuction(
        int sellerId,
        decimal startingBid = 10m,
        decimal minimumIncrement = 5m,
        decimal? buyNowPrice = null,
        DateTime? endTime = null,
        AuctionStatus status = AuctionStatus.Active)
    {
        var auction = new AuctionItem
        {
            Title = "Test Item",
            Description = "Test",
            Category = "Tech",
            Condition = "Good",
            StartingBid = startingBid,
            MinimumIncrement = minimumIncrement,
            BuyNowPrice = buyNowPrice,
            EndTime = endTime ?? DateTime.UtcNow.AddHours(1),
            PickupLocation = "Library",
            SellerId = sellerId,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            Photos = new List<AuctionPhoto>
            {
                new() { Url = "https://example.com/photo.jpg", IsCover = true, Order = 0 }
            }
        };
        Db.Auctions.Add(auction);
        Db.SaveChanges();
        return auction;
    }

    protected NotificationService CreateNotificationService()
        => new(HubMock.Object, NullLogger<NotificationService>.Instance);

    public void Dispose() => Db.Dispose();
}