using Microsoft.EntityFrameworkCore;
using Auction.Models;

namespace Auction.Data;

public class AuctionDbContext : DbContext
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuctionItem> Auctions => Set<AuctionItem>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<AuctionPhoto> Photos => Set<AuctionPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity => entity.HasIndex(u => u.Email).IsUnique());

        modelBuilder.Entity<AuctionItem>(entity =>
        {
            entity.HasOne(a => a.Seller).WithMany(u => u.Auctions).HasForeignKey(a => a.SellerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.Winner).WithMany().HasForeignKey(a => a.WinnerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(a => a.Status); entity.HasIndex(a => a.EndTime); entity.HasIndex(a => a.Category);
        });

        modelBuilder.Entity<Bid>(entity =>
        {
            entity.HasOne(b => b.Auction).WithMany(a => a.Bids).HasForeignKey(b => b.AuctionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(b => b.Bidder).WithMany(u => u.Bids).HasForeignKey(b => b.BidderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(b => b.AuctionId);
        });

        modelBuilder.Entity<AuctionPhoto>(entity =>
        {
            entity.HasOne(p => p.Auction).WithMany(a => a.Photos).HasForeignKey(p => p.AuctionId).OnDelete(DeleteBehavior.Cascade);
        });

        // Seed
        var pw = BCrypt.Net.BCrypt.HashPassword("password123");
        modelBuilder.Entity<User>().HasData(
            new User { Id = 1, Email = "alice@university.edu", DisplayName = "Alice", PasswordHash = pw, CreatedAt = DateTime.UtcNow.AddDays(-30) },
            new User { Id = 2, Email = "bob@university.edu", DisplayName = "Bob", PasswordHash = pw, CreatedAt = DateTime.UtcNow.AddDays(-20) },
            new User { Id = 3, Email = "charlie@university.edu", DisplayName = "Charlie", PasswordHash = pw, CreatedAt = DateTime.UtcNow.AddDays(-10) }
        );

        var rng = new Random(42);
        var titles = new[] { "MacBook Pro 13\" M1", "CS Algorithms Bundle", "IKEA Desk Chair", "Vintage Denim Jacket", "Samsung 27\" Monitor", "Calculus Textbook", "Standing Desk", "Nike Shoes Size 10", "iPhone 12 Pro 128GB", "Organic Chemistry Book" };
        var cats = new[] { "Tech", "Books", "Furniture", "Clothing", "Tech", "Books", "Furniture", "Clothing", "Tech", "Books" };
        var conds = new[] { "Good", "Like New", "Fair", "New", "Good", "Like New", "Fair", "New", "Good", "Like New" };
        var sellers = new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3, 1 };

        for (int i = 0; i < 10; i++)
        {
            modelBuilder.Entity<AuctionItem>().HasData(new AuctionItem
            {
                Id = i + 1,
                Title = titles[i],
                Description = "Great condition. Barely used.",
                Category = cats[i],
                Condition = conds[i],
                StartingBid = (i + 1) * 10,
                MinimumIncrement = 5,
                BuyNowPrice = i % 3 == 0 ? (i + 1) * 50 : null,
                EndTime = DateTime.UtcNow.AddHours(i * 10 + 5),
                PickupLocation = "Main Library Entrance",
                Status = AuctionStatus.Active,
                SellerId = sellers[i],
                CurrentHighestBid = null,
                BidCount = 0,
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            });
            modelBuilder.Entity<AuctionPhoto>().HasData(new AuctionPhoto
            {
                Id = i + 1,
                AuctionId = i + 1,
                Url = $"https://picsum.photos/seed/item{rng.Next(1, 999)}/400/300",
                IsCover = true,
                Order = 0
            });
        }
    }
}