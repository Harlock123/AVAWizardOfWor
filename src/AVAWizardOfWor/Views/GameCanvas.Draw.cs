using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAWizardOfWor.Engine;
using Effect = AVAWizardOfWor.Engine.Effect;

namespace AVAWizardOfWor.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAWizardOfWor/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Wall = 0xFF2E6CFF, Gold = 0xFFFFC83A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>
    /// A figure drawn side-on facing right, given as (forward, down) points, placed facing the way it goes:
    /// mirrored to face left, turned to face up or down.
    /// </summary>
    private static Point[] Place(Point at, Dir facing, double scale, params (double F, double S)[] pts) =>
        pts.Select(p =>
        {
            var (x, y) = facing switch
            {
                Dir.Left => (-p.F, p.S),
                Dir.Down => (-p.S, p.F),
                Dir.Up => (p.S, -p.F),
                _ => (p.F, p.S),
            };
            return new Point(at.X + x * scale, at.Y + y * scale);
        }).ToArray();

    private static Point Px(double x, double y) { var (px, py) = Field.Px(x, y); return new Point(px, py); }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            DrawMaze(dc, g.Maze);
            DrawCage(dc);
            DrawRadar(dc);
            foreach (var e in g.Enemies.Where(e => !e.Dead)) DrawEnemy(dc, e);
            if (g.Shot is { } s) DrawBolt(dc, s, true);
            foreach (var b in g.Bullets) DrawBolt(dc, b, false);
            var w = g.Warrior;
            if (w.Alive) DrawWarrior(dc, Px(w.X, w.Y), w.Facing, w.Side, 1, w.X + w.Y);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 316), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 16), 26, Yellow, center: true);
    }

    // ───────────────────────────── the dungeon ─────────────────────────────

    private void DrawMaze(DrawingContext dc, Maze m)
    {
        var lines = new List<(Point A, Point B)>();
        double x0 = Field.X0, y0 = Field.Y0, cell = Field.Cell;
        for (int c = 0; c < Maze.Cols; c++)
            for (int r = 0; r <= Maze.Rows; r++)
                if (m.H[c, r] && !(r == Maze.Rows && c == Field.CageX))
                    lines.Add((new Point(x0 + c * cell, y0 + r * cell), new Point(x0 + (c + 1) * cell, y0 + r * cell)));
        for (int c = 0; c <= Maze.Cols; c++)
            for (int r = 0; r < Maze.Rows; r++)
                if (m.V[c, r] && !(r == Field.DoorRow && (c == 0 || c == Maze.Cols)))
                    lines.Add((new Point(x0 + c * cell, y0 + r * cell), new Point(x0 + c * cell, y0 + (r + 1) * cell)));
        uint colour = m.Kind switch { MazeKind.Courtyard => 0xFF2EC86C, MazeKind.Hollow => 0xFFE04AC8, _ => Wall };
        // A soft glow under a bright double line.
        foreach (var (a, b) in lines) dc.DrawLine(Pen(Alpha(colour, 0.22), 12), a, b);
        foreach (var (a, b) in lines) dc.DrawLine(Pen(colour, 5), a, b);
        foreach (var (a, b) in lines) dc.DrawLine(Pen(0xFF000000, 1.6), a, b);
        // The side doors: open, a dark gap; shut, glowing bars.
        foreach (double x in new[] { x0, x0 + Maze.Cols * cell })
        {
            double top = y0 + Field.DoorRow * cell, bottom = top + cell;
            if (m.DoorsOpen)
            {
                dc.DrawLine(Pen(Alpha(colour, 0.5), 3), new Point(x - 10, top), new Point(x + 10, top));
                dc.DrawLine(Pen(Alpha(colour, 0.5), 3), new Point(x - 10, bottom), new Point(x + 10, bottom));
            }
            else
            {
                dc.DrawLine(Pen(Alpha(Red, 0.3), 12), new Point(x, top), new Point(x, bottom));
                for (int k = 0; k < 5; k++)
                    dc.DrawLine(Pen(Red, 3), new Point(x - 6, top + 8 + k * 15), new Point(x + 6, top + 8 + k * 15));
                dc.DrawLine(Pen(Red, 3), new Point(x, top), new Point(x, bottom));
            }
        }
    }

    /// <summary>The warrior's cage under the bottom right corner, and the warriors still waiting beside it.</summary>
    private void DrawCage(DrawingContext dc)
    {
        var g = Engine;
        double cell = Field.Cell, left = Field.X0 + Field.CageX * cell, top = Field.Y0 + Field.Rows * cell;
        uint colour = Gold;
        dc.DrawLine(Pen(colour, 4), new Point(left, top), new Point(left, top + cell * 0.9));
        dc.DrawLine(Pen(colour, 4), new Point(left + cell, top), new Point(left + cell, top + cell * 0.9));
        dc.DrawLine(Pen(colour, 4), new Point(left, top + cell * 0.9), new Point(left + cell, top + cell * 0.9));
        // The gate along the top: shut once the warrior is out.
        if (!g.Warrior.InCage || !g.Warrior.Alive)
            for (int k = 0; k < 6; k++) dc.DrawLine(Pen(Alpha(colour, 0.8), 2), new Point(left + 6 + k * 13, top - 2), new Point(left + 6 + k * 13, top + 10));
        if (g.Warrior is { InCage: true, Alive: true, Leaving: false } && g.Phase == Phase.Play)
        {
            double left2 = Math.Max(0, GameEngine.CageSeconds - g.Warrior.CageTime);
            Text(dc, left2.ToString("0"), new Point(left - 22, top + cell * 0.45), 30, Yellow, center: true);
        }
        // The warriors still to come, bottom left.
        int spare = Math.Min(7, g.Lives - (g.Warrior.Alive ? 1 : 0));
        Text(dc, "WARRIORS", new Point(Field.X0, top + 30), 22, Dim);
        for (int k = 0; k < spare; k++)
            DrawWarrior(dc, new Point(Field.X0 + 16 + k * 36, top + 80), Dir.Right, Dir.Right, 0.7, 0);
    }

    private void DrawRadar(DrawingContext dc)
    {
        var g = Engine;
        const double cell = 14;
        double w = Field.Cols * cell, h = Field.Rows * cell;
        double x0 = Field.Width / 2 - w / 2, y0 = Field.Y0 + Field.Rows * Field.Cell + 26;
        dc.FillRectangle(Brush(0xFF060A1A), new Rect(x0 - 4, y0 - 4, w + 8, h + 8));
        dc.DrawRectangle(null, Pen(Alpha(Wall, 0.8), 2), new Rect(x0 - 4, y0 - 4, w + 8, h + 8));
        Text(dc, "RADAR", new Point(Field.Width / 2, y0 + h + 18), 20, Dim, center: true);
        foreach (var e in g.Enemies.Where(e => !e.Dead && !e.Outside))
            dc.FillRectangle(Brush(GameEngine.Colour(e.Kind)), new Rect(x0 + (e.X + 0.5) * cell - 4, y0 + (e.Y + 0.5) * cell - 4, 8, 8));
        var me = g.Warrior;
        if (me.Alive && !me.InCage)
            dc.DrawRectangle(null, Pen(Yellow, 1.5), new Rect(x0 + (me.X + 0.5) * cell - 4, y0 + (me.Y + 0.5) * cell - 4, 8, 8));
        if (g.Multiplier > 1) Text(dc, "DOUBLE SCORE", new Point(x0 + w + 30, y0 + 22), 26, Yellow);
    }

    // ───────────────────────────── the warrior and the monsters ─────────────────────────────

    /// <summary>The warrior, side-on: helmet, visor, armour and blaster (pointing the way it faces). Its legs move with the distance walked.</summary>
    private void DrawWarrior(DrawingContext dc, Point at, Dir facing, Dir side, double scale, double walked)
    {
        double k = 1.15 * scale;
        double stride = Math.Sin(walked * Math.PI * 2) * 5;
        var P = (double f, double s) => Place(at, side, k, (f, s))[0];
        // Legs.
        dc.DrawLine(Pen(0xFF8A6A1A, 5 * k), P(-3, 6), P(-3 + stride, 16));
        dc.DrawLine(Pen(0xFF8A6A1A, 5 * k), P(3, 6), P(3 - stride, 16));
        // Body and helmet.
        Poly(dc, Gold, 0xFF5A3A00, 1.2, Place(at, side, k, (-8, -6), (7, -6), (8, 8), (-8, 8)));
        dc.DrawEllipse(Brush(0xFFFFE9A0), Pen(0xFF5A3A00, 1.2), P(0, -12), 8 * k, 8 * k);
        Poly(dc, 0xFF2A9AFF, null, 0, Place(at, side, k, (1, -15), (8, -14), (8, -9), (1, -10)));
        Poly(dc, 0xFFFF6A3A, null, 0, Place(at, side, k, (-6, -20), (-1, -24), (2, -19)));
        // The blaster: ahead, or held up or down.
        var (gx, gy) = facing switch { Dir.Up => (0.0, -1.0), Dir.Down => (0.0, 1.0), _ => (side == Dir.Left ? -1.0 : 1.0, 0.0) };
        var grip = P(4, gy < 0 ? -4 : gy > 0 ? 4 : 0);
        dc.DrawLine(Pen(0xFF6A7A8A, 4.5 * k), grip, new Point(grip.X + gx * 16 * k, grip.Y + gy * 16 * k));
        dc.DrawLine(Pen(0xFFB0C0D0, 2 * k), new Point(grip.X + gx * 14 * k, grip.Y + gy * 14 * k), new Point(grip.X + gx * 19 * k, grip.Y + gy * 19 * k));
    }

    private void DrawEnemy(DrawingContext dc, Enemy e)
    {
        var at = Px(e.X, e.Y);
        double grow = Math.Min(1, e.Age / GameEngine.SpawnSeconds);
        if (e.Kind == EnemyKind.Wizard)
        {
            // It flickers as it comes and goes.
            if (e.ShowFor > 0 && (int)(_clock * 30) % 2 == 0) return;
            DrawWizard(dc, at);
            return;
        }
        if (!e.Seen && grow >= 1) return;
        if (grow < 1 && (int)(_clock * 24) % 2 == 0) return;
        double s = 0.4 + 0.6 * grow;
        uint body = GameEngine.Colour(e.Kind);
        double walk = Math.Sin((e.X + e.Y) * Math.PI * 3) * 4;
        var f = e.Side;
        var P = (double fw, double sd) => Place(at, f, s, (fw, sd))[0];
        switch (e.Kind)
        {
            case EnemyKind.Prowler:
                // A hunched beast on four legs, ears up.
                foreach (var (x, o) in new[] { (-12.0, walk), (-6.0, -walk), (6.0, walk), (12.0, -walk) })
                    dc.DrawLine(Pen(0xFF1A3A8A, 4.5 * s), P(x, 4), P(x + o, 17));
                Poly(dc, body, 0xFF0A1A4A, 1.2, Place(at, f, s, (-18, -2), (-8, -10), (8, -10), (14, -4), (14, 6), (-16, 6)));
                Poly(dc, body, 0xFF0A1A4A, 1.2, Place(at, f, s, (8, -14), (12, -24), (15, -15), (20, -12), (24, -4), (14, 2)));
                Poly(dc, 0xFFFFFFFF, null, 0, Place(at, f, s, (17, -9), (20, -9), (19, -6)));
                dc.DrawLine(Pen(body, 4 * s), P(-17, 0), P(-26, -10 + walk * 0.5));
                break;
            case EnemyKind.Shade:
                // Low and spined, with a long snout.
                foreach (var (x, o) in new[] { (-10.0, walk), (0.0, -walk), (10.0, walk) })
                    dc.DrawLine(Pen(0xFF8A6A0A, 4 * s), P(x, 4), P(x + o, 16));
                Poly(dc, body, 0xFF5A3A00, 1.2, Place(at, f, s, (-20, 4), (-14, -8), (-8, -4), (-4, -12), (2, -6), (8, -12), (12, -4), (26, -2), (26, 4), (12, 6)));
                Poly(dc, 0xFFFF2A2A, null, 0, Place(at, f, s, (19, -3), (22, -3), (21, 0)));
                break;
            case EnemyKind.Phantom:
                // Lean and horned, built for speed.
                foreach (var (x, o) in new[] { (-10.0, walk * 1.4), (8.0, -walk * 1.4) })
                    dc.DrawLine(Pen(0xFF8A1A1A, 4 * s), P(x, 2), P(x + o, 16));
                Poly(dc, body, 0xFF4A0A0A, 1.2, Place(at, f, s, (-22, -6), (-8, -6), (10, -8), (22, -4), (24, 2), (-8, 4), (-20, 2)));
                Poly(dc, body, 0xFF4A0A0A, 1.2, Place(at, f, s, (10, -8), (6, -20), (14, -10)));
                Poly(dc, body, 0xFF4A0A0A, 1.2, Place(at, f, s, (16, -6), (16, -18), (20, -6)));
                Poly(dc, 0xFFFFFF60, null, 0, Place(at, f, s, (18, -3), (21, -3), (20, 0)));
                break;
            case EnemyKind.Wisp:
            {
                // A winged thing, wings beating.
                s *= 1.5;
                double flap = Math.Sin(_clock * 30) * 10;
                Poly(dc, Alpha(body, 0.7), null, 0, Place(at, f, s, (-2, -2), (-14, -14 - flap), (6, -6)));
                Poly(dc, Alpha(body, 0.7), null, 0, Place(at, f, s, (-2, 2), (-14, 14 + flap), (6, 6)));
                dc.DrawEllipse(Brush(body), Pen(0xFF0A4A2A, 1.2), at, 12 * s, 9 * s);
                Poly(dc, 0xFF0A4A2A, null, 0, Place(at, f, s, (6, -3), (10, -3), (9, 0)));
                Poly(dc, 0xFF0A4A2A, null, 0, Place(at, f, s, (6, 3), (10, 3), (9, 0)));
                break;
            }
        }
    }

    private void DrawWizard(DrawingContext dc, Point at)
    {
        double sway = Math.Sin(_clock * 4) * 2;
        uint robe = 0xFFB050F0;
        // A glow, robe, face, a tall hat with stars, and a staff.
        dc.DrawEllipse(Brush(Alpha(robe, 0.18 + 0.1 * Math.Sin(_clock * 8))), null, at, 34, 34);
        Poly(dc, robe, 0xFF3A0A5A, 1.4, new Point(at.X - 18, at.Y + 26), new Point(at.X - 8 + sway, at.Y - 8), new Point(at.X + 8 + sway, at.Y - 8), new Point(at.X + 18, at.Y + 26));
        dc.DrawEllipse(Brush(0xFFF0D0B0), null, new Point(at.X + sway, at.Y - 12), 7, 7);
        Poly(dc, 0xFFE0E0F0, null, 0, new Point(at.X - 6 + sway, at.Y - 9), new Point(at.X + 6 + sway, at.Y - 9), new Point(at.X + sway, at.Y + 6));
        Poly(dc, 0xFF5A1A9A, 0xFF2A0A4A, 1.2, new Point(at.X - 13 + sway, at.Y - 16), new Point(at.X + 13 + sway, at.Y - 16), new Point(at.X + 4 + sway * 2, at.Y - 44));
        dc.DrawEllipse(Brush(Yellow), null, new Point(at.X + 2 + sway * 1.5, at.Y - 28), 2.5, 2.5);
        dc.DrawLine(Pen(0xFF8A5A2A, 3), new Point(at.X + 20, at.Y + 28), new Point(at.X + 20, at.Y - 24));
        dc.DrawEllipse(Brush(0xFF7AF0FF), null, new Point(at.X + 20, at.Y - 26), 5 + Math.Sin(_clock * 12), 5 + Math.Sin(_clock * 12));
    }

    private void DrawBolt(DrawingContext dc, Bolt b, bool mine)
    {
        var at = Px(b.X, b.Y);
        var (vx, vy) = Dirs.Vec(b.Dir);
        var tail = new Point(at.X - vx * 22, at.Y - vy * 22);
        if (mine)
        {
            dc.DrawLine(Pen(Alpha(Yellow, 0.35), 8), tail, at);
            dc.DrawLine(Pen(0xFFFFF6C0, 3), tail, at);
        }
        else if (b.FromWizard)
        {
            // A crackling bolt of lightning.
            var pts = Enumerable.Range(0, 6).Select(k =>
            {
                double t = k / 5.0, j = (Hash(k, (int)(_clock * 20), 5) % 9 - 4.0) * 1.6;
                return new Point(at.X - vx * 30 * t + vy * j, at.Y - vy * 30 * t + vx * j);
            }).ToArray();
            for (int k = 0; k < pts.Length - 1; k++) dc.DrawLine(Pen(0xFFE0A0FF, 3), pts[k], pts[k + 1]);
        }
        else
        {
            uint colour = b.Owner is { } o ? GameEngine.Colour(o.Kind) : Red;
            dc.DrawLine(Pen(Alpha(colour, 0.4), 8), new Point(at.X - vx * 14, at.Y - vy * 14), at);
            dc.DrawLine(Pen(0xFFFFFFFF, 3), new Point(at.X - vx * 8, at.Y - vy * 8), at);
        }
    }

    private static void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 36 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 3), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = r * (0.6 + Hash(k, 2, 3) % 10 * 0.06);
                    dc.FillRectangle(Brush(Alpha(0xFFFFF0C0, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2, c.Y + Math.Sin(a) * d - 2, 4, 4));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 14 - t * 24), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 40, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (inGame)
        {
            string name = GameEngine.NameOf(g.Dungeon);
            Text(dc, $"DUNGEON {g.Dungeon}" + (name != "" ? $" - {name}" : ""), new Point(Field.Width / 2, 40), 34, White, center: true);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Y0 + Field.DoorRow * Field.Cell + Field.Cell;
        var (w, h) = (Math.Max(msg.Length * 30, (g.SubMessage?.Length ?? 0) * 16) + 60, g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - w / 2, cy - 48, w, h));
        Text(dc, msg, new Point(cx, cy - 12), 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 30, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawWizard(dc, new Point(cx, 190));
        Text(dc, "AVA WIZARD OF WOR", new Point(cx, 110), 96, 0xFFB070FF, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 250), 28, Dim, center: true);
        string[] lines =
        [
            "HUNT THE MONSTERS OF THE DUNGEON BEFORE THEY HUNT YOU",
            "SOME CAN'T BE SEEN - WATCH THE RADAR",
            "SLAY THE WISP AND THE WIZARD COMES FOR YOU",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 296 + i * 32), 27, White, center: true);
        EnemyKind[] kinds = [EnemyKind.Prowler, EnemyKind.Shade, EnemyKind.Phantom, EnemyKind.Wisp, EnemyKind.Wizard];
        for (int i = 0; i < kinds.Length; i++)
        {
            double x = 150 + i * 181;
            var at = new Point(x, 440);
            if (kinds[i] == EnemyKind.Wizard) DrawWizard(dc, at);
            else DrawEnemy(dc, new Enemy { Kind = kinds[i], X = 0, Y = 0, Age = 9, Facing = Dir.Left, Seen = true }, at);
            Text(dc, GameEngine.NameOf(kinds[i]), new Point(x, 494), 24, GameEngine.Colour(kinds[i]), center: true);
            Text(dc, GameEngine.Points(kinds[i]).ToString(), new Point(x, 520), 26, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 600), 46, Yellow, center: true);
        Text(dc, "ARROWS: WALK (HOLD TWO TO TURN THE NEXT CORNER)    SPACE: FIRE", new Point(cx, 664), 24, Dim, center: true);
    }

    /// <summary>A monster drawn somewhere other than its place in the dungeon (the title's table).</summary>
    private void DrawEnemy(DrawingContext dc, Enemy e, Point at)
    {
        var (x, y) = ((at.X - Field.X0) / Field.Cell - 0.5, (at.Y - Field.Y0) / Field.Cell - 0.5);
        (e.X, e.Y) = (x, y);
        DrawEnemy(dc, e);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE BRAVEST WARRIORS", new Point(cx, 130), 64, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   DUNGEON", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}  ", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
