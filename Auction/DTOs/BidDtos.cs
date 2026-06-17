namespace Auction.DTOs;

public class BidDto
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public string BidderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime PlacedAt { get; set; }
    public decimal CurrentHighestBid { get; set; }
    public int BidCount { get; set; }
    public bool IsBuyNow { get; set; }
    public int? WinnerId { get; set; }
}

public class PagedResponse<T>
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}
