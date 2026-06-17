using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Auction.Models;

public enum AuctionStatus { Active, Ended, Sold, Canceled }

public class User
{
    public int Id { get; set; }
    [Required, MaxLength(255)] public string Email { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [Required] public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<AuctionItem> Auctions { get; set; } = new List<AuctionItem>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}

public class AuctionItem
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string Description { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Category { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Condition { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal StartingBid { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MinimumIncrement { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? BuyNowPrice { get; set; }
    public DateTime EndTime { get; set; }
    [Required, MaxLength(500)] public string PickupLocation { get; set; } = string.Empty;
    public AuctionStatus Status { get; set; } = AuctionStatus.Active;
    public int SellerId { get; set; }
    public User Seller { get; set; } = null!;
    public int? WinnerId { get; set; }
    public User? Winner { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? CurrentHighestBid { get; set; }
    public int BidCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public ICollection<AuctionPhoto> Photos { get; set; } = new List<AuctionPhoto>();
}

public class Bid
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public AuctionItem Auction { get; set; } = null!;
    public int BidderId { get; set; }
    public User Bidder { get; set; } = null!;
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
}

public class AuctionPhoto
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public AuctionItem Auction { get; set; } = null!;
    [Required, MaxLength(500)] public string Url { get; set; } = string.Empty;
    public bool IsCover { get; set; }
    public int Order { get; set; }
}