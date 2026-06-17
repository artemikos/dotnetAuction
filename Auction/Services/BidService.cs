using Microsoft.EntityFrameworkCore;
using Auction.Data;
using Auction.DTOs;
using Auction.Models;

namespace Auction.Services;


public class BidService
{
    private readonly AuctionDbContext _context;
    private readonly ILogger<BidService> _logger;

    public BidService(AuctionDbContext context, ILogger<BidService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<(BidDto bid, int? previousBidderId)> PlaceBidAsync(
        int auctionId, int bidderId, decimal amount, string bidderDisplayName)
    {
        using var transaction = await _context.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        try
        {
            var auction = await _context.Auctions
                .FirstOrDefaultAsync(a => a.Id == auctionId)
                ?? throw new InvalidOperationException("Auction not found");

            if (auction.Status != AuctionStatus.Active)
                throw new InvalidOperationException("Auction is not active");

            if (DateTime.UtcNow >= auction.EndTime)
                throw new InvalidOperationException("Auction has already ended");

            if (auction.SellerId == bidderId)
                throw new InvalidOperationException("Seller cannot bid on their own auction");

            if (auction.BidCount == 0 && amount < auction.StartingBid)
                throw new InvalidOperationException(
                    $"First bid must be at least {auction.StartingBid:C}");

            var minRequired = (auction.CurrentHighestBid ?? auction.StartingBid) + auction.MinimumIncrement;
            if (auction.BidCount > 0 && amount < minRequired)
                throw new InvalidOperationException(
                    $"Bid must be at least {minRequired:C}");

            int? previousBidderId = null;
            if (auction.BidCount > 0)
            {
                previousBidderId = await _context.Bids
                    .Where(b => b.AuctionId == auctionId)
                    .OrderByDescending(b => b.Amount)
                    .Select(b => (int?)b.BidderId)
                    .FirstOrDefaultAsync();
            }

            var bid = new Bid
            {
                AuctionId = auctionId,
                BidderId  = bidderId,
                Amount    = amount,
                PlacedAt  = DateTime.UtcNow
            };
            _context.Bids.Add(bid);

            auction.CurrentHighestBid = amount;
            auction.BidCount++;

            bool isBuyNow = auction.BuyNowPrice.HasValue && amount >= auction.BuyNowPrice.Value;
            if (isBuyNow)
            {
                auction.Status   = AuctionStatus.Ended;
                auction.WinnerId = bidderId;
                _logger.LogInformation(
                    "Auction {AuctionId} closed immediately via BuyNow by user {BidderId}",
                    auctionId, bidderId);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Bid accepted: AuctionId={AuctionId} BidderId={BidderId} Amount={Amount} IsBuyNow={IsBuyNow}",
                auctionId, bidderId, amount, isBuyNow);

            var notifyPrevious = previousBidderId.HasValue && previousBidderId.Value != bidderId
                ? previousBidderId
                : null;

            return (new BidDto
            {
                Id               = bid.Id,
                AuctionId        = auctionId,
                BidderName       = bidderDisplayName,
                Amount           = amount,
                PlacedAt         = bid.PlacedAt,
                CurrentHighestBid = amount,
                BidCount         = auction.BidCount,
                IsBuyNow         = isBuyNow,
                WinnerId         = isBuyNow ? bidderId : null
            }, notifyPrevious);
        }
        
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
