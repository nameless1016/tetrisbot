using System;
using System.Collections.Generic;
using System.Text;
using TetrisBot.Game;

namespace TetrisBot.UI;

/// <summary>Draws a game as lines of text (ANSI colors, or plain ASCII with UseColor=false).</summary>
public static class ConsoleRenderer
{
    public static bool UseColor = true;
    public const int PanelWidth = 30;

    private const string Esc = "\u001b";
    private static readonly string[] Codes = { "96", "93", "95", "92", "91", "94", "38;5;208" }; // I O T S Z J L
    private const string Letters = "IOTSZJL";

    private static string Color(string code, string text) => UseColor ? Esc + "[" + code + "m" + text + Esc + "[0m" : text;

    private static string Letter(PieceType t) => Color(Codes[(int)t], Letters[(int)t].ToString());

    public static int VisibleLength(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\u001b') { while (i < s.Length && s[i] != 'm') i++; continue; }
            n++;
        }
        return n;
    }

    public static string Pad(string s, int width) => s + new string(' ', Math.Max(0, width - VisibleLength(s)));

    private static string CellStr(int v)
    {
        if (v == 0) return UseColor ? Color("90", "· ") : ". ";
        if (v >= 8) return UseColor ? Color(Codes[v - 8], "░░") : "::";
        return UseColor ? Color(Codes[v - 1], "██") : "[]";
    }

    public static string ClearText(ClearResult r)
    {
        if (r.Lines == 0 && r.Spin == SpinKind.None) return "";
        var parts = new List<string>();
        if (r.Spin == SpinKind.Full) parts.Add("T-SPIN");
        else if (r.Spin == SpinKind.Mini) parts.Add("T-SPIN MINI");
        if (r.Lines > 0) parts.Add(r.Lines switch { 1 => "SINGLE", 2 => "DOUBLE", 3 => "TRIPLE", _ => "TETRIS" });
        string s = string.Join(" ", parts);
        if (r.BackToBack) s += " B2B";
        if (r.Combo >= 1) s += " " + r.Combo + " COMBO";
        if (r.AllClear) s += " ALL CLEAR!";
        return s;
    }

    public static string FormatTime(double t) => $"{(int)(t / 60):00}:{t % 60:00.0}";

    public static List<string> Panel(TetrisGame g, string title, double elapsed, string message)
    {
        var L = new List<string>();
        L.Add(Pad(title, PanelWidth));

        var next = new StringBuilder();
        for (int i = 0; i < 5; i++) next.Append(Letter(g.PeekNext(i))).Append(' ');
        string hold = g.Hold.HasValue ? Letter(g.Hold.Value) : "-";
        L.Add(Pad("HOLD " + hold + "  NEXT " + next, PanelWidth));

        int[] disp = new int[Board.VisibleHeight * Board.Width];
        for (int y = 0; y < Board.VisibleHeight; y++)
            for (int x = 0; x < Board.Width; x++)
                disp[y * Board.Width + x] = g.Field.Cells[(y + Board.HiddenRows) * Board.Width + x];

        if (!g.GameOver)
        {
            var cur = g.Current;
            var c = PieceData.GetCells(cur.Type, cur.Rot);
            int gy = g.GhostY();
            for (int i = 0; i < 4; i++)
            {
                int gx = cur.X + c[i * 2];
                int gyy = gy + c[i * 2 + 1] - Board.HiddenRows;
                if (gyy >= 0 && disp[gyy * Board.Width + gx] == 0) disp[gyy * Board.Width + gx] = 8 + (int)cur.Type;
            }
            for (int i = 0; i < 4; i++)
            {
                int px = cur.X + c[i * 2];
                int py = cur.Y + c[i * 2 + 1] - Board.HiddenRows;
                if (py >= 0) disp[py * Board.Width + px] = 1 + (int)cur.Type;
            }
        }

        L.Add(Pad("┌" + new string('─', Board.Width * 2) + "┐", PanelWidth));
        for (int y = 0; y < Board.VisibleHeight; y++)
        {
            var sb = new StringBuilder("│");
            for (int x = 0; x < Board.Width; x++) sb.Append(CellStr(disp[y * Board.Width + x]));
            sb.Append('│');
            L.Add(Pad(sb.ToString(), PanelWidth));
        }
        L.Add(Pad("└" + new string('─', Board.Width * 2) + "┘", PanelWidth));

        double pps = elapsed > 0.5 ? g.PiecesPlaced / elapsed : 0;
        L.Add(Pad($"Score {g.Score}", PanelWidth));
        L.Add(Pad($"Lines {g.Lines}  Level {g.Level}", PanelWidth));
        L.Add(Pad($"Pieces {g.PiecesPlaced}  PPS {pps:F2}", PanelWidth));
        L.Add(Pad($"Time {FormatTime(elapsed)}", PanelWidth));
        L.Add(Pad(g.Scoring.BackToBack ? "B2B active" : "", PanelWidth));
        L.Add(Pad(g.GameOver ? "GAME OVER" : message, PanelWidth));
        return L;
    }
}