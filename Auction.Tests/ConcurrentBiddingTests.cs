using Auction.Data;
using Auction.Models;
using Auction.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auction.Tests;


public class ConcurrentBiddingTests : TestBase
{

    [Fact]
    public async Task TwoConcurrentBids_BothAccepted_CurrentBidMatchesBidTable()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("B1", "b1@uni.edu");
        var bidder2 = CreateUser("B2", "b2@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "B1");
        await BidService.PlaceBidAsync(auction.Id, bidder2.Id, 15m, "B2");

        var a = await Db.Auctions.FindAsync(auction.Id);
        var bidsInDb = await Db.Bids.Where(b => b.AuctionId == auction.Id).ToListAsync();

        a!.BidCount.Should().Be(bidsInDb.Count);
        a.CurrentHighestBid.Should().Be(bidsInDb.Max(b => b.Amount));
    }

    [Fact]
    public async Task ParallelBids_FromDifferentBidders_NoStateLoss()
    {
        var seller   = CreateUser("Seller", "seller@uni.edu");
        var bidders  = Enumerable.Range(0, 5)
            .Select(i => CreateUser($"Bidder{i}", $"b{i}@uni.edu"))
            .ToList();
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 1m);

        var amounts = new[] { 10m, 11m, 12m, 13m, 14m };
        var tasks = bidders.Zip(amounts, (b, a) => (bidder: b, amount: a))
            .Select(async x =>
            {
                try { await BidService.PlaceBidAsync(auction.Id, x.bidder.Id, x.amount, x.bidder.DisplayName); return true; }
                catch { return false; }
            });

        var results = await Task.WhenAll(tasks);

        var finalAuction = await Db.Auctions.FindAsync(auction.Id);
        var bidsInDb = await Db.Bids.Where(b => b.AuctionId == auction.Id).ToListAsync();

        finalAuction!.BidCount.Should().Be(bidsInDb.Count);

        if (bidsInDb.Any())
            finalAuction.CurrentHighestBid.Should().Be(bidsInDb.Max(b => b.Amount));
    }

    [Fact]
    public async Task TwoIdenticalAmounts_SecondIsRejected_BecauseNotMeetingIncrement()
    {
        var seller  = CreateUser("Seller", "seller@uni.edu");
        var bidder1 = CreateUser("B1", "b1@uni.edu");
        var bidder2 = CreateUser("B2", "b2@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 5m);

        await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 10m, "B1");

        var act = () => BidService.PlaceBidAsync(auction.Id, bidder2.Id, 10m, "B2");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bid must be at least*");
    }

    [Fact]
    public async Task BuyNow_UnderConcurrentLoad_OnlyOneWinner()
    {
        var seller   = CreateUser("Seller", "seller@uni.edu");
        var bidder1  = CreateUser("B1", "b1@uni.edu");
        var bidder2  = CreateUser("B2", "b2@uni.edu");
        var auction  = CreateAuction(seller.Id, startingBid: 10m, buyNowPrice: 100m);

        var (bid, _) = await BidService.PlaceBidAsync(auction.Id, bidder1.Id, 100m, "B1");
        bid.IsBuyNow.Should().BeTrue();

        var act = () => BidService.PlaceBidAsync(auction.Id, bidder2.Id, 100m, "B2");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not active*");

        var final = await Db.Auctions.FindAsync(auction.Id);
        final!.WinnerId.Should().Be(bidder1.Id);
    }

    [Fact]
    public async Task ManySequentialBids_BidCountAlwaysMatchesBidTable()
    {
        var seller = CreateUser("Seller", "seller@uni.edu");
        var bidder = CreateUser("Bidder", "bidder@uni.edu");
        var auction = CreateAuction(seller.Id, startingBid: 10m, minimumIncrement: 1m);

        for (int i = 0; i < 20; i++)
            await BidService.PlaceBidAsync(auction.Id, bidder.Id, 10m + i, $"Bidder");

        var finalAuction = await Db.Auctions.FindAsync(auction.Id);
        var bidsInDb = await Db.Bids.Where(b => b.AuctionId == auction.Id).CountAsync();

        finalAuction!.BidCount.Should().Be(bidsInDb);
        finalAuction.CurrentHighestBid.Should().Be(29m); 
    }
}
