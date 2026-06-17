using Auction.DTOs;
using Auction.Models;
using FluentAssertions;

namespace Auction.Tests;

/// <summary>
/// Покрывает создание, редактирование, отмену и подтверждение продажи аукционов.
/// </summary>
public class AuctionCrudTests : TestBase
{
    // ─── Создание ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAuction_ValidRequest_ReturnsDto()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var request = new CreateAuctionRequest
        {
            Title            = "MacBook Pro",
            Description      = "Good condition",
            Category         = "Tech",
            Condition        = "Good",
            StartingBid      = 100m,
            MinimumIncrement = 10m,
            EndTime          = DateTime.UtcNow.AddHours(2),
            PickupLocation   = "Library"
        };

        var dto = await AuctionService.CreateAuctionAsync(request, seller.Id, new List<string>());

        dto.Id.Should().BeGreaterThan(0);
        dto.Title.Should().Be("MacBook Pro");
        dto.Status.Should().Be("Active");
        dto.SellerName.Should().Be("Seller");
    }

    [Fact]
    public async Task CreateAuction_EndTimeInPast_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var request = new CreateAuctionRequest
        {
            Title = "Test", Description = "x", Category = "Tech", Condition = "Good",
            StartingBid = 10m, MinimumIncrement = 5m,
            EndTime = DateTime.UtcNow.AddMinutes(1), // меньше 2 минут
            PickupLocation = "Library"
        };

        var act = () => AuctionService.CreateAuctionAsync(request, seller.Id, new List<string>());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at least 2 minutes*");
    }

    [Fact]
    public async Task CreateAuction_EndTimeTooFar_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var request = new CreateAuctionRequest
        {
            Title = "Test", Description = "x", Category = "Tech", Condition = "Good",
            StartingBid = 10m, MinimumIncrement = 5m,
            EndTime = DateTime.UtcNow.AddDays(8),
            PickupLocation = "Library"
        };

        var act = () => AuctionService.CreateAuctionAsync(request, seller.Id, new List<string>());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*7 days*");
    }

    [Fact]
    public async Task CreateAuction_BuyNowBelowStartingBid_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var request = new CreateAuctionRequest
        {
            Title = "Test", Description = "x", Category = "Tech", Condition = "Good",
            StartingBid = 100m, MinimumIncrement = 5m, BuyNowPrice = 50m,
            EndTime = DateTime.UtcNow.AddHours(1), PickupLocation = "Library"
        };

        var act = () => AuctionService.CreateAuctionAsync(request, seller.Id, new List<string>());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Buy Now price*");
    }

    // ─── Редактирование ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAuction_BySellerBeforeBids_Succeeds()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id);
        var request = new CreateAuctionRequest
        {
            Title = "Updated Title", Description = "New desc", Category = "Books",
            Condition = "Like New", StartingBid = 20m, MinimumIncrement = 5m,
            EndTime = DateTime.UtcNow.AddHours(3), PickupLocation = "Dorm B"
        };

        await AuctionService.UpdateAuctionAsync(auction.Id, seller.Id, request);

        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Title.Should().Be("Updated Title");
        updated.Category.Should().Be("Books");
    }

    [Fact]
    public async Task UpdateAuction_ByNonSeller_Throws()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var other   = CreateUser("Other", "other@uni.edu");
        var auction = CreateAuction(seller.Id);
        var request = new CreateAuctionRequest
        {
            Title = "Hack", Description = "x", Category = "Tech", Condition = "Good",
            StartingBid = 10m, MinimumIncrement = 5m,
            EndTime = DateTime.UtcNow.AddHours(1), PickupLocation = "X"
        };

        var act = () => AuctionService.UpdateAuctionAsync(auction.Id, other.Id, request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*seller*");
    }

    [Fact]
    public async Task UpdateAuction_AfterFirstBid_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id);

        await BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m, "Bidder");

        var request = new CreateAuctionRequest
        {
            Title = "Try Update", Description = "x", Category = "Tech", Condition = "Good",
            StartingBid = 10m, MinimumIncrement = 5m,
            EndTime = DateTime.UtcNow.AddHours(2), PickupLocation = "X"
        };

        var act = () => AuctionService.UpdateAuctionAsync(auction.Id, seller.Id, request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*bids*");
    }

    // ─── Отмена ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAuction_BySellerWithNoBids_Succeeds()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id);

        await AuctionService.CancelAuctionAsync(auction.Id, seller.Id);

        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Status.Should().Be(AuctionStatus.Canceled);
    }

    [Fact]
    public async Task CancelAuction_AfterFirstBid_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id);

        await BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m, "Bidder");

        var act = () => AuctionService.CancelAuctionAsync(auction.Id, seller.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*bids*");
    }

    [Fact]
    public async Task CancelAuction_ByNonSeller_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var other  = CreateUser("Other", "other@uni.edu");
        var auction = CreateAuction(seller.Id);

        var act = () => AuctionService.CancelAuctionAsync(auction.Id, other.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*seller*");
    }

    [Fact]
    public async Task CancelAuction_AlreadyEnded_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, status: AuctionStatus.Ended);

        var act = () => AuctionService.CancelAuctionAsync(auction.Id, seller.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active*");
    }

    // ─── Подтверждение продажи ────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmSale_BySeller_OnEndedAuction_Succeeds()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, status: AuctionStatus.Ended);

        await AuctionService.ConfirmSaleAsync(auction.Id, seller.Id);

        var updated = await Db.Auctions.FindAsync(auction.Id);
        updated!.Status.Should().Be(AuctionStatus.Sold);
    }

    [Fact]
    public async Task ConfirmSale_OnActiveAuction_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var auction = CreateAuction(seller.Id, status: AuctionStatus.Active);

        var act = () => AuctionService.ConfirmSaleAsync(auction.Id, seller.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ended*");
    }

    [Fact]
    public async Task ConfirmSale_ByNonSeller_Throws()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var other  = CreateUser("Other", "other@uni.edu");
        var auction = CreateAuction(seller.Id, status: AuctionStatus.Ended);

        var act = () => AuctionService.ConfirmSaleAsync(auction.Id, other.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*seller*");
    }

    // ─── Queries ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuctions_ReturnsOnlyActiveByDefault()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        CreateAuction(seller.Id, status: AuctionStatus.Active);
        CreateAuction(seller.Id, status: AuctionStatus.Ended);
        CreateAuction(seller.Id, status: AuctionStatus.Canceled);

        var result = await AuctionService.GetAuctionsAsync(status: "Active");

        result.Items.Should().HaveCount(1);
        result.Items.All(a => a.Status == "Active").Should().BeTrue();
    }

    [Fact]
    public async Task GetAuctions_SearchByTitle_FiltersCorrectly()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var a1 = CreateAuction(seller.Id); a1.Title = "MacBook Pro"; Db.SaveChanges();
        var a2 = CreateAuction(seller.Id); a2.Title = "iPhone 14";   Db.SaveChanges();
        var a3 = CreateAuction(seller.Id); a3.Title = "Calculus Book"; Db.SaveChanges();

        var result = await AuctionService.GetAuctionsAsync(search: "mac");

        result.Items.Should().HaveCount(1);
        result.Items[0].Title.Should().Contain("MacBook");
    }

    [Fact]
    public async Task GetAuctions_Paging_ReturnsCorrectPage()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        for (int i = 0; i < 15; i++) CreateAuction(seller.Id);

        var page1 = await AuctionService.GetAuctionsAsync(page: 1, pageSize: 10);
        var page2 = await AuctionService.GetAuctionsAsync(page: 2, pageSize: 10);

        page1.Items.Should().HaveCount(10);
        page2.Items.Should().HaveCount(5);
        page1.Total.Should().Be(15);
    }

    [Fact]
    public async Task GetAuctionDetails_WithBids_ReturnsBidHistory()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m, "Bidder");
        await BidService.PlaceBidAsync(auction.Id, bidder.Id, 15m, "Bidder");

        var detail = await AuctionService.GetAuctionDetailsAsync(auction.Id);

        detail.Should().NotBeNull();
        detail!.BidCount.Should().Be(2);
        detail.RecentBids.Should().HaveCount(2);
        detail.CurrentBid.Should().Be(15m);
    }

    [Fact]
    public async Task GetAuctionDetails_NonExistent_ReturnsNull()
    {
        var result = await AuctionService.GetAuctionDetailsAsync(9999);
        result.Should().BeNull();
    }
}
