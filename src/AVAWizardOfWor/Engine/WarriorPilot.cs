namespace AVAWizardOfWor.Engine;

/// <summary>
/// The computer at the stick, for the demonstration (and the tests). It watches the radar as a player
/// would, so it knows where the unseen ones are. Anything straight down a corridor from it, monster or
/// bullet, it turns to face and shoots, standing its ground; otherwise it walks to the nearest spot with a
/// clear shot at something a safe way off.
/// </summary>
public sealed class WarriorPilot
{
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var w = g.Warrior;
        if (!w.Alive || g.Phase != Phase.Play) return default;
        var m = g.Maze;
        if (w.InCage)
        {
            // Out only when nothing is lurking by the way out (the count forces it out in the end anyway).
            var exit = m.Distances(Field.CageX, Field.Rows - 1);
            bool lurking = g.Enemies.Any(e => !e.Dead && !e.Outside && exit[e.Cx, e.Cy] is >= 0 and <= 2);
            Doing = "cage";
            return w.CageTime > 0.4 && !lurking ? new Controls(Dy: -1) : default;
        }
        if (w.Outside) return Press(w.Dir == Dir.None ? Dir.Left : w.Dir);
        int c = w.Cx, r = w.Cy;
        bool onRow = Math.Abs(w.Y - r) < 1e-6, onColumn = Math.Abs(w.X - c) < 1e-6;

        // 1. Something coming straight down the corridor: face it and shoot.
        Dir aim = Dir.None;
        double nearest = 99;
        foreach (var e in g.Enemies)
        {
            if (e.Dead || e.Outside || (e.Kind != EnemyKind.Wizard && e.Age < GameEngine.SpawnSeconds * 0.5)) continue;
            Consider(e.X, e.Y, e.Cx, e.Cy);
        }
        foreach (var b in g.Bullets)
        {
            int bc = (int)Math.Round(b.X), br = (int)Math.Round(b.Y);
            if (!Field.Inside(bc, br)) continue;
            // Only one coming this way.
            var toward = Toward(b.X, b.Y, w.X, w.Y);
            if (toward == b.Dir) Consider(b.X, b.Y, bc, br);
        }
        if (aim != Dir.None)
        {
            Doing = "shoot";
            return w.Facing == aim ? new Controls(Fire: true) : Press(aim, true);
        }

        // 2. Something close, round a corner where it can't be shot: back away from it.
        var close = g.Enemies.Where(e => !e.Dead && !e.Outside && Math.Abs(e.X - w.X) + Math.Abs(e.Y - w.Y) < 1.7).OrderBy(e => Math.Abs(e.X - w.X) + Math.Abs(e.Y - w.Y)).FirstOrDefault();
        if (close != null)
        {
            var from = m.Distances(close.Cx, close.Cy);
            IEnumerable<Dir> ways = w.AtCentre ? Dirs.All.Where(d => m.Open(c, r, d)) : onRow ? [Dir.Left, Dir.Right] : [Dir.Up, Dir.Down];
            var away = ways.OrderByDescending(d =>
            {
                var (nc, nr) = w.AtCentre ? Maze.Next(c, r, d) : Ahead(w, d);
                return Field.Inside(nc, nr) && from[nc, nr] >= 0 ? from[nc, nr] - Crowd(g, nc, nr) * 2 : -9;
            }).FirstOrDefault();
            if (away != Dir.None)
            {
                Doing = "back off";
                return Press(away);
            }
        }

        // 3. Walk to the nearest cell with a clear shot down a corridor at something a safe way off.
        var here = m.Distances(c, r);
        (int C, int R) target = (-1, -1);
        int best = int.MaxValue;
        for (int x = 0; x < Field.Cols; x++)
            for (int y = 0; y < Field.Rows; y++)
            {
                if (here[x, y] < 0) continue;
                int score = here[x, y];
                bool shot = false, danger = false;
                foreach (var e in g.Enemies)
                {
                    if (e.Dead || e.Outside) continue;
                    int span = Math.Abs(e.Cx - x) + Math.Abs(e.Cy - y);
                    if (span <= 1) danger = true;
                    if (span >= 2 && span <= 6 && m.Clear(x, y, e.Cx, e.Cy)) shot = true;
                }
                if (!shot) continue;
                if (danger) score += 6;
                if (score < best) { best = score; target = (x, y); }
            }
        // Nowhere with a clear shot: go after the nearest one.
        if (target.C < 0)
        {
            var prey = g.Enemies.Where(e => !e.Dead && !e.Outside).OrderBy(e => here[e.Cx, e.Cy] < 0 ? 999 : here[e.Cx, e.Cy]).FirstOrDefault();
            if (prey == null)
            {
                Doing = "wait";
                return default;
            }
            target = (prey.Cx, prey.Cy);
        }
        if (target == (c, r) && w.AtCentre)
        {
            Doing = "hold";
            return default;
        }
        var to = m.Distances(target.C, target.R);
        // The quickest way there that doesn't walk up next to something.
        var way = Dirs.All.Where(d => m.Open(c, r, d)).OrderBy(d => { var (nc, nr) = Maze.Next(c, r, d); return (to[nc, nr] < 0 ? 999 : to[nc, nr]) + Crowd(g, nc, nr) * 4; }).FirstOrDefault();
        Doing = "walk";
        return Press(way);

        void Consider(double x, double y, int tc, int tr)
        {
            bool row = tr == r && onRow, column = tc == c && onColumn;
            if (!row && !column) return;
            if (!m.Clear(c, r, tc, tr)) return;
            double d = Math.Abs(x - w.X) + Math.Abs(y - w.Y);
            if (d > 8 || d >= nearest) return;
            nearest = d;
            aim = Toward(w.X, w.Y, x, y);
        }
    }

    /// <summary>How many monsters are in or next to a cell.</summary>
    private static int Crowd(GameEngine g, int c, int r) => g.Enemies.Count(e => !e.Dead && Math.Abs(e.X - c) + Math.Abs(e.Y - r) < 1.5);

    /// <summary>The centre the warrior comes to going a way along its corridor.</summary>
    private static (int C, int R) Ahead(Warrior w, Dir d)
    {
        var (dx, dy) = Dirs.Vec(d);
        return (dx > 0 ? (int)Math.Ceiling(w.X) : dx < 0 ? (int)Math.Floor(w.X) : w.Cx, dy > 0 ? (int)Math.Ceiling(w.Y) : dy < 0 ? (int)Math.Floor(w.Y) : w.Cy);
    }

    private static Dir Toward(double fx, double fy, double tx, double ty) =>
        Math.Abs(tx - fx) > Math.Abs(ty - fy) ? (tx > fx ? Dir.Right : Dir.Left) : (ty > fy ? Dir.Down : Dir.Up);

    private static Controls Press(Dir d, bool fire = false)
    {
        var (dx, dy) = Dirs.Vec(d);
        return new Controls(dx, dy, fire);
    }
}
