using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The settings endpoint is called directly where it is under test; the Api helpers only arrange state.
public sealed class PlanningSettingsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Put_ValidMinimum_IsUsedByThePreview()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        var byDefault = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        var saved = await client.PutAsJsonAsync(
            PlanningSettingsUri, new { minimumTransferPln = 250, paymentLeadDays = 7 }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(100, byDefault.MinimumTransferPln);
        Assert.Equal(250, (await client.GetTransferPlanAsync("2026-10", cancellationToken)).MinimumTransferPln);

        await client.SavePlanningSettingsAsync(cancellationToken, minimumTransferPln: 2_000);
        var strict = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        var inwestycje = strict.Rows.Where(r => r.BucketName == "Inwestycje").ToList();
        var row = Assert.Single(inwestycje);
        Assert.Equal(1_979.27m, row.AmountPln);
        Assert.True(row.Marker.Is(TransferMarker.BelowMinimum));
    }

    [Fact]
    public async Task Put_InvalidMinimum_ReturnsBadRequestAndKeepsTheSetting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SavePlanningSettingsAsync(cancellationToken, minimumTransferPln: 250, paymentLeadDays: 14);

        foreach (var minimum in new[] { -1d, 12.5d, 1_000_001d })
        {
            var response = await client.PutAsJsonAsync(
                PlanningSettingsUri, new { minimumTransferPln = minimum, paymentLeadDays = 7 }, cancellationToken);

            await response.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidMinimumTransferErrorCode, cancellationToken);
        }

        Assert.Equal(new PlanningSettingsView(250, 14), await client.GetPlanningSettingsAsync(cancellationToken));
    }

    [Fact]
    public async Task LeadDays_DefaultToSevenAndDriveTheTodo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateRecurringExpenseAsync(
            cancellationToken, "Przegląd", 400m, new DateOnly(2026, 10, 30), intervalMonths: 6, dayOfMonth: 30);

        var defaults = await client.GetPlanningSettingsAsync(cancellationToken);
        var withDefault = await client.GetTodoAsync(cancellationToken);
        var saved = await client.PutAsJsonAsync(
            PlanningSettingsUri, new { minimumTransferPln = 100, paymentLeadDays = 14 }, cancellationToken);
        var withFourteen = await client.GetTodoAsync(cancellationToken);

        Assert.Equal(new PlanningSettingsView(100, 7), defaults);
        Assert.Equal(7, withDefault.PaymentLeadDays);
        Assert.Empty(withDefault.Items);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(14, withFourteen.PaymentLeadDays);
        var item = Assert.Single(withFourteen.Items);
        Assert.Equal((UpcomingPaymentKind, "Przegląd", 10), (item.Kind, item.Name, item.DaysUntil));
        Assert.Equal(new PlanningSettingsView(100, 14), await client.GetPlanningSettingsAsync(cancellationToken));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    [InlineData(2.5)]
    public async Task Put_InvalidLeadDays_ReturnsBadRequestAndKeepsTheSetting(double leadDays)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SavePlanningSettingsAsync(cancellationToken, minimumTransferPln: 250, paymentLeadDays: 14);

        var response = await client.PutAsJsonAsync(
            PlanningSettingsUri, new { minimumTransferPln = 250, paymentLeadDays = leadDays }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidPaymentLeadDaysErrorCode, cancellationToken);
        Assert.Equal(new PlanningSettingsView(250, 14), await client.GetPlanningSettingsAsync(cancellationToken));
    }
}
