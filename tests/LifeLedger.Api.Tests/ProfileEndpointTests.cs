using System.Net;
using System.Net.Http.Json;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies profile settings and cross-country career persistence.</summary>
public sealed class ProfileEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    /// <summary>Hosts the API with an isolated temporary database.</summary>
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test class with its shared in-process API host.</summary>
    public ProfileEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Updates birth date and sex without corrupting existing career periods.</summary>
    [Fact]
    public async Task Updating_profile_settings_preserves_existing_careers()
    {
        var profileId = Guid.NewGuid();
        var polishCareerId = Guid.NewGuid();
        var frenchCareerId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Profiles.Add(new Profile
            {
                Id = profileId,
                DisplayName = "International career profile",
                BirthDate = new DateOnly(1990, 1, 1),
                Careers =
                [
                    new CareerPeriod { Id = polishCareerId, CountryCode = "PL", StartedOn = new DateOnly(2015, 1, 1), AnnualInsurableIncome = 60_000m },
                    new CareerPeriod { Id = frenchCareerId, CountryCode = "FR", StartedOn = new DateOnly(2020, 1, 1), AnnualInsurableIncome = 45_000m }
                ]
            });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/profiles/{profileId}", new
        {
            id = profileId,
            displayName = "International career profile",
            birthDate = "1988-07-17",
            sex = "Male",
            homeCountryCode = "PL",
            baseCurrency = "EUR",
            expectedLifespan = 82,
            childrenCount = 0,
            careers = new[]
            {
                new { id = polishCareerId, profileId, countryCode = "PL", startedOn = "2015-01-01", endedOn = (string?)null, annualInsurableIncome = 60_000m, estimatedMonthlyPublicPension = 0m, pensionAge = 65, notes = "" },
                new { id = frenchCareerId, profileId, countryCode = "FR", startedOn = "2020-01-01", endedOn = (string?)null, annualInsurableIncome = 45_000m, estimatedMonthlyPublicPension = 0m, pensionAge = 65, notes = "" }
            }
        });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var updatedProfile = await verificationDatabase.Profiles.AsNoTracking().Include(profile => profile.Careers).SingleAsync(profile => profile.Id == profileId);
        Assert.Equal(new DateOnly(1988, 7, 17), updatedProfile.BirthDate);
        Assert.Equal(ProfileSex.Male, updatedProfile.Sex);
        Assert.Equal(2, updatedProfile.Careers.Count);
        Assert.Contains(updatedProfile.Careers, career => career.CountryCode == "PL");
        Assert.Contains(updatedProfile.Careers, career => career.CountryCode == "FR");
    }
}
