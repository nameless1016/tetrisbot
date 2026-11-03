namespace TetrisBot.Game;

/// <summary>10x40 playfield (20 hidden buffer rows on top + 20 visible). Row 0 is the top.</summary>
public sealed class Board
{
    public const int Width = 10;
    public const int Height = 40;
    public const int HiddenRows = 20;
    public const int VisibleHeight = Height - HiddenRows;

    /// <summary>0 = empty, otherwise (PieceType + 1).</summary>
    public readonly byte[] Cells = new byte[Width * Height];

    public bool IsBlocked(int x, int y)
        => x < 0 || x >= Width || y < 0 || y >= Height || Cells[y * Width + x] != 0;

    public bool Collides(PieceType type, int rot, int px, int py)
    {
        int[] c = PieceData.GetCells(type, rot);
        for (int i = 0; i < 8; i += 2)
            if (IsBlocked(px + c[i], py + c[i + 1])) return true;
        return false;
    }

    public void Place(PieceType type, int rot, int px, int py)
    {
        int[] c = PieceData.GetCells(type, rot);
        byte v = (byte)((int)type + 1);
        for (int i = 0; i < 8; i += 2)
            Cells[(py + c[i + 1]) * Width + px + c[i]] = v;
    }

    /// <summary>Removes full rows, compacts the stack, returns how many rows were cleared.</summary>
    public int ClearLines()
    {
        int write = Height - 1, cleared = 0;
        for (int y = Height - 1; y >= 0; y--)
        {
            bool full = true;
            for (int x = 0; x < Width; x++)
                if (Cells[y * Width + x] == 0) { full = false; break; }
            if (full) { cleared++; continue; }
            if (write != y) Array.Copy(Cells, y * Width, Cells, write * Width, Width);
            write--;
        }
        for (int y = write; y >= 0; y--) Array.Clear(Cells, y * Width, Width);
        return cleared;
    }

    public bool IsEmpty()
    {
        for (int i = 0; i < Cells.Length; i++) if (Cells[i] != 0) return false;
        return true;
    }

    public void Clear() => Array.Clear(Cells, 0, Cells.Length);
    public void CopyFrom(Board other) => Array.Copy(other.Cells, Cells, Cells.Length);
}
