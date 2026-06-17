using Microsoft.EntityFrameworkCore;
using Auction.Data;
using Auction.DTOs;
using Auction.Models;

namespace Auction.Services;

public class AuctionService
{
    private readonly AuctionDbContext _context;
    private readonly ILogger<AuctionService> _logger;

    public AuctionService(AuctionDbContext context, ILogger<AuctionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResponse<AuctionSummaryDto>> GetAuctionsAsync(
        int page = 1, int pageSize = 10,
        string? category = null, string? sort = null,
        string? status = "Active", string? search = null)
    {
        var query = _context.Auctions
            .Include(a => a.Seller)
            .Include(a => a.Photos)
            .Include(a => a.Winner)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AuctionStatus>(status, out var s))
            query = query.Where(a => a.Status == s);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.Category == category);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a => a.Title.ToLower().Contains(search.ToLower()));

        query = sort switch
        {
            "newest"     => query.OrderByDescending(a => a.CreatedAt),
            "price_asc"  => query.OrderBy(a => a.CurrentHighestBid ?? a.StartingBid),
            "price_desc" => query.OrderByDescending(a => a.CurrentHighestBid ?? a.StartingBid),
            "most_bids"  => query.OrderByDescending(a => a.BidCount),
            _            => query.OrderBy(a => a.EndTime)
        };

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResponse<AuctionSummaryDto>
        {
            Total = total, Page = page, PageSize = pageSize,
            Items = items.Select(MapToSummary).ToList()
        };
    }

    public async Task<AuctionDetailDto?> GetAuctionDetailsAsync(int id)
    {
        var a = await _context.Auctions
            .Include(x => x.Seller)
            .Include(x => x.Winner)
            .Include(x => x.Photos)
            .Include(x => x.Bids).ThenInclude(b => b.Bidder)
            .FirstOrDefaultAsync(x => x.Id == id);

        return a == null ? null : MapToDetail(a);
    }

    public async Task<AuctionSummaryDto> CreateAuctionAsync(
        CreateAuctionRequest request, int sellerId, List<string> photoUrls)
    {
        if (request.EndTime <= DateTime.UtcNow.AddMinutes(2))
            throw new InvalidOperationException("Auction must last at least 2 minutes");
        if (request.EndTime > DateTime.UtcNow.AddDays(7))
            throw new InvalidOperationException("Auction cannot last more than 7 days");
        if (request.BuyNowPrice.HasValue && request.BuyNowPrice <= request.StartingBid)
            throw new InvalidOperationException("Buy Now price must be higher than starting bid");

        if (photoUrls.Count == 0)
            photoUrls.Add($"https://picsum.photos/seed/auction{Random.Shared.Next(1, 999)}/400/300");

        var auction = new AuctionItem
        {
            Title           = request.Title,
            Description     = request.Description,
            Category        = request.Category,
            Condition       = request.Condition,
            StartingBid     = request.StartingBid,
            MinimumIncrement = request.MinimumIncrement,
            BuyNowPrice     = request.BuyNowPrice,
            EndTime         = request.EndTime,
            PickupLocation  = request.PickupLocation,
            SellerId        = sellerId,
            Status          = AuctionStatus.Active,
            CreatedAt       = DateTime.UtcNow,
            Photos = photoUrls
                .Select((url, i) => new AuctionPhoto { Url = url, IsCover = i == 0, Order = i })
                .ToList()
        };

        _context.Auctions.Add(auction);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Auction {Id} created by user {SellerId}", auction.Id, sellerId);

        var created = await _context.Auctions
            .Include(a => a.Seller).Include(a => a.Photos)
            .FirstAsync(a => a.Id == auction.Id);

        return MapToSummary(created);
    }

    public async Task UpdateAuctionAsync(int auctionId, int sellerId, CreateAuctionRequest request)
    {
        var a = await _context.Auctions.FindAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        if (a.SellerId != sellerId)   throw new InvalidOperationException("Only the seller can edit this auction");
        if (a.BidCount > 0)           throw new InvalidOperationException("Cannot edit an auction that already has bids");
        if (a.Status != AuctionStatus.Active) throw new InvalidOperationException("Only active auctions can be edited");

        a.Title           = request.Title;
        a.Description     = request.Description;
        a.Category        = request.Category;
        a.Condition       = request.Condition;
        a.StartingBid     = request.StartingBid;
        a.MinimumIncrement = request.MinimumIncrement;
        a.BuyNowPrice     = request.BuyNowPrice;
        a.EndTime         = request.EndTime;
        a.PickupLocation  = request.PickupLocation;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Auction {Id} updated by seller {SellerId}", auctionId, sellerId);
    }

    public async Task ConfirmSaleAsync(int auctionId, int sellerId)
    {
        var a = await _context.Auctions.FindAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        if (a.Status != AuctionStatus.Ended)
            throw new InvalidOperationException("Only ended auctions can be confirmed as sold");
        if (a.SellerId != sellerId)
            throw new InvalidOperationException("Only the seller can confirm the sale");

        a.Status = AuctionStatus.Sold;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Auction {Id} confirmed as sold by seller {SellerId}", auctionId, sellerId);
    }

    public async Task CancelAuctionAsync(int auctionId, int sellerId)
    {
        var a = await _context.Auctions.FindAsync(auctionId)
            ?? throw new InvalidOperationException("Auction not found");

        if (a.SellerId != sellerId)
            throw new InvalidOperationException("Only the seller can cancel this auction");
        if (a.Status != AuctionStatus.Active)
            throw new InvalidOperationException("Only active auctions can be cancelled");
        if (a.BidCount > 0)
            throw new InvalidOperationException("Cannot cancel an auction that already has bids");

        a.Status = AuctionStatus.Canceled;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Auction {Id} cancelled by seller {SellerId}", auctionId, sellerId);
    }

    private static AuctionSummaryDto MapToSummary(AuctionItem a) => new()
    {
        Id           = a.Id,
        Title        = a.Title,
        Category     = a.Category,
        Condition    = a.Condition,
        CoverImageUrl = a.Photos.FirstOrDefault(p => p.IsCover)?.Url,
        CurrentBid   = a.CurrentHighestBid ?? a.StartingBid,
        StartingBid  = a.StartingBid,
        BuyNowPrice  = a.BuyNowPrice,
        BidCount     = a.BidCount,
        EndTime      = a.EndTime,
        Status       = a.Status.ToString(),
        SellerName   = a.Seller?.DisplayName ?? "Unknown",
        WinnerName   = a.Winner?.DisplayName
    };

    private static AuctionDetailDto MapToDetail(AuctionItem a) => new()
    {
        Id              = a.Id,
        Title           = a.Title,
        Description     = a.Description,
        Category        = a.Category,
        Condition       = a.Condition,
        PhotoUrls       = a.Photos.OrderBy(p => p.Order).Select(p => p.Url).ToList(),
        CurrentBid      = a.CurrentHighestBid ?? a.StartingBid,
        StartingBid     = a.StartingBid,
        MinimumIncrement = a.MinimumIncrement,
        BuyNowPrice     = a.BuyNowPrice,
        BidCount        = a.BidCount,
        EndTime         = a.EndTime,
        Status          = a.Status.ToString(),
        PickupLocation  = a.PickupLocation,
        SellerName      = a.Seller?.DisplayName ?? "Unknown",
        SellerId        = a.SellerId,
        WinnerId        = a.WinnerId,
        WinnerName      = a.Winner?.DisplayName,
        RecentBids      = a.Bids
            .OrderByDescending(b => b.PlacedAt).Take(20)
            .Select(b => new BidDto
            {
                Id               = b.Id,
                AuctionId        = b.AuctionId,
                BidderName       = b.Bidder?.DisplayName ?? "Unknown",
                Amount           = b.Amount,
                PlacedAt         = b.PlacedAt,
                CurrentHighestBid = a.CurrentHighestBid ?? 0,
                BidCount         = a.BidCount
            }).ToList()
    };
}
