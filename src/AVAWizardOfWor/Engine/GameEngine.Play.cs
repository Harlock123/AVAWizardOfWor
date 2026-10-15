namespace AVAWizardOfWor.Engine;

public sealed partial class GameEngine
{
    public const double WarriorSpeed = 3.2, ShotSpeed = 14, BulletSpeed = 7, WizardBoltSpeed = 9;
    public const double StartSeconds = 2.6, DyingSeconds = 2.2, ClearSeconds = 2.6, CageSeconds = 8, DoorShutSeconds = 5;
    public const double SpawnSeconds = 0.9, WizardSeconds = 18;
    public const int StartProwlers = 6;
    public const int FirstExtraLife = 12000, ExtraLifeEvery = 30000;

    public int Dungeon { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Maze Maze { get; private set; } = Maze.Build(1);
    public Warrior Warrior { get; } = new();
    public List<Enemy> Enemies { get; } = [];
    /// <summary>The warrior's shot: only one at a time.</summary>
    public Bolt? Shot { get; private set; }
    public List<Bolt> Bullets { get; } = [];
    public WarriorPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double DungeonTime { get; private set; }
    public int Kills { get; private set; }
    /// <summary>Points count double in the dungeon after the wisp is slain.</summary>
    public int Multiplier { get; private set; } = 1;
    /// <summary>How long the side doors stay shut after someone goes through.</summary>
    public double DoorShut { get; private set; }
    public bool WispSlain { get; private set; }
    /// <summary>Where the dungeon has got to: its prowlers, shades and phantoms; the wisp; the wizard; done.</summary>
    public int Step { get; private set; }
    /// <summary>For tests: nobody spawns, so a test can set up just what it wants.</summary>
    internal bool Calm { get; set; }

    private bool _doubleNext;
    private int _shadesToCome, _nextExtra;
    private double _beatIn, _quiet;
    private bool _beatHigh;
    private int[,] _toWarrior = new int[Field.Cols, Field.Rows];

    public double Pace => Math.Min(1.5, 1 + 0.04 * (Dungeon - 1));
    /// <summary>The longer a dungeon goes on, and the more that fall, the faster the rest move (and the heart beats).</summary>
    public double Hurry => Math.Min(1.8, 1 + 0.012 * DungeonTime + 0.04 * Kills);
    /// <summary>How many shades come once the prowlers are thinned out.</summary>
    public int Shades => Math.Min(4, 1 + (Dungeon - 1) / 2);

    private static readonly string[] Taunts =
        ["WELCOME TO THE DUNGEON", "THE WIZARD IS WATCHING", "NONE LEAVE ALIVE", "THE DARK HAS TEETH", "GO FORTH, WARRIOR", "YOUR BONES WILL STAY HERE"];

    public static string NameOf(int dungeon) => Maze.KindOf(dungeon) switch
    {
        MazeKind.Courtyard => "THE COURTYARD",
        MazeKind.Hollow => "THE HOLLOW",
        _ => "",
    };

    public static int Points(EnemyKind k) => k switch
    {
        EnemyKind.Prowler => 100, EnemyKind.Shade => 200, EnemyKind.Phantom => 500, EnemyKind.Wisp => 1000, _ => 2500,
    };

    public static string NameOf(EnemyKind k) => k switch
    {
        EnemyKind.Prowler => "PROWLER", EnemyKind.Shade => "SHADE", EnemyKind.Phantom => "PHANTOM", EnemyKind.Wisp => "WISP", _ => "WIZARD",
    };

    public static uint Colour(EnemyKind k) => k switch
    {
        EnemyKind.Prowler => 0xFF4A8CFF, EnemyKind.Shade => 0xFFFFD23A, EnemyKind.Phantom => 0xFFFF4A4A, EnemyKind.Wisp => 0xFF6AF0B0, _ => 0xFFD070FF,
    };

    private double Speed(EnemyKind k) => k switch
    {
        EnemyKind.Prowler => Math.Min(3.4, 1.6 * Pace * Hurry),
        EnemyKind.Shade => Math.Min(3.5, 2.2 * Pace * Hurry),
        EnemyKind.Phantom => Math.Min(3.6, 2.9 * Pace * Hurry),
        EnemyKind.Wisp => Math.Min(4.2, 3.4 * Pace),
        _ => 0,
    };

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Dungeon = 1;
        _doubleNext = false;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginDungeon(int dungeon)
    {
        Dungeon = dungeon;
        Multiplier = _doubleNext ? 2 : 1;
        _doubleNext = false;
        LayOut();
        SetPhase(Phase.Start);
        string name = NameOf(dungeon);
        Say($"DUNGEON {dungeon}", Multiplier == 2 ? "DOUBLE SCORE DUNGEON" : name != "" ? name : Taunts[(dungeon - 1) % Taunts.Length], StartSeconds);
        Play(Sfx.DungeonStart);
    }

    private void LayOut()
    {
        Maze = Maze.Build(Dungeon);
        Enemies.Clear();
        Bullets.Clear();
        Effects.Clear();
        Shot = null;
        DungeonTime = 0;
        Kills = 0;
        DoorShut = 0;
        Step = 0;
        WispSlain = false;
        _quiet = 0;
        _beatIn = 0.5;
        _shadesToCome = Shades;
        PutInCage();
        if (Calm) return;
        for (int k = 0; k < StartProwlers; k++) Spawn(EnemyKind.Prowler);
    }

    private void PutInCage()
    {
        var w = Warrior;
        (w.X, w.Y) = (Field.CageX, Field.CageY);
        w.Alive = true;
        w.InCage = true;
        w.Leaving = false;
        w.CageTime = 0;
        w.Dir = Dir.None;
        w.Facing = Dir.Up;
    }

    /// <summary>The cell the warrior is in (or the one it's about to come into from the cage or a doorway).</summary>
    private (int C, int R) WarriorCell()
    {
        var w = Warrior;
        if (w.InCage) return (Field.CageX, Field.Rows - 1);
        return (Math.Clamp(w.Cx, 0, Field.Cols - 1), Math.Clamp(w.Cy, 0, Field.Rows - 1));
    }

    /// <summary>A newcomer, somewhere well away from the warrior.</summary>
    private Enemy Spawn(EnemyKind kind)
    {
        var (wc, wr) = WarriorCell();
        var dist = Maze.Distances(wc, wr);
        var cells = new List<(int C, int R)>();
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
                if (dist[c, r] >= 4 && !Enemies.Any(e => !e.Dead && Math.Abs(e.X - c) + Math.Abs(e.Y - r) < 1.5)) cells.Add((c, r));
        if (cells.Count == 0)
            for (int c = 0; c < Field.Cols; c++)
                for (int r = 0; r < Field.Rows; r++)
                    if (dist[c, r] >= 2) cells.Add((c, r));
        var (x, y) = cells.Count > 0 ? cells[_rng.Next(cells.Count)] : (0, 0);
        var e = new Enemy
        {
            Kind = kind, X = x, Y = y, Facing = Dirs.All[_rng.Next(4)], FireWait = 1.5 + _rng.NextDouble() * 2,
            ShowFor = SpawnSeconds + 0.6, HopIn = 1.2,
        };
        Enemies.Add(e);
        return e;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                DungeonTime += dt;
                Doors(dt);
                MoveWarrior(c, dt);
                Fire(c);
                var (wc, wr) = WarriorCell();
                _toWarrior = Maze.Distances(wc, wr);
                MoveEnemies(dt);
                MoveBolts(dt);
                Hits();
                if (Phase == Phase.Play) Progress(dt);
                Beat(dt);
                break;
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    PutInCage();
                    Bullets.Clear();
                    Shot = null;
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Clear:
                if (PhaseTime > ClearSeconds) BeginDungeon(Dungeon + 1);
                break;
        }
        Enemies.RemoveAll(e => e.Dead);
        Bullets.RemoveAll(b => b.Dead);
        if (Shot is { Dead: true }) Shot = null;
    }

