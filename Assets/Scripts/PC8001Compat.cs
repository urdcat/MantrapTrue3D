
using System.Collections.Generic;
using UnityEngine;
using static PC8001Screen;

public static class PC8
{
    static PC8001Screen _scr;

    static Dictionary<int, Color32> _pal = new Dictionary<int, Color32> {
        {0, new Color32(0,0,0,255)},
        {1, new Color32(0,0,255,255)},
        {2, new Color32(255,0,0,255)},
        {3, new Color32(255,0,255,255)},
        {4, new Color32(0,255,0,255)},
        {5, new Color32(0,255,255,255)},
        {6, new Color32(255,255,0,255)},
        {7, new Color32(255,255,255,255)},
    };

    public static void Bind(PC8001Screen s) { _scr = s; }
    public static void COLOR(int fg, int bg) { _scr?.SetColor(_pal[Mathf.Clamp(fg,0,7)], _pal[Mathf.Clamp(bg,0,7)]); }
    public static void CLS() { _scr?.Cls(); }
    public static void LOCATE(int col, int row) { _scr?.Locate(col, row); }
    public static void PRINT(string s) { _scr?.Print(s); }

    public static void PSET(int x, int y, bool on=true, bool xor=false){ _scr?.PSet160(x,y,on,xor); }
    public static void LINE(int x1,int y1,int x2,int y2, bool xor=false){ _scr?.Line160(x1,y1,x2,y2,xor); }
    public static void PUT10x12(int x, int y, bool[,] mask, bool xor){ _scr?.PutRect10x12(x,y,mask,xor); }

    // INP: port 2/3/4/9 → bitmaskの仮実装（後で実機割当へ寄せる）
    public static int INP(int port) {
        int v = 0xFF;
        if (port == 4) {
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) v &= ~(1<<3);
            if (Input.GetKey(KeyCode.RightArrow)|| Input.GetKey(KeyCode.D)) v &= ~(1<<7);
            if (Input.GetKey(KeyCode.UpArrow)   || Input.GetKey(KeyCode.W)) v &= ~(1<<5);
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v &= ~(1<<1);
            if (Input.GetKey(KeyCode.Space)) v &= ~(1<<0);
        }
        if (port == 2 || port == 3 || port == 9) {
            if (Input.GetKey(KeyCode.Space)) v &= ~(1<<0);
        }
        return v;
    }

    // BEEP 簡易
    static AudioSource _beep;
    public static void BEEP() {
        if (_beep == null) {
            var go = new GameObject("PC8BEEP");
            Object.DontDestroyOnLoad(go);
            _beep = go.AddComponent<AudioSource>();
            _beep.playOnAwake = false;
        }
        _beep.PlayOneShot(AudioClip.Create("b", 8000/30, 1, 8000, false));
    }
	public static void PUT10x12_BITS(int x, int y, ushort[] rows, bool xor, bool lsbLeft = true)
	{
		if (_scr == null) return;
		var mask = PC80Patterns.Decode10x12(rows, lsbLeft);
		_scr.PutRect10x12(x, y, mask, xor);
	}
}
