namespace Auction.DTOs;


// Представление аукциона для списка
public class AuctionSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public decimal CurrentBid { get; set; }
    public decimal StartingBid { get; set; }
    public decimal? BuyNowPrice { get; set; }
    public int BidCount { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public string? WinnerName { get; set; }
}

// Представление аукциона с историей ставок
public class AuctionDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public List<string> PhotoUrls { get; set; } = new();
    public decimal CurrentBid { get; set; }
    public decimal StartingBid { get; set; }
    public decimal MinimumIncrement { get; set; }
    public decimal? BuyNowPrice { get; set; }
    public int BidCount { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public int SellerId { get; set; }
    public int? WinnerId { get; set; }
    public string? WinnerName { get; set; }
    public List<BidDto> RecentBids { get; set; } = new();
}



// Тело запроса на создание или обновление аукциона.
public class CreateAuctionRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public decimal StartingBid { get; set; }
    public decimal MinimumIncrement { get; set; }
    public decimal? BuyNowPrice { get; set; }
    public DateTime EndTime { get; set; }
    public string PickupLocation { get; set; } = string.Empty;
}

public class PlaceBidRequest
{
    public decimal Amount { get; set; }
}
