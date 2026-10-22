using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The settings endpoint is called directly where it is under test; the Api helpers only arrange state.
public sealed class PlanningSettingsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{

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
            PlanningSettingsUri, new { paymentLeadDays = 14 }, cancellationToken);
        var withFourteen = await client.GetTodoAsync(cancellationToken);

        Assert.Equal(new PlanningSettingsView(7), defaults);
        Assert.Equal(7, withDefault.PaymentLeadDays);
        Assert.Empty(withDefault.Items);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(14, withFourteen.PaymentLeadDays);
        var item = Assert.Single(withFourteen.Items);
        Assert.Equal((UpcomingPaymentKind, "Przegląd", 10), (item.Kind, item.Name, item.DaysUntil));
        Assert.Equal(new PlanningSettingsView(14), await client.GetPlanningSettingsAsync(cancellationToken));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    [InlineData(2.5)]
    public async Task Put_InvalidLeadDays_ReturnsBadRequestAndKeepsTheSetting(double leadDays)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SavePlanningSettingsAsync(cancellationToken, paymentLeadDays: 14);

        var response = await client.PutAsJsonAsync(
            PlanningSettingsUri, new { paymentLeadDays = leadDays }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidPaymentLeadDaysErrorCode, cancellationToken);
        Assert.Equal(new PlanningSettingsView(14), await client.GetPlanningSettingsAsync(cancellationToken));
    }
}