    private void Doors(double dt)
    {
        // The wisp keeps them open: it's making for them.
        if (Enemies.Any(e => e.Kind == EnemyKind.Wisp && !e.Dead)) { Maze.DoorsOpen = true; DoorShut = 0; return; }
        if (DoorShut > 0 && (DoorShut -= dt) <= 0) Maze.DoorsOpen = true;
    }

    /// <summary>
    /// Moves something along the corridors by a distance, from centre to centre, asking at each centre which
    /// way next (None: stop there). Through a side door it comes out of the other.
    /// </summary>
    private void Advance(Mover m, double dist, Func<Mover, Dir> choose)
    {
        for (int guard = 0; dist > 1e-9 && guard < 8; guard++)
        {
            if (m.Dir == Dir.None)
            {
                if (!m.AtCentre) return;
                var first = choose(m);
                if (first == Dir.None) return;
                m.Dir = first;
            }
            var (vx, vy) = Dirs.Vec(m.Dir);
            double pos = vx != 0 ? m.X : m.Y;
            int s = vx + vy;
            double next = s > 0 ? Math.Floor(pos + 1e-9) + 1 : Math.Ceiling(pos - 1e-9) - 1;
            double gap = Math.Abs(next - pos);
            // Never stop a hair short of a centre: it would be passed by next time without a look round.
            if (dist < gap - 1e-6)
            {
                if (vx != 0) m.X += s * dist; else m.Y += s * dist;
                return;
            }
            if (vx != 0) m.X = next; else m.Y = next;
            dist -= gap;
            if (Math.Round(m.Y) == Field.DoorRow && (m.X <= -1 || m.X >= Field.Cols))
            {
                if (!Through(m)) return;
                m.X = m.X <= -1 ? Field.Cols : -1;
            }
            var nd = choose(m);
            m.Dir = nd;
            if (nd == Dir.None) return;
        }
    }

