using Auction.Models;
using FluentAssertions;

namespace Auction.Tests;

/// <summary>
/// Покрывает все правила валидации ставок из ТЗ.
/// </summary>
public class BidValidationTests : TestBase
{
    // ─── Валидные ставки ──────────────────────────────────────────────────────

    [Fact]
    public async Task FirstBid_EqualToStartingBid_IsAccepted()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 50m);

        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 50m, "Bidder");

        bid.Amount.Should().Be(50m);
        bid.BidCount.Should().Be(1);
        bid.IsBuyNow.Should().BeFalse();
    }

    [Fact]
    public async Task SecondBid_AboveMinimumIncrement_IsAccepted()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("Bidder1", "b1@uni.edu");
        var bidder2 = CreateUser("Bidder2", "b2@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "Bidder1");
        var (bid, prev) = await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 15m, "Bidder2");

        bid.Amount.Should().Be(15m);
        bid.CurrentHighestBid.Should().Be(15m);
        prev.Should().Be(bidder1.Id); // предыдущий лидер возвращается для уведомления
    }

    [Fact]
    public async Task BidCount_IncrementsCorrectly_AfterMultipleBids()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("B1", "b1@uni.edu");
        var bidder2 = CreateUser("B2", "b2@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "B1");
        await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 15m, "B2");
        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 20m, "B1");

        bid.BidCount.Should().Be(3);
    }

    // ─── Ставка ниже минимума ─────────────────────────────────────────────────

    [Fact]
    public async Task FirstBid_BelowStartingBid_IsRejected()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 50m);

        var act = () => BidService.PlaceBidAsync(auction.Id, bidder.Id, 30m, "Bidder");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*First bid must be at least*");
    }

    [Fact]
    public async Task SecondBid_BelowMinimumIncrement_IsRejected()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("B1", "b1@uni.edu");
        var bidder2 = CreateUser("B2", "b2@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "B1");

        // 10 + 5 = 15 минимум, ставим 12 — должно упасть
        var act = () => BidService.PlaceBidAsync(auction.Id, bidder2.Id, 12m, "B2");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bid must be at least*");
    }

    [Fact]
    public async Task SecondBid_ExactlyAtMinimum_IsAccepted()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("B1", "b1@uni.edu");
        var bidder2 = CreateUser("B2", "b2@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "B1");
        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 15m, "B2");

        bid.Amount.Should().Be(15m);
    }

    // ─── Ставка на чужой аукцион / собственный ───────────────────────────────

    [Fact]
    public async Task Seller_CannotBidOnOwnAuction()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m);

        var act = () => BidService.PlaceBidAsync(auction.Id, seller.Id, 10m, "Seller");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Seller cannot bid*");
    }

    // ─── Ставка на завершённый / отменённый аукцион ──────────────────────────

    [Fact]
    public async Task Bid_OnEndedAuction_IsRejected()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, status: AuctionStatus.Ended);

        var act = () => BidService.PlaceBidAsync(auction.Id, bidder.Id, 50m, "Bidder");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task Bid_OnCanceledAuction_IsRejected()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, status: AuctionStatus.Canceled);

        var act = () => BidService.PlaceBidAsync(auction.Id, bidder.Id, 50m, "Bidder");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public async Task Bid_AfterEndTime_IsRejected()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        // endTime в прошлом, но статус ещё Active (воркер ещё не успел обработать)
        var auction = CreateAuction(seller.Id, endTime: DateTime.UtcNow.AddSeconds(-1));

        var act = () => BidService.PlaceBidAsync(auction.Id, bidder.Id, 50m, "Bidder");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already ended*");
    }

    [Fact]
    public async Task Bid_OnNonExistentAuction_IsRejected()
    {
        var bidder = CreateUser("Bidder", "bidder@uni.edu");

        var act = () => BidService.PlaceBidAsync(9999, bidder.Id, 50m, "Bidder");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ─── BuyNow ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyNow_BidAtExactPrice_ClosesAuctionImmediately()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, buyNowPrice: 100m);

        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 100m, "Bidder");

        bid.IsBuyNow.Should().BeTrue();
        bid.WinnerId.Should().Be(bidder.Id);

        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Status.Should().Be(AuctionStatus.Ended);
        updated.WinnerId.Should().Be(bidder.Id);
    }

    [Fact]
    public async Task BuyNow_BidAbovePrice_AlsoClosesAuction()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, buyNowPrice: 100m);

        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 150m, "Bidder");

        bid.IsBuyNow.Should().BeTrue();
    }

    [Fact]
    public async Task BuyNow_BidBelowBuyNowPrice_DoesNotCloseAuction()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, buyNowPrice: 100m);

        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 50m, "Bidder");

        bid.IsBuyNow.Should().BeFalse();
        bid.WinnerId.Should().BeNull();
        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Status.Should().Be(AuctionStatus.Active);
    }

    // ─── PreviousBidder для уведомлений ──────────────────────────────────────

    [Fact]
    public async Task FirstBid_PreviousBidderId_IsNull()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id);

        var (_, prev) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m, "Bidder");

        prev.Should().BeNull();
    }

    [Fact]
    public async Task SameBidder_OutbidsHimself_PreviousBidderIsNull()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m, "Bidder");
        var (_, prev) = await BidService.PlaceBidAsync(auction.Id, bidder.Id, 15m, "Bidder");

        // Тот же биддер — уведомлять не нужно
        prev.Should().BeNull();
    }
}
