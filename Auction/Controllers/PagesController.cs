using Auction.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auction.Controllers;

/// <summary>
/// MVC-контроллер для серверного рендеринга HTML-страниц.
/// Не содержит бизнес-логики — только подготовка данных для Views.
/// </summary>
public class PagesController : Controller
{
    private readonly AuctionService _auctionService;

    public PagesController(AuctionService auctionService)
    {
        _auctionService = auctionService;
    }

    [HttpGet("/auctions")]
    public async Task<IActionResult> Auctions(
        string? category = null,
        string? status = "Active",
        string? sort = "ending_soon",
        string? search = null)
    {
        // Поиск делегируется сервису через параметр — контроллер не фильтрует в памяти
        var result = await _auctionService.GetAuctionsAsync(1, 50, category, sort, status, search);
        return View("~/Views/Auctions/Index.cshtml", result.Items);
    }

    [HttpGet("/auctions/detail/{id:int}")]
    public async Task<IActionResult> AuctionDetail(int id)
    {
        var auction = await _auctionService.GetAuctionDetailsAsync(id);
        if (auction == null) return NotFound();

        // CurrentUserId нужен View для отображения кнопок (ставка / редактировать)
        // Это UI-контекст, поэтому он живёт здесь, а не в AuctionDetailDto
        int? currentUserId = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (claim != null) currentUserId = int.Parse(claim.Value);
        }

        ViewBag.CurrentUserId = currentUserId;
        return View("~/Views/Auctions/Detail.cshtml", auction);
    }

    [HttpGet("/auctions/create")]
    public IActionResult CreateAuction() => View("~/Views/Auctions/Create.cshtml");

    [HttpGet("/auctions/edit/{id:int}")]
    public async Task<IActionResult> EditAuction(int id)
    {
        var auction = await _auctionService.GetAuctionDetailsAsync(id);
        if (auction == null) return NotFound();
        if (auction.BidCount > 0) return Redirect($"/auctions/detail/{id}");
        return View("~/Views/Auctions/Edit.cshtml", auction);
    }
}
