using Microsoft.AspNetCore.SignalR;
using Auction.DTOs;

namespace Auction.Services;

/// <summary>
/// SignalR Hub: управление группами и push-запросы актуального состояния.
/// Бизнес-логики здесь нет — только маршрутизация сообщений.
/// </summary>
public class AuctionHub : Hub
{
    private readonly AuctionService _auctionService;
    private readonly ILogger<AuctionHub> _logger;

    public AuctionHub(AuctionService auctionService, ILogger<AuctionHub> logger)
    {
        _auctionService = auctionService;
        _logger = logger;
    }

    /// <summary>Клиент подписывается на обновления аукциона и сразу получает его состояние.</summary>
    public async Task JoinAuctionGroup(int auctionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, auctionId.ToString());
        _logger.LogInformation("Connection {ConnectionId} joined auction group {AuctionId}",
            Context.ConnectionId, auctionId);

        // Отправляем актуальное состояние сразу при подключении/переподключении
        var details = await _auctionService.GetAuctionDetailsAsync(auctionId);
        if (details != null)
            await Clients.Caller.SendAsync("AuctionDetails", details);
    }

    /// <summary>Клиент отписывается от группы (явный уход со страницы).</summary>
    public async Task LeaveAuctionGroup(int auctionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, auctionId.ToString());
        _logger.LogInformation("Connection {ConnectionId} left auction group {AuctionId}",
            Context.ConnectionId, auctionId);
    }

    /// <summary>Запрос страницы аукционов через SignalR (для клиентов без HTTP-доступа).</summary>
    public async Task GetActiveAuctions(int page = 1, string? category = null,
        string? sort = null, string? status = "Active")
    {
        var result = await _auctionService.GetAuctionsAsync(page, 10, category, sort, status);
        await Clients.Caller.SendAsync("AuctionsList", result);
    }

    /// <summary>
    /// При разрыве соединения SignalR сам удаляет его из групп.
    /// Логируем для трассировки жизненного цикла.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Connection {ConnectionId} disconnected. Error: {Error}",
            Context.ConnectionId, exception?.Message ?? "none");
        await base.OnDisconnectedAsync(exception);
    }
}
