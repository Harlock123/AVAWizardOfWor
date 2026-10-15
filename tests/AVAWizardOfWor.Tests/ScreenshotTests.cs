using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAWizardOfWor.Engine;
using AVAWizardOfWor.Views;

namespace AVAWizardOfWor.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set WOR_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avawizardofwor-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("WOR_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAWizardOfWor-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer warrior play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 71250, 9);
        g.HighScores.Insert("AVA", 52800, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_first_dungeon()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(9);
        g.Idle(60);
        c.Advance(0.5);
        Shot(w, c, "dungeon-start");
        Assert.True(PlayUntil(g, c, g => g.Shot != null && g.Warrior.Alive && !g.Warrior.InCage && g.Enemies.Count(e => e.Kind == EnemyKind.Prowler) >= 3
                                       && g.Enemies.Any(e => Math.Abs(e.X - g.Warrior.X) + Math.Abs(e.Y - g.Warrior.Y) < 4)));
        Shot(w, c, "play");
        Assert.True(PlayUntil(g, c, g => g.Enemies.Any(e => e.Kind is EnemyKind.Shade or EnemyKind.Phantom && e.Seen && e.Age > 1.5) && g.Warrior.Alive));
        Shot(w, c, "shade");
    }

    [AvaloniaFact]
    public void The_wisp_and_the_wizard()
    {
        var (w, c, g) = Open(5);
        g.Go(2);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Enemies.Any(e => e.Kind == EnemyKind.Wisp && e.Age > 1.2) && g.Warrior.Alive, 200));
        Shot(w, c, "wisp");
        // The wizard, set up so it's sure to come: a fresh dungeon emptied, the wisp slain, and a warrior standing still.
        g.Go(2);
        g.SetLives(9);
        foreach (var e in g.Enemies) e.Dead = true;
        g.Idle(90);
        g.Slay(g.Enemies.Single(e => e.Kind == EnemyKind.Wisp));
        g.PutWarrior(3, 3, Dir.Right);
        bool fired = false;
        for (int t = 0; t < 60 * 15 && !fired; t++)
        {
            g.Tick(default);
            if (t % 3 == 0) c.Advance(1 / 20.0);
            fired = g.Bullets.Any(b => b.FromWizard && Math.Abs(b.X - g.Warrior.X) + Math.Abs(b.Y - g.Warrior.Y) > 1.2) && g.Enemies.Any(e => e.ShowFor <= 0);
        }
        Assert.True(fired);
        Shot(w, c, "wizard");
    }

    [AvaloniaFact]
    public void The_courtyard_and_the_hollow()
    {
        var (w, c, g) = Open(3);
        g.Go(4);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Dungeon == 4 && g.DungeonTime > 3 && g.Warrior is { Alive: true, InCage: false } && g.Enemies.Count >= 3));
        Shot(w, c, "courtyard");
        g.Go(13);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.DungeonTime > 4 && g.Warrior is { Alive: true, InCage: false } && g.Shot != null && g.Enemies.Count >= 4));
        Shot(w, c, "hollow");
    }

    [AvaloniaFact]
    public void A_warrior_lost()
    {
        var (w, c, g) = Open();
        g.Go(3);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.Warrior is { Alive: true, InCage: false } && g.DungeonTime > 6));
        var p = g.PutEnemy(EnemyKind.Phantom, g.Warrior.Cx, g.Warrior.Cy);
        g.Idle(15);
        c.Advance(0.3);
        Shot(w, c, "lost");
    }
}
