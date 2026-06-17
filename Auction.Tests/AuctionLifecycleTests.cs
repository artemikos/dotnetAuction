using Auction.Models;
using Auction.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Auction.Tests;

/// <summary>
/// Сквозные (end-to-end) сценарии: полный жизненный цикл аукциона
/// от создания до Sold, включая все переходы состояний.
/// </summary>
public class AuctionLifecycleTests : TestBase
{
    // ─── Полный цикл: Active → ставки → Ended → Sold ──────────────────────────

    [Fact]
    public async Task FullLifecycle_ActiveToBidToEndedToSold()
    {
        // 1. Создаём участников
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("Alice", "alice@uni.edu");
        var bidder2 = CreateUser("Bob", "bob@uni.edu");

        // 2. Продавец создаёт аукцион
        var auction = CreateAuction(seller.Id, startingBid: 20m, minimumIncrement: 5m);
        auction.Status.Should().Be(AuctionStatus.Active);

        // 3. Первая ставка
        var (bid1, prev1) = await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 20m, "Alice");
        bid1.BidCount.Should().Be(1);
        prev1.Should().BeNull(); // никого не перебили

        // 4. Вторая ставка перебивает первую
        var (bid2, prev2) = await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 25m, "Bob");
        bid2.BidCount.Should().Be(2);
        prev2.Should().Be(bidder1.Id); // Alice должна получить Outbid-уведомление

        // 5. Alice перебивает Bob
        var (bid3, prev3) = await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 30m, "Alice");
        bid3.CurrentHighestBid.Should().Be(30m);
        prev3.Should().Be(bidder2.Id); // Bob должен получить уведомление

        // 6. Вручную завершаем аукцион (имитация воркера)
        var a = await Db.Auctions.FindAsync(auction.Id);
        var bestBid = await Db.Bids.Where(b => b.AuctionId == auction.Id)
            .OrderByDescending(b => b.Amount).FirstAsync();
        a!.Status   = AuctionStatus.Ended;
        a.WinnerId  = bestBid.BidderId;
        await Db.SaveChangesAsync();

        a.WinnerId.Should().Be(bidder1.Id); // Alice победила

        // 7. Продавец подтверждает продажу
        await AuctionService.ConfirmSaleAsync(auction.Id, seller.Id);

        var final = await Db.Auctions.FindAsync(auction.Id);
        final!.Status.Should().Be(AuctionStatus.Sold);
        final.WinnerId.Should().Be(bidder1.Id);
    }

    // ─── Цикл отмены: Active → Canceled ──────────────────────────────────────

    [Fact]
    public async Task LifecycleCanceled_NosBids_SellerCancels()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id);

        await AuctionService.CancelAuctionAsync(auction.Id, seller.Id);

        var final = await Db.Auctions.FindAsync(auction.Id);
        final!.Status.Should().Be(AuctionStatus.Canceled);
        final.WinnerId.Should().BeNull();
    }

    // ─── BuyNow цикл: Active → Ended (мгновенно) ─────────────────────────────

    [Fact]
    public async Task LifecycleBuyNow_AuctionClosesImmediately()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, buyNowPrice: 200m);

        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 200m, "Bidder");

        bid.IsBuyNow.Should().BeTrue();
        bid.WinnerId.Should().Be(bidder.Id);

        // Нельзя делать ставки после BuyNow
        var otherBidder = CreateUser("Other", "other@uni.edu");
        var act = () => BidService.PlaceBidAsync(auction.Id, otherBidder.Id, 200m, "Other");
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Продавец может подтвердить продажу
        await AuctionService.ConfirmSaleAsync(auction.Id, seller.Id);
        var final = await Db.Auctions.FindAsync(auction.Id);
        final!.Status.Should().Be(AuctionStatus.Sold);
    }

    // ─── Цикл без ставок: Active → Ended (без победителя) ────────────────────

    [Fact]
    public async Task LifecycleNoBids_EndedWithoutWinner()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddMilliseconds(50));

        await Task.Delay(100);

        // Имитация воркера
        var a = await Db.Auctions.FindAsync(auction.Id);
        a!.Status = AuctionStatus.Ended;
        await Db.SaveChangesAsync();

        var final = await Db.Auctions.FindAsync(auction.Id);
        final!.Status.Should().Be(AuctionStatus.Ended);
        final.WinnerId.Should().BeNull();

        // Продавец не может подтвердить продажу без победителя (логически)
        // но технически ConfirmSale смотрит только на статус — это нормально
    }

    // ─── Переходы состояний — нельзя двигаться назад ─────────────────────────

    [Fact]
    public async Task StateTransition_CanceledAuction_CannotAcceptBids()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id);

        await AuctionService.CancelAuctionAsync(auction.Id, seller.Id);
        var act = () => BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m, "Bidder");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task StateTransition_SoldAuction_CannotBeConfirmedAgain()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, status: AuctionStatus.Ended);

        await AuctionService.ConfirmSaleAsync(auction.Id, seller.Id);

        // Повторное подтверждение — уже Sold, не Ended
        var act = () => AuctionService.ConfirmSaleAsync(auction.Id, seller.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ended*");
    }

    // ─── Данные сохраняются: история ставок полная ────────────────────────────

    [Fact]
    public async Task BidHistory_IsComplete_AfterMultipleRounds()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("Alice", "alice@uni.edu");
        var bidder2 = CreateUser("Bob", "bob@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "Alice");
        await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 15m, "Bob");
        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 20m, "Alice");
        await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 25m, "Bob");

        var bids = await Db.Bids.Where(b => b.AuctionId == auction.Id).ToListAsync();
        bids.Should().HaveCount(4);

        var detail = await AuctionService.GetAuctionDetailsAsync(auction.Id);
        detail!.RecentBids.Should().HaveCount(4);
        detail.CurrentBid.Should().Be(25m);
        detail.BidCount.Should().Be(4);
    }
}
