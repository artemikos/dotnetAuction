using Microsoft.AspNetCore.SignalR;

namespace Auction.Services;

public class NotificationService
{
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IHubContext<AuctionHub> hubContext, ILogger<NotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    // Уведомляет перебитого участника.
    public async Task NotifyOutbidAsync(int userId, int auctionId, decimal newAmount)
    {
        _logger.LogInformation(
            "Outbid notification: UserId={UserId} AuctionId={AuctionId} NewAmount={Amount}",
            userId, auctionId, newAmount);

        await _hubContext.Clients.User(userId.ToString()).SendAsync("Outbid", new
        {
            Type      = "Outbid",
            Message   = $"You've been outbid! Current bid: {newAmount:C}",
            AuctionId = auctionId
        });
    }

    //Рассылает предупреждение о скором завершении всем участвовавшим в ставках.
    public async Task NotifyAuctionEndingAsync(int auctionId, List<int> bidderIds, string auctionTitle)
    {
        _logger.LogInformation(
            "EndingSoon notification: AuctionId={AuctionId} Bidders={Count}",
            auctionId, bidderIds.Count);

        var tasks = bidderIds.Select(id =>
            _hubContext.Clients.User(id.ToString()).SendAsync("AuctionEnding", new
            {
                Type      = "AuctionEnding",
                Message   = $"Auction \"{auctionTitle}\" ends in 5 minutes!",
                AuctionId = auctionId
            }));

        await Task.WhenAll(tasks);
    }

    // Уведомляет победителя аукциона
    public async Task NotifyWinnerAsync(int winnerId, int auctionId, string auctionTitle)
    {
        _logger.LogInformation(
            "Winner notification: UserId={UserId} AuctionId={AuctionId}",
            winnerId, auctionId);

        await _hubContext.Clients.User(winnerId.ToString()).SendAsync("AuctionWon", new
        {
            Type      = "Won",
            Message   = $"You won \"{auctionTitle}\"!",
            AuctionId = auctionId
        });
    }

    // Уведомляет продавца о завершении без ставок
    public async Task NotifyNoBidsAsync(int sellerId, int auctionId, string auctionTitle)
    {
        _logger.LogInformation(
            "NoBids notification: SellerId={SellerId} AuctionId={AuctionId}",
            sellerId, auctionId);

        await _hubContext.Clients.User(sellerId.ToString()).SendAsync("NoBidsEnded", new
        {
            Type      = "NoBids",
            Message   = $"Your auction \"{auctionTitle}\" ended with no bids",
            AuctionId = auctionId
        });
    }

    // Уведомляет продавца и победителя о подтверждении продажи
    public async Task NotifySaleConfirmedAsync(int sellerId, int winnerId, int auctionId, string auctionTitle)
    {
        _logger.LogInformation(
            "SaleConfirmed notification: AuctionId={AuctionId} Seller={SellerId} Winner={WinnerId}",
            auctionId, sellerId, winnerId);

        await Task.WhenAll(
            _hubContext.Clients.User(sellerId.ToString()).SendAsync("SaleConfirmed", new
            {
                Type      = "SaleConfirmed",
                Message   = $"You confirmed the sale of \"{auctionTitle}\"",
                AuctionId = auctionId
            }),
            _hubContext.Clients.User(winnerId.ToString()).SendAsync("SaleConfirmed", new
            {
                Type      = "SaleConfirmed",
                Message   = $"The seller confirmed the sale of \"{auctionTitle}\". Please arrange pickup.",
                AuctionId = auctionId
            })
        );
    }
}