    /// <summary>Someone has gone out of a side door: the doors shut behind them for a while, and the wisp is away.</summary>
    private bool Through(Mover m)
    {
        if (m is Enemy { Kind: EnemyKind.Wisp } wisp)
        {
            wisp.Dead = true;
            Say("THE WISP ESCAPED", null, 2);
            Play(Sfx.WispEscape);
            return false;
        }
        Maze.DoorsOpen = false;
        DoorShut = DoorShutSeconds;
        Play(Sfx.Door);
        return true;
    }

    // ───────────────────────────── the warrior ─────────────────────────────

    private void MoveWarrior(Controls c, double dt)
    {
        var w = Warrior;
        if (!w.Alive) return;
        if (w.InCage)
        {
            w.CageTime += dt;
            if (!w.Leaving && (c.Dx != 0 || c.Dy != 0 || w.CageTime > CageSeconds)) w.Leaving = true;
            if (!w.Leaving) return;
            w.Facing = Dir.Up;
            w.Y -= WarriorSpeed * dt;
            if (w.Y <= Field.Rows - 1)
            {
                w.Y = Field.Rows - 1;
                w.InCage = w.Leaving = false;
                w.Dir = Dir.None;
            }
            return;
        }
        Dir a = Dirs.Of(c.Dx, 0), b = Dirs.Of(0, c.Dy);
        if (a == Dir.None && b == Dir.None)
        {
            // Let go, and it stands where it is.
            w.Dir = Dir.None;
            return;
        }
        // With two ways held, the one that turns a corner comes first.
        bool movingAcross = w.Dir != Dir.None ? Dirs.Horizontal(w.Dir) : Math.Abs(w.X - Math.Round(w.X)) > 1e-6 || Dirs.Horizontal(w.Facing);
        var wants = (movingAcross ? new[] { b, a } : new[] { a, b }).Where(d => d != Dir.None).ToArray();
        if (!w.AtCentre)
        {
            bool onRow = Math.Abs(w.X - Math.Round(w.X)) > 1e-6;
            var along = wants.FirstOrDefault(d => Dirs.Horizontal(d) == onRow);
            if (along != Dir.None) w.Dir = along;
            else if (w.Dir == Dir.None)
            {
                // A turn wanted halfway along: on to the nearer centre first.
                double frac = onRow ? w.X - Math.Floor(w.X) : w.Y - Math.Floor(w.Y);
                w.Dir = onRow ? (frac < 0.5 ? Dir.Left : Dir.Right) : (frac < 0.5 ? Dir.Up : Dir.Down);
            }
        }
        else w.Dir = Dir.None;
        Advance(w, WarriorSpeed * dt, m =>
        {
            // In a doorway, it can only go on through or back.
            if (m.Outside) return m.Dir != Dir.None ? m.Dir : wants.FirstOrDefault(Dirs.Horizontal);
            return wants.FirstOrDefault(d => Maze.Open(m.Cx, m.Cy, d));
        });
        w.Facing = w.Dir != Dir.None ? w.Dir : wants[0];
    }

