using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Auction.DTOs;
using Auction.Services;

namespace Auction.Controllers;


[ApiController]
[Route("api/auctions")]
public class AuctionsController : ControllerBase
{
    private readonly AuctionService _auctionService;
    private readonly BidService _bidService;
    private readonly NotificationService _notificationService;
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<AuctionsController> _logger;

    public AuctionsController(
        AuctionService auctionService,
        BidService bidService,
        NotificationService notificationService,
        IHubContext<AuctionHub> hubContext,
        ILogger<AuctionsController> logger)
    {
        _auctionService      = auctionService;
        _bidService          = bidService;
        _notificationService = notificationService;
        _hubContext          = hubContext;
        _logger              = logger;
    }



    // Список аукционов с фильтрацией и пагинацией
    [HttpGet]
    public async Task<IActionResult> GetAuctions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? category = null,
        [FromQuery] string? sort = "ending_soon",
        [FromQuery] string? status = "Active",
        [FromQuery] string? search = null)
    {
        var result = await _auctionService.GetAuctionsAsync(page, pageSize, category, sort, status);

        if (!string.IsNullOrWhiteSpace(search))
            result.Items = result.Items
                .Where(a => a.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

        return Ok(result);
    }

    // Детали одного аукциона
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAuction(int id)
    {
        var auction = await _auctionService.GetAuctionDetailsAsync(id);
        if (auction == null) return NotFound(new { message = "Auction not found" });
        return Ok(auction);
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Создать аукцион.</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateAuction([FromBody] CreateAuctionRequest request)
    {
        try
        {
            var userId  = GetUserId();
            var auction = await _auctionService.CreateAuctionAsync(request, userId, new List<string>());

            // Сообщаем всем подключённым клиентам о новом лоте
            await _hubContext.Clients.All.SendAsync("NewAuction", auction);

            return CreatedAtAction(nameof(GetAuction), new { id = auction.Id }, auction);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Обновить аукцион (только до первой ставки).</summary>
    [HttpPut("{id:int}")]
    [Authorize]
    public async Task<IActionResult> UpdateAuction(int id, [FromBody] CreateAuctionRequest request)
    {
        try
        {
            await _auctionService.UpdateAuctionAsync(id, GetUserId(), request);
            return Ok(new { message = "Auction updated" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Разместить ставку.</summary>
    [HttpPost("{id:int}/bids")]
    [Authorize]
    public async Task<IActionResult> PlaceBid(int id, [FromBody] PlaceBidRequest request)
    {
        try
        {
            var userId          = GetUserId();
            var bidderName      = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

            // Имя биддера передаём в сервис — он знает, как собрать DTO
            var (bid, previousBidderId) = await _bidService.PlaceBidAsync(id, userId, request.Amount, bidderName);

            _logger.LogInformation(
                "Bid accepted: AuctionId={AuctionId} UserId={UserId} Amount={Amount}",
                id, userId, request.Amount);

            // Широковещание обновления ставки всем в группе аукциона
            await _hubContext.Clients.Group(id.ToString()).SendAsync("BidPlaced", bid);

            // Уведомление перебитому участнику (только если он существует)
            if (previousBidderId.HasValue)
                await _notificationService.NotifyOutbidAsync(previousBidderId.Value, id, request.Amount);

            // BuyNow: аукцион закрылся немедленно
            if (bid.IsBuyNow)
            {
                var auctionDetail = await _auctionService.GetAuctionDetailsAsync(id);
                await _hubContext.Clients.Group(id.ToString()).SendAsync("AuctionEnded", auctionDetail);
                await _notificationService.NotifyWinnerAsync(userId, id, auctionDetail?.Title ?? "Unknown");
            }

            return Ok(bid);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Продавец подтверждает сделку (Ended → Sold).</summary>
    [HttpPost("{id:int}/confirm-sale")]
    [Authorize]
    public async Task<IActionResult> ConfirmSale(int id)
    {
        try
        {
            var sellerId = GetUserId();

            // Получаем аукцион ДО смены статуса, чтобы знать winnerId для уведомления
            var auctionBefore = await _auctionService.GetAuctionDetailsAsync(id);

            await _auctionService.ConfirmSaleAsync(id, sellerId);

            // Широковещание о продаже
            await _hubContext.Clients.Group(id.ToString()).SendAsync("AuctionSold", new { auctionId = id });

            // Уведомление победителю и продавцу о подтверждении
            if (auctionBefore?.WinnerId is int winnerId)
                await _notificationService.NotifySaleConfirmedAsync(
                    sellerId, winnerId, id, auctionBefore.Title);

            return Ok(new { message = "Sale confirmed" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Отменить аукцион (только до первой ставки).</summary>
    [HttpDelete("{id:int}")]
    [Authorize]
    public async Task<IActionResult> CancelAuction(int id)
    {
        try
        {
            await _auctionService.CancelAuctionAsync(id, GetUserId());
            await _hubContext.Clients.Group(id.ToString()).SendAsync("AuctionCanceled", new { auctionId = id });
            return Ok(new { message = "Auction cancelled" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID claim missing");
        return int.Parse(claim.Value);
    }
}
