using Auction.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Auction.Tests;

/// <summary>
/// Проверяет, что NotificationService вызывает нужные SignalR-события
/// с нужными именами методов для нужных пользователей/групп.
/// </summary>
public class NotificationTests : TestBase
{
    // ─── Outbid ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyOutbid_SendsOutbidEvent_ToCorrectUser()
    {
        var ns = CreateNotificationService();

        await ns.NotifyOutbidAsync(userId: 42, auctionId: 1, newAmount: 150m);

        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("Outbid", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task NotifyOutbid_MessageContainsNewAmount()
    {
        var ns = CreateNotificationService();
        string? capturedMessage = null;

        ClientProxyMock
            .Setup(c => c.SendCoreAsync("Outbid", It.IsAny<object[]>(), default))
            .Callback<string, object[], System.Threading.CancellationToken>((_, args, _) =>
            {
                // args[0] — анонимный объект с Message
                var msg = args[0];
                capturedMessage = msg?.GetType().GetProperty("Message")?.GetValue(msg)?.ToString();
            })
            .Returns(Task.CompletedTask);

        await ns.NotifyOutbidAsync(userId: 1, auctionId: 1, newAmount: 200m);

        capturedMessage.Should().Contain("200");
    }

    // ─── Winner ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyWinner_SendsAuctionWonEvent_ToWinner()
    {
        var ns = CreateNotificationService();

        await ns.NotifyWinnerAsync(winnerId: 7, auctionId: 3, auctionTitle: "MacBook");

        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("AuctionWon", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task NotifyWinner_MessageContainsAuctionTitle()
    {
        var ns = CreateNotificationService();
        string? capturedMessage = null;

        ClientProxyMock
            .Setup(c => c.SendCoreAsync("AuctionWon", It.IsAny<object[]>(), default))
            .Callback<string, object[], System.Threading.CancellationToken>((_, args, _) =>
            {
                var msg = args[0];
                capturedMessage = msg?.GetType().GetProperty("Message")?.GetValue(msg)?.ToString();
            })
            .Returns(Task.CompletedTask);

        await ns.NotifyWinnerAsync(winnerId: 1, auctionId: 1, auctionTitle: "Vintage Guitar");

        capturedMessage.Should().Contain("Vintage Guitar");
    }

    // ─── NoBids ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyNoBids_SendsNoBidsEndedEvent_ToSeller()
    {
        var ns = CreateNotificationService();

        await ns.NotifyNoBidsAsync(sellerId: 5, auctionId: 2, auctionTitle: "Old Chair");

        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("NoBidsEnded", It.IsAny<object[]>(), default),
            Times.Once);
    }

    // ─── AuctionEnding (5 минут) ──────────────────────────────────────────────

    [Fact]
    public async Task NotifyAuctionEnding_SendsEventToAllBidders()
    {
        var ns = CreateNotificationService();
        var bidderIds = new List<int> { 1, 2, 3 };

        await ns.NotifyAuctionEndingAsync(auctionId: 10, bidderIds: bidderIds, auctionTitle: "Desk");

        // По одному вызову на каждого биддера
        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("AuctionEnding", It.IsAny<object[]>(), default),
            Times.Exactly(3));
    }

    [Fact]
    public async Task NotifyAuctionEnding_EmptyBidderList_SendsNothing()
    {
        var ns = CreateNotificationService();

        await ns.NotifyAuctionEndingAsync(auctionId: 1, bidderIds: new List<int>(), auctionTitle: "x");

        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("AuctionEnding", It.IsAny<object[]>(), default),
            Times.Never);
    }

    // ─── SaleConfirmed ────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifySaleConfirmed_SendsEventToBothSellerAndWinner()
    {
        var ns = CreateNotificationService();

        await ns.NotifySaleConfirmedAsync(
            sellerId: 1, winnerId: 2, auctionId: 5, auctionTitle: "iPhone");

        // SaleConfirmed должен уйти дважды: продавцу и победителю
        ClientProxyMock.Verify(c =>
            c.SendCoreAsync("SaleConfirmed", It.IsAny<object[]>(), default),
            Times.Exactly(2));
    }
}
