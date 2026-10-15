using AVAWizardOfWor.Engine;

namespace AVAWizardOfWor.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avawizardofwor-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play in a dungeon with nothing in it unless a test puts it there. Dungeon 13 is the hollow one, with no walls inside.</summary>
    private static GameEngine Game(int dungeon = 13)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(dungeon);
        return g;
    }

    /// <summary>A real game in play (monsters coming), with plenty of warriors.</summary>
    private static GameEngine Real(int dungeon = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(dungeon);
        g.SetLives(99);
        return g;
    }

    private static Controls Press(Dir d, bool fire = false)
    {
        var (dx, dy) = Dirs.Vec(d);
        return new Controls(dx, dy, fire);
    }

    private static IEnumerable<(int C, int R)> Cells() =>
        Enumerable.Range(0, Field.Cols).SelectMany(c => Enumerable.Range(0, Field.Rows).Select(r => (c, r)));

    // ───────────────────────────── the dungeons ─────────────────────────────

    [Fact]
    public void Every_dungeon_is_mirrored_joined_up_and_has_no_dead_ends()
    {
        for (int d = 1; d <= 24; d++)
        {
            var m = Maze.Build(d);
            var dist = m.Distances(Field.CageX, Field.Rows - 1);
            Assert.All(Cells(), s => Assert.True(dist[s.C, s.R] >= 0, $"dungeon {d} cell {s}"));
            Assert.All(Cells(), s => Assert.True(Dirs.All.Count(w => m.Open(s.C, s.R, w)) >= 2, $"dungeon {d} dead end at {s}"));
            for (int c = 0; c < Maze.Cols; c++)
                for (int r = 0; r < Maze.Rows; r++)
                {
                    Assert.Equal(m.H[c, r], m.H[Maze.Cols - 1 - c, r]);
                    Assert.Equal(m.V[c, r], m.V[Maze.Cols - c, r]);
                }
            Assert.True(m.Open(0, Field.DoorRow, Dir.Left) && m.Open(Maze.Cols - 1, Field.DoorRow, Dir.Right));
            Assert.Equal(Maze.Build(d).H.Cast<bool>(), m.H.Cast<bool>());
        }
    }

    [Fact]
    public void Courtyards_are_open_and_the_hollow_has_no_walls_inside()
    {
        static int Walls(Maze m) => m.H.Cast<bool>().Count(w => w) + m.V.Cast<bool>().Count(w => w);
        Assert.Equal(MazeKind.Courtyard, Maze.KindOf(4));
        Assert.Equal(MazeKind.Hollow, Maze.KindOf(13));
        Assert.Equal(MazeKind.Dungeon, Maze.KindOf(5));
        Assert.True(Walls(Maze.Build(4)) < Walls(Maze.Build(3)));
        Assert.Equal(2 * Maze.Cols + 2 * Maze.Rows, Walls(Maze.Build(13)));
        Assert.Equal("THE COURTYARD", GameEngine.NameOf(8));
    }

    // ───────────────────────────── the warrior ─────────────────────────────

    [Fact]
    public void The_warrior_waits_safe_in_the_cage_until_it_moves_or_the_count_runs_out()
    {
        var g = Game();
        Assert.True(g.Warrior.InCage);
        g.PutBullet(Field.CageX, Field.CageY, Dir.Up);
        g.Idle(60);
        Assert.True(g.Warrior.Alive);
        Assert.True(g.Warrior.InCage);
        g.Run(40, Press(Dir.Up));
        Assert.False(g.Warrior.InCage);
        Assert.Equal(Field.CageX, g.Warrior.X);
        Assert.True(g.Warrior.Y <= Field.Rows - 1);

        var h = Game();
        h.Idle((int)(60 * GameEngine.CageSeconds) + 40);
        Assert.False(h.Warrior.InCage);
    }

    [Fact]
    public void The_warrior_walks_while_the_stick_is_held_and_stands_when_let_go()
    {
        var g = Game();
        g.PutWarrior(3, 3);
        g.Run(30, Press(Dir.Right));
        double x = g.Warrior.X;
        Assert.InRange(x, 4, 5);
        g.Idle(30);
        Assert.Equal(x, g.Warrior.X);
        Assert.Equal(Dir.Right, g.Warrior.Facing);
    }

    [Fact]
    public void Walls_stop_the_warrior_but_it_still_turns_to_face_them()
    {
        var g = Game(1);
        var (c, r) = Cells().First(s => !g.Maze.Open(s.C, s.R, Dir.Right) && g.Maze.Open(s.C, s.R, Dir.Left));
        g.PutWarrior(c, r, Dir.Left);
        g.Run(30, Press(Dir.Right));
        Assert.Equal((c, r), ((int)g.Warrior.X, (int)g.Warrior.Y));
        Assert.Equal(Dir.Right, g.Warrior.Facing);
    }

    [Fact]
    public void Holding_two_ways_turns_the_next_corner()
    {
        var g = Game();
        g.PutWarrior(2, 1);
        g.Run(10, Press(Dir.Right));
        for (int t = 0; t < 60 && g.Warrior.Y <= 1; t++) g.Run(1, new Controls(1, 1));
        Assert.Equal(3, g.Warrior.X, 3);
        Assert.True(g.Warrior.Y > 1);
    }

    [Fact]
    public void A_side_door_leads_round_to_the_other_and_shuts_behind()
    {
        var g = Game();
        g.PutWarrior(0, Field.DoorRow, Dir.Left);
        g.Run(40, Press(Dir.Left));
        Assert.InRange(g.Warrior.X, 8, Field.Cols);
        Assert.False(g.Maze.DoorsOpen);
        Assert.False(g.Maze.Open(Field.Cols - 1, Field.DoorRow, Dir.Right));
        g.Idle((int)(60 * GameEngine.DoorShutSeconds) + 5);
        Assert.True(g.Maze.DoorsOpen);
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void One_shot_at_a_time_flies_down_the_corridor_until_a_wall_stops_it()
    {
        var g = Game();
        g.PutWarrior(2, 3, Dir.Right);
        g.Run(1, new Controls(Fire: true));
        var shot = g.Shot;
        Assert.NotNull(shot);
        g.Run(5, new Controls(Fire: true));
        Assert.Same(shot, g.Shot);
        g.Idle(60);
        Assert.Null(g.Shot);
        Assert.True(shot!.X < Field.Cols);
    }

    [Fact]
    public void A_shot_down_the_corridor_slays_a_prowler_for_100()
    {
        var g = Game();
        g.PutWarrior(2, 3, Dir.Right);
        var e = g.PutEnemy(EnemyKind.Prowler, 7, 3);
        g.Run(30, new Controls(Fire: true));
        Assert.True(e.Dead);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void Shots_meeting_head_on_knock_each_other_out()
    {
        var g = Game();
        g.PutWarrior(1, 3, Dir.Right);
        var b = g.PutBullet(8, 3, Dir.Left);
        g.Run(60, new Controls(Fire: true));
        Assert.True(b.Dead);
        Assert.True(g.Warrior.Alive);
    }

    // ───────────────────────────── the monsters ─────────────────────────────

    [Fact]
    public void The_last_prowlers_leave_shades_and_each_shade_a_phantom()
    {
        var g = Game();
        g.SetShadesToCome(1);
        var a = g.PutEnemy(EnemyKind.Prowler, 1, 1);
        var b = g.PutEnemy(EnemyKind.Prowler, 9, 1);
        g.Slay(a);
        Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Shade);
        g.Slay(b);
        var shade = Assert.Single(g.Enemies, e => !e.Dead && e.Kind == EnemyKind.Shade);
        g.Slay(shade);
        Assert.Single(g.Enemies, e => !e.Dead && e.Kind == EnemyKind.Phantom);
        Assert.Equal(100 + 100 + 200, g.Score);
    }

    [Fact]
    public void Shades_cannot_be_seen_unless_close_or_straight_down_the_corridor()
    {
        var g = Game();
        g.PutWarrior(1, 1);
        var far = g.PutEnemy(EnemyKind.Shade, 8, 4);
        var lined = g.PutEnemy(EnemyKind.Phantom, 7, 1);
        var prowler = g.PutEnemy(EnemyKind.Prowler, 9, 5);
        g.Idle(1);
        Assert.False(far.Seen);
        Assert.True(lined.Seen);
        Assert.True(prowler.Seen);
    }

    [Fact]
    public void Monsters_chase_the_warrior()
    {
        var g = Real(1);
        g.Run(30, Press(Dir.Up));
        g.PutWarrior(5, 2);
        foreach (var e in g.Enemies) e.Dead = true;
        var p = g.PutEnemy(EnemyKind.Phantom, 0, 5);
        var dist = g.Maze.Distances(5, 2);
        int before = dist[0, 5];
        g.Idle(90);
        if (!p.Dead) Assert.True(dist[p.Cx, p.Cy] < before || !g.Warrior.Alive);
    }

    [Fact]
    public void Monsters_fire_down_the_corridor_when_they_face_the_warrior()
    {
        var g = Real(13);
        foreach (var e in g.Enemies) e.Dead = true;
        g.PutWarrior(2, 4, Dir.Up);
        var p = g.PutEnemy(EnemyKind.Prowler, 8, 4, Dir.Left);
        p.FireWait = 0;
        bool fired = false;
        for (int t = 0; t < 20 && !fired; t++) { g.Idle(1); fired = g.Bullets.Any(b => b.Owner == p); }
        Assert.True(fired);
    }

    [Fact]
    public void A_bullet_or_a_monster_ends_the_warrior()
    {
        var g = Game();
        g.PutWarrior(4, 4);
        g.PutBullet(4, 4, Dir.Left);
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.StartsWith("SHOT BY", g.Cause);

        var h = Game();
        h.PutWarrior(4, 4);
        h.PutEnemy(EnemyKind.Phantom, 4, 4);
        h.Idle(1);
        Assert.Equal("EATEN BY A PHANTOM", h.Cause);
    }

    [Fact]
    public void A_lost_warrior_is_followed_by_the_next_from_the_cage_and_the_last_ends_the_game()
    {
        var g = Game();
        int lives = g.Lives;
        g.PutWarrior(4, 4);
        g.PutBullet(4, 4, Dir.Left);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(lives - 1, g.Lives);
        Assert.True(g.Warrior.InCage);
        Assert.Equal(Phase.Play, g.Phase);
        g.SetLives(1);
        g.PutWarrior(4, 4);
        g.PutBullet(4, 4, Dir.Left);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_monsters_hurry_as_time_goes_on_and_their_numbers_fall()
    {
        var g = Game();
        double start = g.Hurry;
        g.Idle(600);
        Assert.True(g.Hurry > start);
        double later = g.Hurry;
        g.Slay(g.PutEnemy(EnemyKind.Prowler, 1, 1));
        Assert.True(g.Hurry > later);
    }

    // ───────────────────────────── the wisp and the wizard ─────────────────────────────

    [Fact]
    public void The_first_dungeon_ends_when_its_monsters_are_gone()
    {
        var g = Real(1);
        foreach (var e in g.Enemies) e.Dead = true;
        g.Idle(90);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Dungeon);
    }

    [Fact]
    public void From_the_second_the_wisp_comes_and_slaying_it_brings_the_wizard_and_a_double_score_dungeon()
    {
        var g = Real(2);
        foreach (var e in g.Enemies) e.Dead = true;
        g.Idle(90);
        var wisp = Assert.Single(g.Enemies, e => e.Kind == EnemyKind.Wisp);
        Assert.True(g.Maze.DoorsOpen);
        g.Slay(wisp);
        Assert.Equal(1000, g.Score);
        g.Idle(90);
        var wizard = Assert.Single(g.Enemies, e => e.Kind == EnemyKind.Wizard);
        g.Slay(wizard);
        Assert.Equal(3500, g.Score);
        g.Idle(90);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(3, g.Dungeon);
        Assert.Equal(2, g.Multiplier);
        Assert.Equal("DOUBLE SCORE DUNGEON", g.SubMessage);
    }

    [Fact]
    public void The_wisp_makes_for_a_door_and_escapes_through_it()
    {
        var g = Game(2);
        g.PutWarrior(5, 5);
        var wisp = g.PutEnemy(EnemyKind.Wisp, 2, Field.DoorRow);
        g.Idle(120);
        Assert.True(wisp.Dead);
        Assert.Equal("THE WISP ESCAPED", g.Message);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void The_wizard_vanishes_and_reappears_and_in_the_end_is_gone()
    {
        var g = Game();
        g.PutWarrior(5, 2);
        var w = g.PutEnemy(EnemyKind.Wizard, 0, 0);
        w.HopIn = 0.5;
        g.Idle(40);
        Assert.NotEqual((0.0, 0.0), (w.X, w.Y));
        g.Idle((int)(60 * GameEngine.WizardSeconds));
        Assert.True(w.Dead);
        Assert.Equal("THE WIZARD VANISHES", g.Message);
    }

    [Fact]
    public void A_double_score_dungeon_doubles_the_points()
    {
        var g = Real(2);
        foreach (var e in g.Enemies) e.Dead = true;
        g.Idle(90);
        g.Slay(g.Enemies.Single(e => e.Kind == EnemyKind.Wisp));
        g.Idle(90);
        g.Enemies.Single(e => e.Kind == EnemyKind.Wizard).Dead = true;
        g.Idle(90 + (int)(60 * GameEngine.ClearSeconds) + 5);
        int before = g.Score;
        g.Slay(g.Enemies.First(e => e.Kind == EnemyKind.Prowler));
        Assert.Equal(before + 200, g.Score);
    }

    [Fact]
    public void Extra_warriors_come_at_12000_and_every_30000_after()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(11950);
        g.Slay(g.PutEnemy(EnemyKind.Prowler, 1, 1));
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_computer_warrior_clears_the_first_dungeons()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 150 && g.State == GameState.Playing && g.Dungeon < 3; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Dungeon >= 3, $"dungeon {g.Dungeon}");
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 25);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 25)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, double, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, g.Warrior.X, g.Warrior.Y, g.Dungeon);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        g.PutWarrior(4, 4);
        g.PutBullet(4, 4, Dir.Left);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "WOR") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("WOR", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
