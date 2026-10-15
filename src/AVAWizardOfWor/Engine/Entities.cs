namespace AVAWizardOfWor.Engine;

/// <summary>The stick (four ways; two at once means "turn here if you can") and the fire button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

public enum Dir { None, Up, Down, Left, Right }

public static class Dirs
{
    public static readonly Dir[] All = [Dir.Up, Dir.Left, Dir.Down, Dir.Right];

    public static (int X, int Y) Vec(Dir d) => d switch { Dir.Up => (0, -1), Dir.Down => (0, 1), Dir.Left => (-1, 0), Dir.Right => (1, 0), _ => (0, 0) };

    public static Dir Opposite(Dir d) => d switch { Dir.Up => Dir.Down, Dir.Down => Dir.Up, Dir.Left => Dir.Right, Dir.Right => Dir.Left, _ => Dir.None };

    public static bool Horizontal(Dir d) => d is Dir.Left or Dir.Right;

    public static Dir Of(int dx, int dy) => dx > 0 ? Dir.Right : dx < 0 ? Dir.Left : dy > 0 ? Dir.Down : dy < 0 ? Dir.Up : Dir.None;
}

/// <summary>
/// The screen and the dungeon on it. Positions are in cells: a cell's centre is at whole numbers, (0, 0)
/// top left to (10, 5) bottom right. The side doors lead out of row 2, and the warrior's cage sits below
/// the bottom right cell, at (10, 6).
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64;
    public const int Cols = 11, Rows = 6, DoorRow = 2;
    public const int CageX = 10, CageY = 6;
    public const double Cell = 76;
    public const double X0 = (Width - Cols * Cell) / 2, Y0 = 88;

    /// <summary>Where a point in cells is on the screen.</summary>
    public static (double X, double Y) Px(double x, double y) => (X0 + (x + 0.5) * Cell, Y0 + (y + 0.5) * Cell);

    public static bool Inside(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;
}

/// <summary>Anything that walks the corridors, from cell centre to cell centre.</summary>
public class Mover
{
    public double X, Y;
    public Dir Dir;
    private Dir _facing = Dir.Left;
    /// <summary>The way it looks (and shoots).</summary>
    public Dir Facing
    {
        get => _facing;
        set
        {
            _facing = value;
            if (Dirs.Horizontal(value)) Side = value;
        }
    }
    /// <summary>The last way it looked left or right: figures are drawn side-on, facing this way.</summary>
    public Dir Side { get; private set; } = Dir.Left;
    public int Cx => (int)Math.Round(X);
    public int Cy => (int)Math.Round(Y);
    public bool AtCentre => Math.Abs(X - Math.Round(X)) < 1e-6 && Math.Abs(Y - Math.Round(Y)) < 1e-6;
    public bool Outside => !Field.Inside(Cx, Cy);
}

public sealed class Warrior : Mover
{
    public bool Alive = true;
    /// <summary>Waiting in the cage below the dungeon (where nothing can reach it), or on the way out of it.</summary>
    public bool InCage = true, Leaving;
    public double CageTime;
}

public enum EnemyKind { Prowler, Shade, Phantom, Wisp, Wizard }

public sealed class Enemy : Mover
{
    public EnemyKind Kind;
    public bool Dead;
    public double Age, FireWait;
    /// <summary>Whether the warrior can see it (the shades and phantoms are unseen unless close or in line).</summary>
    public bool Seen = true;
    public double ShowFor;
    /// <summary>The wizard: when it next vanishes and reappears elsewhere.</summary>
    public double HopIn;
}

/// <summary>A shot: flies straight along a corridor until it hits a wall or something.</summary>
public sealed class Bolt
{
    public double X, Y, Speed;
    public Dir Dir;
    public bool Dead, FromWizard;
    public Enemy? Owner;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
