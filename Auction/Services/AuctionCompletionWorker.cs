using Auction.Data;
using Auction.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Auction.Services;


// Завершает аукционы по endTime и рассылает уведомления о скором закрытии.
public class AuctionCompletionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuctionCompletionWorker> _logger;

    private static readonly ConcurrentDictionary<int, bool> SentWarnings = new();

    public AuctionCompletionWorker(IServiceProvider serviceProvider, ILogger<AuctionCompletionWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AuctionCompletionWorker started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredAuctions(ct);
                await ProcessEndingSoonNotifications(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AuctionCompletionWorker");
            }

            var delay = await CalculateNextCheckDelay(ct);
            _logger.LogDebug("Next check in {Delay}", delay);
            await Task.Delay(delay, ct);
        }

        _logger.LogInformation("AuctionCompletionWorker stopped");
    }

    // Завершение аукционов 

    private async Task ProcessExpiredAuctions(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var ns  = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<AuctionHub>>();

        var expired = await ctx.Auctions
            .Where(a => a.Status == AuctionStatus.Active && a.EndTime <= DateTime.UtcNow)
            .ToListAsync(ct);

        if (!expired.Any()) return;

        _logger.LogInformation("Processing {Count} expired auctions", expired.Count);

        foreach (var auction in expired)
        {
            await EndAuctionAsync(auction, ctx, ns, hub, ct);
        }
    }

    // Завершает один аукцион: определяет победителя, меняет статус, рассылает события.

    private async Task EndAuctionAsync(
        AuctionItem auction,
        AuctionDbContext ctx,
        NotificationService ns,
        IHubContext<AuctionHub> hub,
        CancellationToken ct)
    {
        var best = await ctx.Bids
            .Include(b => b.Bidder)
            .Where(b => b.AuctionId == auction.Id)
            .OrderByDescending(b => b.Amount)
            .FirstOrDefaultAsync(ct);

        auction.Status = AuctionStatus.Ended;

        if (best != null)
        {
            auction.WinnerId = best.BidderId;
            await ctx.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Auction {Id} ended. Winner={WinnerId} FinalPrice={Price}",
                auction.Id, best.BidderId, best.Amount);

            await ns.NotifyWinnerAsync(best.BidderId, auction.Id, auction.Title);

            await hub.Clients.Group(auction.Id.ToString()).SendAsync("AuctionEnded", new
            {
                id         = auction.Id,
                title      = auction.Title,
                winnerId   = best.BidderId,
                winnerName = best.Bidder.DisplayName,
                finalPrice = best.Amount,
                status     = "Ended"
            }, ct);
        }
        else
        {
            await ctx.SaveChangesAsync(ct);

            _logger.LogInformation("Auction {Id} ended with no bids", auction.Id);

            await ns.NotifyNoBidsAsync(auction.SellerId, auction.Id, auction.Title);

            await hub.Clients.Group(auction.Id.ToString()).SendAsync("AuctionEnded", new
            {
                id     = auction.Id,
                title  = auction.Title,
                status = "Ended"
            }, ct);
        }
    }

    // Уведомления о скором завершении 

    private async Task ProcessEndingSoonNotifications(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var ns  = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now  = DateTime.UtcNow;
        var soon = now.AddMinutes(5);

        var auctions = await ctx.Auctions
            .Where(a => a.Status == AuctionStatus.Active
                     && a.EndTime > now
                     && a.EndTime <= soon
                     && a.BidCount > 0)
            .ToListAsync(ct);

        foreach (var auction in auctions)
        {
            if (!SentWarnings.TryAdd(auction.Id, true)) continue;

            var bidderIds = await ctx.Bids
                .Where(b => b.AuctionId == auction.Id)
                .Select(b => b.BidderId)
                .Distinct()
                .ToListAsync(ct);

            if (bidderIds.Any())
                await ns.NotifyAuctionEndingAsync(auction.Id, bidderIds, auction.Title);
        }
    }

    private async Task<TimeSpan> CalculateNextCheckDelay(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();

        var next = await ctx.Auctions
            .Where(a => a.Status == AuctionStatus.Active && a.EndTime > DateTime.UtcNow)
            .OrderBy(a => a.EndTime)
            .Select(a => a.EndTime)
            .FirstOrDefaultAsync(ct);

        if (next == default) return TimeSpan.FromMinutes(1);

        var delay = next - DateTime.UtcNow + TimeSpan.FromSeconds(1);
        if (delay <= TimeSpan.Zero) return TimeSpan.FromSeconds(1);
        return delay > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }
}
