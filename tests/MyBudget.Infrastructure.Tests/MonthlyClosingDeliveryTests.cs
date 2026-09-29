using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using MyBudget.Application.Dates;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;
using MyBudget.Infrastructure.Persistence.Repositories;
using NSubstitute;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The scheduled closing end to end across the claim boundary.
/// <para>
/// The application service is the real one and the marker lives in PostgreSQL, so this is what
/// proves the delivery is exactly-once per user and month: a second pass on the same day finds
/// the claim already taken and prepares nothing.
/// </para>
/// </summary>
public sealed class MonthlyClosingDeliveryTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly MonthPeriod August = new(2026, 8);

    [Fact]
    public async Task A_month_is_prepared_once_and_the_second_pass_finds_the_claim_taken()
    {
        await using var context = CreateContext();
        var user = new User(999);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // 05:00 UTC on 1 September is the first local day of September in Bogotá.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 5, 0, 0, TimeSpan.Zero));
        var reports = Substitute.For<IReportService>();
        reports.GetMonthlySummaryAsync(user.Id, August, Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(August, [new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 1_000_000, 1_200_000)]));
        reports.GetStatisticsAsync(user.Id, August, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new PeriodStatistics(
                August, 1_200_000, 10, 31, 0, [], [], [],
                new PeriodComparison(August, August.Previous, 1_200_000, 900_000, false, false)));

        var service = new MonthlyClosingService(
            new UserRepository(context),
            reports,
            new MonthlyClosingStore(context),
            new UserLocalDate(clock),
            clock);

        var first = await service.PrepareDueAsync();
        var second = await service.PrepareDueAsync();

        first.Should().ContainSingle()
            .Which.ClosedPeriod.Should().Be(August);
        second.Should().BeEmpty();

        await using var verification = CreateContext();
        (await verification.MonthlyClosings.CountAsync()).Should().Be(1);
    }
}