    private void Fire(Controls c)
    {
        var w = Warrior;
        if (!c.Fire || Shot != null || !w.Alive || w.InCage || w.Outside) return;
        Shot = new Bolt { X = w.X, Y = w.Y, Dir = w.Facing, Speed = ShotSpeed };
        Play(Sfx.Fire);
    }

    // ───────────────────────────── the monsters ─────────────────────────────

    private void MoveEnemies(double dt)
    {
        var w = Warrior;
        foreach (var e in Enemies)
        {
            if (e.Dead) continue;
            e.Age += dt;
            e.ShowFor -= dt;
            if (e.Kind == EnemyKind.Wizard) { MoveWizard(e, dt); continue; }
            if (e.Age < SpawnSeconds) continue;
            Advance(e, Speed(e.Kind) * dt, m => Choose((Enemy)m));
            if (e.Dead) continue;
            if (e.Dir != Dir.None) e.Facing = e.Dir;
            e.FireWait -= dt;
            if (e.Kind != EnemyKind.Wisp && e.FireWait <= 0 && CanShoot(e))
            {
                Shoot(e);
                e.FireWait = (1.2 + _rng.NextDouble() * 2.2) / Pace;
            }
            // The shades and phantoms can't be seen unless close by, or straight down the corridor.
            e.Seen = e.Kind is EnemyKind.Prowler or EnemyKind.Wisp || e.ShowFor > 0
                     || Math.Abs(e.X - w.X) + Math.Abs(e.Y - w.Y) < 1.6 || InLine(e);
        }
    }

    private bool InLine(Enemy e)
    {
        var w = Warrior;
        if (!w.Alive || w.InCage || e.Outside || w.Outside) return false;
        return Maze.Clear(e.Cx, e.Cy, w.Cx, w.Cy);
    }

    /// <summary>Which way a monster goes from a centre: mostly towards the warrior (the faster, the surer), never back unless it must.</summary>
    private Dir Choose(Enemy e)
    {
        if (e.Outside) return e.Dir;
        int c = e.Cx, r = e.Cy;
        var ways = Dirs.All.Where(d => Maze.Open(c, r, d) && d != Dirs.Opposite(e.Dir)).ToList();
        if (ways.Count == 0) ways = Dirs.All.Where(d => Maze.Open(c, r, d)).ToList();
        if (ways.Count == 0) return Dir.None;
        if (e.Kind == EnemyKind.Wisp)
        {
            // Making for the nearer door, and out.
            if (r == Field.DoorRow && c == 0 && Maze.Open(c, r, Dir.Left)) return Dir.Left;
            if (r == Field.DoorRow && c == Field.Cols - 1 && Maze.Open(c, r, Dir.Right)) return Dir.Right;
            var left = Maze.Distances(0, Field.DoorRow);
            var right = Maze.Distances(Field.Cols - 1, Field.DoorRow);
            return ways.OrderBy(d => { var (nc, nr) = Maze.Next(c, r, d); return Math.Min(left[nc, nr], right[nc, nr]); }).ThenBy(_ => _rng.Next()).First();
        }
        double chase = Warrior.InCage ? 0.15 : Math.Min(0.95, e.Kind switch { EnemyKind.Prowler => 0.5, EnemyKind.Shade => 0.68, _ => 0.82 } + 0.02 * Dungeon);
        if (_rng.NextDouble() < chase)
        {
            int best = ways.Min(d => Far(d));
            var near = ways.Where(d => Far(d) == best).ToList();
            return near[_rng.Next(near.Count)];
        }
        return ways[_rng.Next(ways.Count)];

        int Far(Dir d)
        {
            var (nc, nr) = Maze.Next(c, r, d);
            int v = _toWarrior[nc, nr];
            return v < 0 ? 999 : v;
        }
    }

