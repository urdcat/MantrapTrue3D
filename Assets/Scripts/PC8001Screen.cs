
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Text;

// PC-8001: 80x25 text cells, each cell has 2x4 semigraphic subcells => 160x100 "low-res" graphics.
// Semantics:
//  - Printing a character sets a text glyph in the cell and clears semigraphic bits in that cell.
//  - Drawing semigraphics (PSET/LINE/PUT@) sets bits and erases any text glyph in that cell.
public class PC8001Screen : MonoBehaviour
{
    public int cols = 80;
    public int rows = 25;

    public RawImage gfxTarget;     // A RawImage on a Canvas to show the 160x100 semigraphics texture (scaled as you like)
    public TMP_Text textTarget;    // A monospaced TMP_Text to display 80x25 text

    // Colors
    public Color32 fg = new Color32(255,255,255,255);
    public Color32 bg = new Color32(0,0,0,255);

    // Texture for semigraphics (160x100)
    Texture2D _tex;
    Color32[] _pix;
    const int gW = 160;
    const int gH = 100;

    // Text buffer and semigraphic bits
    char[,] _chars;
    byte[,] _semi; // 8 bits per cell: (ySub*2 + xSub), ySub=0..3, xSub=0..1
    StringBuilder _sb = new StringBuilder();

    int _cx = 0, _cy = 0;

    void Awake()
    {
        _tex = new Texture2D(gW, gH, TextureFormat.RGBA32, false);
        _tex.filterMode = FilterMode.Point;
        _pix = new Color32[gW*gH];
        if (gfxTarget != null) gfxTarget.texture = _tex;

        _chars = new char[rows, cols];
        _semi  = new byte[rows, cols];

        Cls();
    }

    public void Cls()
    {
        // clear text & semigraphics
        for (int r=0; r<rows; r++)
        {
            for (int c=0; c<cols; c++)
            {
                _chars[r,c] = ' ';
                _semi[r,c] = 0;
            }
        }
        for (int i=0;i<_pix.Length;i++) _pix[i] = bg;
        FlushAll();
        _cx = _cy = 0;
    }

    void FlushAll()
    {
        // redraw semigraphics texture
        _tex.SetPixels32(_pix);
        _tex.Apply(false);

        // redraw text
        if (textTarget != null)
        {
            _sb.Clear();
            for (int r=0; r<rows; r++)
            {
                for (int c=0; c<cols; c++)
                {
                    // If semigraphics exist at this cell, the text is erased.
                    _sb.Append(_semi[r,c] != 0 ? ' ' : _chars[r,c]);
                }
                if (r < rows-1) _sb.Append('\n');
            }
            textTarget.text = _sb.ToString();
        }
    }

    public void SetColor(Color32 newFg, Color32 newBg)
    {
        fg = newFg; bg = newBg;
        // On a real machine, color is attribute-wide; here we simply redraw background for blank pixels.
        for (int i=0;i<_pix.Length;i++)
        {
            if (_pix[i].Equals(bg)) _pix[i] = bg;
        }
        FlushAll();
    }

    public void Locate(int col, int row)
    {
        _cx = Mathf.Clamp(col, 0, cols-1);
        _cy = Mathf.Clamp(row, 0, rows-1);
    }

    public void Print(string s)
    {
        foreach (var ch in s)
        {
            if (ch == '\n')
            {
                _cx = 0; _cy = Mathf.Min(_cy+1, rows-1);
                continue;
            }
            if (ch == (char)12) // CLS via CHR$(12)
            {
                Cls();
                continue;
            }
            // Place char and clear semigraphics in this cell (text overwrites graphics)
            _chars[_cy,_cx] = ch;
            _semi[_cy,_cx]  = 0;
            // Also clear 2x4 pixels in gfx
            ClearCellGfx(_cx, _cy);

            _cx++;
            if (_cx >= cols) { _cx = 0; _cy = Mathf.Min(_cy+1, rows-1); }
        }
        FlushAll();
    }

    void ClearCellGfx(int col, int row)
    {
        int x0 = col * 2;
        int y0 = row * 4;
        for (int ys=0; ys<4; ys++)
            for (int xs=0; xs<2; xs++)
                SetPixel160(x0+xs, y0+ys, bg, false);
    }

    // Set one 160x100 pixel; y=0 top, x=0 left
    void SetPixel160(int x, int y, Color32 c, bool blendToFg)
    {
        if ((uint)x >= gW || (uint)y >= gH) return;
        int idx = (gH-1-y)*gW + x; // flip Y for Unity texture
        _pix[idx] = c;
    }

    // PSET at 160x100; xor toggles the subcell bit; set=true sets bit on
    public void PSet160(int x, int y, bool set=true, bool xor=false)
    {
        if ((uint)x >= gW || (uint)y >= gH) return;
        int col = x >> 1;       // 0..79
        int row = y >> 2;       // 0..24
        int xs = x & 1;         // 0..1
        int ys = y & 3;         // 0..3
        int bit = ys*2 + xs;    // 0..7

        byte mask = (byte)(1 << bit);
        byte b = _semi[row, col];

        if (xor) b ^= mask;
        else if (set) b |= mask;
        else b &= (byte)~mask;

        _semi[row,col] = b;

        // Drawing graphics erases any text at this cell
        _chars[row,col] = ' ';

        // draw subcell pixels (each subcell is 1x1 pixel in the 160x100 domain)
        SetPixel160(x, y, ( (b & mask) != 0 ) ? fg : bg, false);
        FlushAll();
    }

    public void Line160(int x1, int y1, int x2, int y2, bool xor=false)
    {
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;
        while (true)
        {
            PSet160(x1, y1, true, xor);
            if (x1 == x2 && y1 == y2) break;
            e2 = 2 * err;
            if (e2 >= dy) { err += dy; x1 += sx; }
            if (e2 <= dx) { err += dx; y1 += sy; }
        }
    }

    // PUT@ (x1,y1)-(x2,y2), mask(10x12), mode
    public void PutRect10x12(int x1, int y1, bool[,] mask, bool xor)
    {
        int w = 10, h = 12;
        for (int yy=0; yy<h; yy++)
        {
            for (int xx=0; xx<w; xx++)
            {
                if (!mask[xx,yy]) continue;
                PSet160(x1+xx, y1+yy, true, xor);
            }
        }
    }
	public static class PC80Patterns
	{
		// rows: 12行ぶんの16bit（ushort）を並べる。ビット1=点灯。
		// lsbLeft=true なら LSB が左端、false なら MSB が左端（方言差に備えて切替可）
		public static bool[,] Decode10x12(ushort[] rows, bool lsbLeft = true)
		{
			if (rows == null || rows.Length < 12) throw new System.ArgumentException("rows must have 12 elements");
			bool[,] m = new bool[10, 12];
			for (int y = 0; y < 12; y++)
			{
				ushort r = rows[y];
				for (int x = 0; x < 10; x++)
				{
					int bit = lsbLeft ? x : (15 - x);
					m[x, y] = ((r >> bit) & 1) != 0;
				}
			}
			return m;
		}
	}
}
