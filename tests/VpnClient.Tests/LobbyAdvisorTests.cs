using VpnClient.App.Services;

namespace VpnClient.Tests;

public class LobbyAdvisorTests
{
    private static Location L(string id) => Locations.Find(id) ?? throw new ArgumentException(id);

    // Mercredi 14 octobre 2026, 02:00 UTC : 05:00 à Nairobi, 22:00 (mardi) à New York.
    private static readonly DateTime WednesdayNightUtc = new(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Night_in_small_region_beats_evening_in_big_region()
    {
        var nairobi = LobbyAdvisor.Score(L("ke-nbo"), 90, WednesdayNightUtc);
        var newYork = LobbyAdvisor.Score(L("us-nyc"), 90, WednesdayNightUtc);

        Assert.True(nairobi.Value > newYork.Value, $"Nairobi {nairobi.Value} <= New York {newYork.Value}");
        Assert.Equal("Excellent", nairobi.Grade);
        Assert.Equal("Difficile", newYork.Grade);
        Assert.StartsWith("05:00 sur place", nairobi.Reason);
    }

    [Fact]
    public void Weekend_lowers_the_score()
    {
        var saturdayNightUtc = new DateTime(2026, 10, 17, 2, 0, 0, DateTimeKind.Utc);
        var weekday = LobbyAdvisor.Score(L("in-bom"), 100, WednesdayNightUtc);
        var weekend = LobbyAdvisor.Score(L("in-bom"), 100, saturdayNightUtc);

        Assert.True(weekend.Value < weekday.Value);
        Assert.Contains("week-end", weekend.Reason);
    }

    [Fact]
    public void High_ping_lowers_the_score_and_warns()
    {
        var good = LobbyAdvisor.Score(L("ke-nbo"), 40, WednesdayNightUtc);
        var unknown = LobbyAdvisor.Score(L("ke-nbo"), null, WednesdayNightUtc);
        var bad = LobbyAdvisor.Score(L("ke-nbo"), 300, WednesdayNightUtc);

        Assert.True(good.Value > unknown.Value && unknown.Value > bad.Value);
        Assert.False(good.PingWarning);
        Assert.True(bad.PingWarning);
        Assert.Contains("ping élevé (300 ms)", bad.Reason);
    }

    [Fact]
    public void Scores_stay_between_0_and_100_all_week_for_every_location()
    {
        var start = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        foreach (var location in Locations.All)
        {
            for (var hour = 0; hour < 24 * 7; hour += 1)
            {
                foreach (long? ping in new long?[] { null, 0, 500 })
                {
                    var score = LobbyAdvisor.Score(location, ping, start.AddHours(hour));
                    Assert.InRange(score.Value, 0, 100);
                }
            }
        }
    }

    [Fact]
    public void Score_follows_the_daily_curve()
    {
        // Même jour, même région : 05:00 sur place doit battre 21:00 sur place.
        var paris = L("fr-par");
        var morning = LobbyAdvisor.Score(paris, 30, new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc));  // 05:00 CEST
        var evening = LobbyAdvisor.Score(paris, 30, new DateTime(2026, 10, 14, 19, 0, 0, DateTimeKind.Utc)); // 21:00 CEST

        Assert.True(morning.Value - evening.Value >= 30, $"matin {morning.Value}, soir {evening.Value}");
    }

    [Fact]
    public void Best_missing_location_skips_owned_ones()
    {
        var best = LobbyAdvisor.BestMissing([], WednesdayNightUtc);
        Assert.NotNull(best);

        var again = LobbyAdvisor.BestMissing([best.Value.Location.Id, null], WednesdayNightUtc);
        Assert.NotNull(again);
        Assert.NotEqual(best.Value.Location.Id, again.Value.Location.Id);
    }

    [Theory]
    [InlineData(80, "Excellent")]
    [InlineData(75, "Excellent")]
    [InlineData(60, "Bon")]
    [InlineData(45, "Moyen")]
    [InlineData(10, "Difficile")]
    public void Grades(int value, string grade) => Assert.Equal(grade, LobbyAdvisor.GradeOf(value));
}