    /// <summary>The wizard doesn't walk: it vanishes and appears again close by, and fires as soon as it sees the warrior.</summary>
    private void MoveWizard(Enemy e, double dt)
    {
        if (e.Age > WizardSeconds)
        {
            e.Dead = true;
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = Field.Px(e.X, e.Y).X, Y = Field.Px(e.X, e.Y).Y, Colour = Colour(e.Kind), Size = 1.2 });
            Say("THE WIZARD VANISHES", "UNTIL NEXT TIME", 2);
            Play(Sfx.WizardKill);
            return;
        }
        e.HopIn -= dt;
        if (e.HopIn <= 0)
        {
            var cells = new List<(int C, int R)>();
            var lined = new List<(int C, int R)>();
            var (wc, wr) = WarriorCell();
            for (int c = 0; c < Field.Cols; c++)
                for (int r = 0; r < Field.Rows; r++)
                {
                    int d = _toWarrior[c, r];
                    if (d < 2 || d > 5) continue;
                    cells.Add((c, r));
                    if (Maze.Clear(c, r, wc, wr)) lined.Add((c, r));
                }
            var pick = lined.Count > 0 && _rng.NextDouble() < 0.6 ? lined : cells;
            if (pick.Count > 0) (e.X, e.Y) = pick[_rng.Next(pick.Count)];
            e.Dir = Dir.None;
            e.HopIn = (1.6 + _rng.NextDouble() * 0.8) / Pace;
            e.FireWait = 0.4;
            e.ShowFor = 0.3;
            Play(Sfx.Hop);
        }
        var w = Warrior;
        if (w.Alive && !w.InCage && (e.Cx == w.Cx || e.Cy == w.Cy))
            e.Facing = e.Cx == w.Cx ? (w.Y > e.Y ? Dir.Down : Dir.Up) : (w.X > e.X ? Dir.Right : Dir.Left);
        e.FireWait -= dt;
        if (e.FireWait <= 0 && CanShoot(e))
        {
            Shoot(e);
            e.FireWait = 99;
        }
    }

    private bool CanShoot(Enemy e)
    {
        var w = Warrior;
        if (Calm || !w.Alive || w.InCage || w.Outside || e.Outside) return false;
        if (Bullets.Any(b => b.Owner == e) || Bullets.Count >= Math.Min(4, 2 + Dungeon / 3)) return false;
        if (!InLine(e)) return false;
        var toward = e.Cx == w.Cx ? (w.Y > e.Y ? Dir.Down : Dir.Up) : (w.X > e.X ? Dir.Right : Dir.Left);
        // On the line itself, not between two rows.
        double off = Dirs.Horizontal(toward) ? Math.Abs(e.Y - Math.Round(e.Y)) : Math.Abs(e.X - Math.Round(e.X));
        return (e.Kind == EnemyKind.Wizard || e.Facing == toward) && off < 0.15 && Math.Abs(e.X - w.X) + Math.Abs(e.Y - w.Y) <= 7;
    }

    private void Shoot(Enemy e)
    {
        var w = Warrior;
        var toward = e.Cx == w.Cx ? (w.Y > e.Y ? Dir.Down : Dir.Up) : (w.X > e.X ? Dir.Right : Dir.Left);
        bool wizard = e.Kind == EnemyKind.Wizard;
        double x = Dirs.Horizontal(toward) ? e.X : Math.Round(e.X), y = Dirs.Horizontal(toward) ? Math.Round(e.Y) : e.Y;
        Bullets.Add(new Bolt { X = x, Y = y, Dir = toward, Speed = (wizard ? WizardBoltSpeed : BulletSpeed) * Pace, Owner = e, FromWizard = wizard });
        if (!wizard) e.ShowFor = Math.Max(e.ShowFor, 0.6);
        Play(wizard ? Sfx.WizardFire : Sfx.EnemyFire);
    }

    // ───────────────────────────── shots ─────────────────────────────

    private void MoveBolts(double dt)
    {
        if (Shot != null) Fly(Shot, dt);
        foreach (var b in Bullets) Fly(b, dt);
    }

    /// <summary>Straight on down the corridor; a wall stops it.</summary>
    private void Fly(Bolt b, double dt)
    {
        var (vx, vy) = Dirs.Vec(b.Dir);
        int oc = (int)Math.Round(b.X), or = (int)Math.Round(b.Y);
        b.X += vx * b.Speed * dt;
        b.Y += vy * b.Speed * dt;
        int nc = (int)Math.Round(b.X), nr = (int)Math.Round(b.Y);
        if ((oc, or) != (nc, nr) && (!Field.Inside(oc, or) || !Field.Inside(nc, nr) || !Maze.Open(oc, or, b.Dir))) b.Dead = true;
    }

    private static double Gap(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));

    private void Hits()
    {
        if (Shot is { Dead: false } s)
        {
            foreach (var e in Enemies)
            {
                if (e.Dead || (e.Kind != EnemyKind.Wizard && e.Age < SpawnSeconds * 0.5) || Gap(s.X, s.Y, e.X, e.Y) > 0.5) continue;
                s.Dead = true;
                Down(e);
                break;
            }
            // Shots meeting head on knock each other out.
            if (!s.Dead)
                foreach (var b in Bullets)
                {
                    if (b.Dead || Gap(s.X, s.Y, b.X, b.Y) > 0.45) continue;
                    s.Dead = b.Dead = true;
                    var (px, py) = Field.Px(b.X, b.Y);
                    Effects.Add(new Effect { Kind = EffectKind.Burst, X = px, Y = py, Colour = 0xFFFFFFFF, Size = 0.4, Life = 0.4 });
                    Play(Sfx.Clash);
                    break;
                }
        }
        var w = Warrior;
        if (!w.Alive || w.InCage) return;
        foreach (var b in Bullets)
            if (!b.Dead && Gap(b.X, b.Y, w.X, w.Y) < 0.4)
            {
                b.Dead = true;
                Lose(b.FromWizard ? "BLASTED BY THE WIZARD" : $"SHOT BY A {NameOf(b.Owner?.Kind ?? EnemyKind.Prowler)}");
                return;
            }
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Age < SpawnSeconds || Gap(e.X, e.Y, w.X, w.Y) > 0.65) continue;
            Lose(e.Kind == EnemyKind.Wizard ? "CAUGHT BY THE WIZARD" : $"EATEN BY A {NameOf(e.Kind)}");
            return;
        }
    }

    private void Down(Enemy e)
    {
        e.Dead = true;
        Kills++;
        var (x, y) = Field.Px(e.X, e.Y);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = Colour(e.Kind), Size = e.Kind is EnemyKind.Wizard or EnemyKind.Wisp ? 1.6 : 1 });
        AddScore(Points(e.Kind) * Multiplier, x, y, Colour(e.Kind));
        switch (e.Kind)
        {
            case EnemyKind.Prowler:
                Play(Sfx.Kill);
                // The last few prowlers each leave a shade behind them.
                if (Enemies.Count(o => !o.Dead && o.Kind == EnemyKind.Prowler) < _shadesToCome)
                {
                    _shadesToCome--;
                    Spawn(EnemyKind.Shade);
                    Play(Sfx.Appear);
                }
                break;
            case EnemyKind.Shade:
                Play(Sfx.Kill);
                Spawn(EnemyKind.Phantom);
                Play(Sfx.Appear);
                break;
            case EnemyKind.Phantom:
                Play(Sfx.Kill);
                break;
            case EnemyKind.Wisp:
                WispSlain = true;
                _doubleNext = true;
                Say("THE WISP IS SLAIN", "DOUBLE SCORE IN THE NEXT DUNGEON", 2);
                Play(Sfx.WispKill);
                break;
            case EnemyKind.Wizard:
                Say("THE WIZARD IS DEFEATED!", null, 2.4);
                Play(Sfx.WizardKill);
                break;
        }
    }

    /// <summary>Once everything in the dungeon is gone: the wisp (from the second), then if it fell, the wizard; then out.</summary>
    private void Progress(double dt)
    {
        if (Calm || Enemies.Any(e => !e.Dead)) { _quiet = 0; return; }
        if ((_quiet += dt) < 1.0) return;
        _quiet = 0;
        switch (Step)
        {
            case 0 when Dungeon >= 2:
                Step = 1;
                Spawn(EnemyKind.Wisp);
                Maze.DoorsOpen = true;
                DoorShut = 0;
                Say("THE WISP!", "CATCH IT BEFORE IT FLIES", 2);
                Play(Sfx.Wisp);
                return;
            case 1 when WispSlain:
                Step = 2;
                Spawn(EnemyKind.Wizard);
                Say("THE WIZARD!", "HE HAS COME FOR YOU", 2);
                Play(Sfx.Wizard);
                return;
        }
        Step = 3;
        DungeonClear();
    }

    private void DungeonClear()
    {
        SetPhase(Phase.Clear);
        Say("DUNGEON CLEARED", _doubleNext ? "NEXT: DOUBLE SCORE DUNGEON" : null, ClearSeconds);
        Play(Sfx.Clear);
        Music(false);
        Bullets.Clear();
        Shot = null;
    }

    private void Beat(double dt)
    {
        if ((_beatIn -= dt) > 0) return;
        _beatIn = 0.55 / Hurry;
        Play(_beatHigh ? Sfx.Beat2 : Sfx.Beat1);
        _beatHigh = !_beatHigh;
    }

    private void Lose(string cause)
    {
        Cause = cause;
        Lives--;
        var w = Warrior;
        w.Alive = false;
        var (x, y) = Field.Px(w.X, w.Y);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = 0xFFFFE14A, Size = 1.6, Life = 1.2 });
        Shot = null;
        Play(Sfx.Death);
        Music(false);
        SetPhase(Phase.Dying);
        Say(cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "WARRIOR" : "WARRIORS")} LEFT" : null, DyingSeconds);
    }

    private void AddScore(int points, double x, double y, uint colour = 0xFFF2F2F2)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Colour = colour });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a dungeon.</summary>
    internal void Go(int dungeon = 1)
    {
        StartGame();
        BeginDungeon(dungeon);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>The warrior out of its cage, standing at a cell centre.</summary>
    internal void PutWarrior(int c, int r, Dir facing = Dir.Up)
    {
        var w = Warrior;
        (w.X, w.Y) = (c, r);
        w.InCage = w.Leaving = false;
        w.Alive = true;
        w.Dir = Dir.None;
        w.Facing = facing;
    }

    internal Enemy PutEnemy(EnemyKind kind, int c, int r, Dir dir = Dir.None)
    {
        var e = new Enemy { Kind = kind, X = c, Y = r, Dir = dir, Facing = dir == Dir.None ? Dir.Left : dir, Age = SpawnSeconds, FireWait = 99, HopIn = 99 };
        Enemies.Add(e);
        return e;
    }

    internal Bolt PutBullet(double x, double y, Dir dir, Enemy? owner = null)
    {
        var b = new Bolt { X = x, Y = y, Dir = dir, Speed = BulletSpeed, Owner = owner };
        Bullets.Add(b);
        return b;
    }

    /// <summary>As if shot.</summary>
    internal void Slay(Enemy e) => Down(e);
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetStep(int step) => Step = step;
    internal void SetShadesToCome(int n) => _shadesToCome = n;
}
