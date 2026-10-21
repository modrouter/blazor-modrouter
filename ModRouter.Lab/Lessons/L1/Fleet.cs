namespace ModRouter.Lab.Lessons.L1;

public sealed record Ship(int Id, string Name, string Class, int Crew, string Role);

/// <summary>The little fleet the first lesson navigates through.</summary>
public static class Fleet
{
    public static IReadOnlyList<Ship> Ships { get; } =
    [
        new(1, "Meridian", "Courier", 6, "Carries messages between stations."),
        new(2, "Halcyon", "Survey", 14, "Maps the edge of the charted sector."),
        new(3, "Orrery", "Observatory", 22, "Watches the slow clocks of dead stars."),
        new(4, "Tallow", "Freighter", 31, "Hauls ice and ore on a fixed schedule."),
    ];

    public static Ship? Find(int id) => Ships.FirstOrDefault(s => s.Id == id);
}
