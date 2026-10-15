namespace AVAWizardOfWor.Engine;

public enum MazeKind { Dungeon, Courtyard, Hollow }

/// <summary>
/// A dungeon: eleven cells by six, the same left and right, with no dead ends, and a door out of each
/// side of the third row that leads round to the other. Built from its number, so a dungeon is always the
/// same.
/// </summary>
public sealed class Maze
{
    public const int Cols = Field.Cols, Rows = Field.Rows;

    /// <summary>H[c, r]: a wall along the top of cell (c, r) (r = Rows is the bottom edge). V[c, r]: along its left side.</summary>
    public readonly bool[,] H = new bool[Cols, Rows + 1], V = new bool[Cols + 1, Rows];
    public MazeKind Kind { get; private set; }
    public bool DoorsOpen = true;

    public static MazeKind KindOf(int dungeon) =>
        dungeon >= 13 && (dungeon - 13) % 6 == 0 ? MazeKind.Hollow : dungeon % 4 == 0 ? MazeKind.Courtyard : MazeKind.Dungeon;

    public static Maze Build(int dungeon)
    {
        var m = new Maze { Kind = KindOf(dungeon) };
        for (int c = 0; c < Cols; c++) for (int r = 0; r <= Rows; r++) m.H[c, r] = true;
        for (int c = 0; c <= Cols; c++) for (int r = 0; r < Rows; r++) m.V[c, r] = true;
        var rng = new Random(dungeon * 7919 + 13);
        if (m.Kind == MazeKind.Hollow)
        {
            for (int c = 0; c < Cols; c++) for (int r = 1; r < Rows; r++) m.H[c, r] = false;
            for (int c = 1; c < Cols; c++) for (int r = 0; r < Rows; r++) m.V[c, r] = false;
            return m;
        }
        // A winding tree through the left half and the middle column, mirrored to the right: all of it joined.
        int half = Cols / 2;
        var seen = new bool[half + 1, Rows];
        var stack = new Stack<(int C, int R)>();
        stack.Push((half, rng.Next(Rows)));
        seen[stack.Peek().C, stack.Peek().R] = true;
        while (stack.Count > 0)
        {
            var (c, r) = stack.Peek();
            var ways = Dirs.All.Where(d => { var (dx, dy) = Dirs.Vec(d); int nc = c + dx, nr = r + dy; return nc >= 0 && nc <= half && nr >= 0 && nr < Rows && !seen[nc, nr]; }).ToList();
            if (ways.Count == 0) { stack.Pop(); continue; }
            var d = ways[rng.Next(ways.Count)];
            m.Knock(c, r, d);
            var (vx, vy) = Dirs.Vec(d);
            seen[c + vx, r + vy] = true;
            stack.Push((c + vx, r + vy));
        }
        // No dead ends: anything walled in on three sides gets another way out.
        for (int pass = 0; pass < 3; pass++)
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    var closed = Dirs.All.Where(d => !m.Open(c, r, d) && m.Interior(c, r, d)).ToList();
                    if (Dirs.All.Count(d => m.Open(c, r, d)) < 2 && closed.Count > 0) m.Knock(c, r, closed[rng.Next(closed.Count)]);
                }
        // A few more loops, and many more in a courtyard.
        int extra = m.Kind == MazeKind.Courtyard ? 16 : 3;
        for (int k = 0; k < extra; k++)
        {
            int c = rng.Next(Cols), r = rng.Next(Rows);
            var closed = Dirs.All.Where(d => !m.Open(c, r, d) && m.Interior(c, r, d)).ToList();
            if (closed.Count > 0) m.Knock(c, r, closed[rng.Next(closed.Count)]);
        }
        return m;
    }

    private bool Interior(int c, int r, Dir d)
    {
        var (dx, dy) = Dirs.Vec(d);
        return Field.Inside(c + dx, r + dy);
    }

    /// <summary>Takes down a wall, and its mirror image on the other side.</summary>
    private void Knock(int c, int r, Dir d)
    {
        Remove(c, r, d);
        Remove(Cols - 1 - c, r, d is Dir.Left ? Dir.Right : d is Dir.Right ? Dir.Left : d);
    }

    private void Remove(int c, int r, Dir d)
    {
        switch (d)
        {
            case Dir.Up: H[c, r] = false; break;
            case Dir.Down: H[c, r + 1] = false; break;
            case Dir.Left: V[c, r] = false; break;
            case Dir.Right: V[c + 1, r] = false; break;
        }
    }

    /// <summary>Whether there's a way from a cell (or the doorway outside a side door) one step the given way.</summary>
    public bool Open(int c, int r, Dir d)
    {
        if (r == Field.DoorRow && (c < 0 || c >= Cols)) return Dirs.Horizontal(d);
        if (!Field.Inside(c, r)) return false;
        return d switch
        {
            Dir.Up => r > 0 && !H[c, r],
            Dir.Down => r < Rows - 1 && !H[c, r + 1],
            Dir.Left => c > 0 ? !V[c, r] : r == Field.DoorRow && DoorsOpen,
            Dir.Right => c < Cols - 1 ? !V[c + 1, r] : r == Field.DoorRow && DoorsOpen,
            _ => false,
        };
    }

    /// <summary>The cell one step the given way, going through a side door to the far side.</summary>
    public static (int C, int R) Next(int c, int r, Dir d)
    {
        var (dx, dy) = Dirs.Vec(d);
        int nc = c + dx, nr = r + dy;
        if (nr == Field.DoorRow && nc < 0) nc = Cols - 1;
        else if (nr == Field.DoorRow && nc >= Cols) nc = 0;
        return (nc, nr);
    }

    /// <summary>Steps from a cell to every other (−1 where it can't be reached).</summary>
    public int[,] Distances(int c0, int r0)
    {
        var dist = new int[Cols, Rows];
        for (int c = 0; c < Cols; c++) for (int r = 0; r < Rows; r++) dist[c, r] = -1;
        if (!Field.Inside(c0, r0)) return dist;
        var q = new Queue<(int, int)>();
        dist[c0, r0] = 0;
        q.Enqueue((c0, r0));
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var d in Dirs.All)
            {
                if (!Open(c, r, d)) continue;
                var (nc, nr) = Next(c, r, d);
                if (dist[nc, nr] >= 0) continue;
                dist[nc, nr] = dist[c, r] + 1;
                q.Enqueue((nc, nr));
            }
        }
        return dist;
    }

    /// <summary>Whether two cells in the same row or column can see each other down the corridor.</summary>
    public bool Clear(int c1, int r1, int c2, int r2)
    {
        if (!Field.Inside(c1, r1) || !Field.Inside(c2, r2) || (c1 != c2 && r1 != r2)) return false;
        var d = c1 != c2 ? (c2 > c1 ? Dir.Right : Dir.Left) : (r2 > r1 ? Dir.Down : Dir.Up);
        var (dx, dy) = Dirs.Vec(d);
        for (int c = c1, r = r1; c != c2 || r != r2; c += dx, r += dy)
            if (!Open(c, r, d)) return false;
        return true;
    }
}
