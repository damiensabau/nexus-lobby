namespace VpnClient.App.Services;

public enum PlayerBase
{
    Low,
    Medium,
    High,
}

/// <summary>Note « lobby facile » d'une région à un instant donné (0 = difficile, 100 = idéal).</summary>
public sealed record LobbyScore(int Value, string Grade, string Reason, bool PingWarning);

/// <summary>
/// Estime les chances de tomber sur des lobbies plus faciles dans une région.
///
/// Principe (sources dans le README) :
/// - Heure locale : l'activité des joueurs est minimale vers 3 h–8 h et maximale vers 19 h–23 h, heure du serveur.
///   Moins de joueurs connectés = matchmaking plus permissif sur le niveau pour garder un temps d'attente court.
/// - Jour : du lundi au jeudi, moins de joueurs que le week-end.
/// - Population de la région : les régions à petite base de joueurs (Kenya, Inde, Asie centrale…) reviennent
///   dans la plupart des retours de joueurs. Estimation, pas de chiffres officiels.
/// - Ping : Activision indique que la connexion est le critère le plus important du matchmaking, et a durci les
///   seuils de ping en novembre 2024 pour garder les joueurs dans leur région. Un ping trop élevé dégrade le jeu
///   et peut annuler l'effet du changement de région.
///
/// Rien de tout ça n'est garanti : c'est une heuristique, pas une donnée officielle.
/// </summary>
public static class LobbyAdvisor
{
    private const double TimeWeight = 0.45;
    private const double PopulationWeight = 0.25;
    private const double PingWeight = 0.20;
    private const double DayWeight = 0.10;

    // Activité relative des joueurs selon l'heure locale (0 h → 23 h), 1 = pic du soir.
    private static readonly double[] Activity =
    [
        0.75, 0.55, 0.38, 0.25, 0.17, 0.13, 0.15, 0.20, 0.26, 0.32, 0.38, 0.43,
        0.50, 0.53, 0.56, 0.62, 0.70, 0.80, 0.88, 0.95, 1.00, 1.00, 0.95, 0.85,
    ];

    // Base de joueurs estimée par pays (codes ISO). Les pays absents sont considérés « Medium ».
    private static readonly Dictionary<string, PlayerBase> Population = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = PlayerBase.High, ["CA"] = PlayerBase.High, ["GB"] = PlayerBase.High, ["FR"] = PlayerBase.High,
        ["DE"] = PlayerBase.High, ["NL"] = PlayerBase.High, ["BE"] = PlayerBase.High, ["ES"] = PlayerBase.High,
        ["IT"] = PlayerBase.High, ["AU"] = PlayerBase.High, ["MX"] = PlayerBase.High, ["BR"] = PlayerBase.High,
        ["KE"] = PlayerBase.Low, ["GH"] = PlayerBase.Low, ["NG"] = PlayerBase.Low, ["EG"] = PlayerBase.Low,
        ["MA"] = PlayerBase.Low, ["IN"] = PlayerBase.Low, ["PK"] = PlayerBase.Low, ["BD"] = PlayerBase.Low,
        ["KG"] = PlayerBase.Low, ["VN"] = PlayerBase.Low, ["KH"] = PlayerBase.Low, ["TH"] = PlayerBase.Low,
        ["ID"] = PlayerBase.Low, ["PH"] = PlayerBase.Low, ["TW"] = PlayerBase.Low, ["IL"] = PlayerBase.Low,
        ["ZA"] = PlayerBase.Low, ["PE"] = PlayerBase.Low, ["CO"] = PlayerBase.Low,
    };

    public static PlayerBase PlayerBaseOf(Location location) =>
        Population.GetValueOrDefault(location.CountryCode, PlayerBase.Medium);

    public static LobbyScore Score(Location location, long? pingMs, DateTime utcNow)
    {
        if (location.LocalTimeAt(utcNow) is not { } local)
            return new LobbyScore(0, "Inconnu", "Fuseau horaire inconnu", false);

        var time = 1 - ActivityAt(local);
        var day = DayFactor(local);
        var playerBase = PlayerBaseOf(location);
        var population = playerBase switch
        {
            PlayerBase.Low => 1.0,
            PlayerBase.Medium => 0.55,
            _ => 0.15,
        };
        var ping = pingMs switch
        {
            null => 0.5,   // inconnu : certains serveurs bloquent le ping
            < 80 => 1.0,
            < 150 => 0.65,
            < 220 => 0.3,
            _ => 0.0,
        };

        var value = (int)Math.Round(100 * (TimeWeight * time + PopulationWeight * population + PingWeight * ping + DayWeight * day));
        var pingWarning = pingMs >= 150;

        var reasons = new List<string> { $"{local:HH:mm} sur place" };
        reasons.Add(playerBase switch
        {
            PlayerBase.Low => "peu de joueurs",
            PlayerBase.Medium => "population moyenne",
            _ => "beaucoup de joueurs",
        });
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) reasons.Add("week-end");
        if (pingMs is { } ms) reasons.Add(pingWarning ? $"ping élevé ({ms} ms)" : $"ping {ms} ms");

        return new LobbyScore(value, GradeOf(value), string.Join(" · ", reasons), pingWarning);
    }

    public static string GradeOf(int value) => value switch
    {
        >= 75 => "Excellent",
        >= 58 => "Bon",
        >= 40 => "Moyen",
        _ => "Difficile",
    };

    /// <summary>Meilleure région du catalogue à l'instant, parmi celles qu'on n'a pas encore (ping inconnu).</summary>
    public static (Location Location, LobbyScore Score)? BestMissing(IEnumerable<string?> ownedLocationIds, DateTime utcNow)
    {
        var owned = ownedLocationIds.Where(id => id is not null).ToHashSet();
        return Locations.All
            .Where(l => !owned.Contains(l.Id))
            .Select(l => (Location: l, Score: Score(l, null, utcNow)))
            .MaxBy(x => x.Score.Value);
    }

    private static double ActivityAt(DateTime local)
    {
        // Interpolation linéaire entre deux heures pour éviter les sauts à chaque changement d'heure.
        var h = local.Hour;
        var t = local.Minute / 60.0;
        return Activity[h] * (1 - t) + Activity[(h + 1) % 24] * t;
    }

    private static double DayFactor(DateTime local) => local.DayOfWeek switch
    {
        DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday => 1.0,
        DayOfWeek.Friday => local.Hour >= 17 ? 0.4 : 0.8,
        _ => 0.3,
    };
}
