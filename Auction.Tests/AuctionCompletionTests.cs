using Auction.Data;
using Auction.Models;
using Auction.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auction.Tests;

/// <summary>
/// Тестирует логику завершения аукционов: выбор победителя,
/// обработку аукционов без ставок, BuyNow-закрытие.
/// </summary>
public class AuctionCompletionTests : TestBase
{
    // Вспомогательный метод: имитирует одну итерацию воркера
    private async Task RunCompletionCycleAsync()
    {
        var ns  = CreateNotificationService();
        var hub = HubMock.Object;

        var expired = await Db.Auctions
            .Where(a => a.Status == AuctionStatus.Active && a.EndTime <= DateTime.UtcNow)
            .ToListAsync();

        foreach (var auction in expired)
        {
            var best = await Db.Bids
                .Include(b => b.Bidder)
                .Where(b => b.AuctionId == auction.Id)
                .OrderByDescending(b => b.Amount)
                .FirstOrDefaultAsync();

            auction.Status = AuctionStatus.Ended;

            if (best != null)
            {
                auction.WinnerId = best.BidderId;
                await Db.SaveChangesAsync();
                await ns.NotifyWinnerAsync(best.BidderId, auction.Id, auction.Title);
                await hub.Clients.Group(auction.Id.ToString())
                    .SendAsync("AuctionEnded", new { id = auction.Id, status = "Ended" });
            }
            else
            {
                await Db.SaveChangesAsync();
                await ns.NotifyNoBidsAsync(auction.SellerId, auction.Id, auction.Title);
                await hub.Clients.Group(auction.Id.ToString())
                    .SendAsync("AuctionEnded", new { id = auction.Id, status = "Ended" });
            }
        }
    }

    // ─── Выбор победителя ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExpiredAuction_HighestBidder_BecomesWinner()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("B1", "b1@uni.edu");
        var bidder2 = CreateUser("B2", "b2@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m,
            endTime: DateTime.UtcNow.AddMilliseconds(50));

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "B1");
        await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 15m, "B2");

        await Task.Delay(100); // ждём истечения
        await RunCompletionCycleAsync();

        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Status.Should().Be(AuctionStatus.Ended);
        updated.WinnerId.Should().Be(bidder2.Id); // B2 сделал наибольшую ставку
    }

    [Fact]
    public async Task ExpiredAuction_WithNoBids_HasNoWinner()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddMilliseconds(50));

        await Task.Delay(100);
        await RunCompletionCycleAsync();

        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Status.Should().Be(AuctionStatus.Ended);
        updated.WinnerId.Should().BeNull();
    }

    [Fact]
    public async Task ExpiredAuction_WithNoBids_NotifiesSeller()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddMilliseconds(50));

        await Task.Delay(100);
        await RunCompletionCycleAsync();

        // Продавцу отправлено уведомление NoBidsEnded
        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("NoBidsEnded", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task ExpiredAuction_WithWinner_NotifiesWinner()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddMilliseconds(50));

        await BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m, "Bidder");

        await Task.Delay(100);
        await RunCompletionCycleAsync();

        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("AuctionWon", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task ActiveAuction_NotYetExpired_IsNotCompleted()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddHours(2));

        await RunCompletionCycleAsync();

        var auction = Db.Auctions.First();
        auction.Status.Should().Be(AuctionStatus.Active);
    }

    [Fact]
    public async Task MultipleExpiredAuctions_AllGetCompleted()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder  = CreateUser("Bidder", "bidder@uni.edu");

        var a1 = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddMilliseconds(50));
        var a2 = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddMilliseconds(50));
        var a3 = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddHours(2)); // не истёк

        await BidService.PlaceBidAsync(a1.Id, bidder.Id, 10m, "Bidder");

        await Task.Delay(100);
        await RunCompletionCycleAsync();

        (await Db.Auctions.FindAsync(a1.Id))!.Status.Should().Be(AuctionStatus.Ended);
        (await Db.Auctions.FindAsync(a2.Id))!.Status.Should().Be(AuctionStatus.Ended);
        (await Db.Auctions.FindAsync(a3.Id))!.Status.Should().Be(AuctionStatus.Active);
    }

    // ─── BuyNow ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyNow_ClosesAuction_ImmediatelyWithoutWaitingForEndTime()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        // endTime через час — воркер не должен его трогать
        var auction = CreateAuction(seller.Id, startingBid: 10m, buyNowPrice: 50m,
            endTime: DateTime.UtcNow.AddHours(1));

        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 50m, "Bidder");

        bid.IsBuyNow.Should().BeTrue();
        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Status.Should().Be(AuctionStatus.Ended);
        updated.WinnerId.Should().Be(bidder.Id);

        // Воркер не должен переобработать уже завершённый аукцион
        await RunCompletionCycleAsync();
        var afterWorker = await Db.Auctions.FindAsync(auction.Id);
        afterWorker!.Status.Should().Be(AuctionStatus.Ended); // не изменился
    }
}
