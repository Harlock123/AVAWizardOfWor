using AVAWizardOfWor.Engine;

namespace AVAWizardOfWor.Tests;

/// <summary>How the computer warrior gets on, dungeon by dungeon (set WOR_PROBE to run it, for tuning; WOR_PROBE=lazy for one that only stands and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("WOR_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "wor-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var dungeons = new List<string>();
            var last = g.Phase;
            int dungeon = 1, t = 0, wisps = 0, wizards = 0;
            double start = 0, still = 0;
            string? said = null;
            (double, double) at = (0, 0);
            for (; t < 60 * 900 && g.Dungeon <= 8; t++)
            {
                var c = mode == "lazy" ? new Controls(Dy: t % 600 < 300 ? -1 : 0, Fire: t % 20 < 10) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"D{g.Dungeon} {g.Cause}"] = deaths.GetValueOrDefault($"D{g.Dungeon} {g.Cause}") + 1;
                if (g.Message != said && g.Message == "THE WISP IS SLAIN") wisps++;
                if (g.Message != said && g.Message == "THE WIZARD IS DEFEATED!") wizards++;
                said = g.Message;
                // Standing still for a long time in play is a stall.
                if (g.Phase == Phase.Play && g.Warrior is { Alive: true, InCage: false } w)
                {
                    if ((w.X, w.Y) == at) still += 1 / 60.0; else { still = 0; at = (w.X, w.Y); }
                    if (still > 8) { output.WriteLine($"   stalled D{g.Dungeon} at ({w.X},{w.Y}) doing {g.Pilot.Doing}, {g.Enemies.Count} left: {string.Join(" ", g.Enemies.Select(e => $"{e.Kind}@({e.X:0.##},{e.Y:0.##}) {e.Dir} age {e.Age:0}"))} doors {g.Maze.DoorsOpen} shut {g.DoorShut:0.0}"); still = -999; }
                }
                if (g.Dungeon != dungeon) { dungeons.Add($"D{dungeon}:{(t - start) / 60:0}s"); dungeon = g.Dungeon; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: dungeon {g.Dungeon} after {t / 60:0}s score {g.Score} wisps {wisps} wizards {wizards} | {string.Join(" ", dungeons)}");
            output.WriteLine("   " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}
