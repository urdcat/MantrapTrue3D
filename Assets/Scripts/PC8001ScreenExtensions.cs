using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// PC8001Screen に Compact 風の API 名 (CLS/PSET/LINE/LOCATE/PRINT) を追加する互換拡張。
/// 可能なら PC8001Screen が持つ「Clear/SetPixel/DrawLine/Locate/Print」等をリフレクションで呼び、
/// 見つからない場合は安全なフォールバック（Bresenham 等）で描画します。
/// これを入れるだけで MantrapTrue3D.cs の screen.CLS()/PSET()/LINE()/LOCATE()/PRINT() が通ります。
/// </summary>
public static class PC8001ScreenExtensions
{
	// 既知のメソッド名候補（大小区別なし）
	static readonly string[] CLS_NAMES = { "CLS", "Cls", "Clear", "CLEAR" };
	static readonly string[] PSET_NAMES = { "PSET", "PSet", "SetPixel", "Plot" };
	static readonly string[] LINE_NAMES = { "LINE", "Line", "DrawLine" };
	static readonly string[] LOCATE_NAMES = { "LOCATE", "Locate", "SetCursor", "Cursor" };
	static readonly string[] PRINT_NAMES = { "PRINT", "Print", "Write", "WriteText" };

	// 軽いキャッシュ
	static MethodInfo miCLS, miPSET, miLINE, miLOCATE, miPRINT;

	// 画面サイズの推定（無ければ 640x400 を既定）
	static int GuessWidth(object s) => GetIntProp(s, "Width", "PixelWidth", "TexWidth", "W") ?? 640;
	static int GuessHeight(object s) => GetIntProp(s, "Height", "PixelHeight", "TexHeight", "H") ?? 400;
	static int? GetIntProp(object o, params string[] names)
	{
		var t = o.GetType();
		foreach (var n in names)
		{
			var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
			if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(o);
			var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
			if (f != null && f.FieldType == typeof(int)) return (int)f.GetValue(o);
		}
		return null;
	}

	static MethodInfo FindMethod(object o, string[] names, Func<MethodInfo, bool> ok = null)
	{
		var t = o.GetType();
		var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public);
		foreach (var cand in names)
		{
			var mlist = methods.Where(m => string.Equals(m.Name, cand, StringComparison.OrdinalIgnoreCase));
			if (ok != null) mlist = mlist.Where(ok);
			var m = mlist.FirstOrDefault();
			if (m != null) return m;
		}
		return null;
	}

	// ========== 公開：互換 API ==========
	public static void CLS(this PC8001Screen s)
	{
		miCLS ??= FindMethod(s, CLS_NAMES);
		if (miCLS != null) { miCLS.Invoke(s, null); return; }

		// フォールバック：全面消去（白=false 相当で塗る）
		int w = GuessWidth(s), h = GuessHeight(s);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				s.PSET(x, y, false, false);
	}

	public static void PSET(this PC8001Screen s, int x, int y, bool white, bool invert)
	{
		// 代表的なシグネチャを順に試す
		miPSET ??= FindMethod(s, PSET_NAMES, m =>
		{
			var ps = m.GetParameters();
			if (ps.Length == 4 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int)
							   && ps[2].ParameterType == typeof(bool) && ps[3].ParameterType == typeof(bool)) return true;
			if (ps.Length == 3 && ps[2].ParameterType == typeof(bool)) return true;
			if (ps.Length == 3 && ps[2].ParameterType == typeof(Color32)) return true;
			if (ps.Length == 3 && ps[2].ParameterType == typeof(Color)) return true;
			return false;
		});

		if (miPSET != null)
		{
			var ps = miPSET.GetParameters();
			if (ps.Length == 4) { miPSET.Invoke(s, new object[] { x, y, white, invert }); return; }
			if (ps.Length == 3 && ps[2].ParameterType == typeof(bool)) { miPSET.Invoke(s, new object[] { x, y, white }); return; }
			if (ps.Length == 3 && ps[2].ParameterType == typeof(Color32)) { miPSET.Invoke(s, new object[] { x, y, white ? (Color32)Color.white : (Color32)Color.black }); return; }
			if (ps.Length == 3 && ps[2].ParameterType == typeof(Color)) { miPSET.Invoke(s, new object[] { x, y, white ? Color.white : Color.black }); return; }
		}

		// 最後の手段：何も無ければ黙って無視（クラッシュさせない）
	}

	public static void LINE(this PC8001Screen s, int x0, int y0, int x1, int y1)
	{
		miLINE ??= FindMethod(s, LINE_NAMES, m =>
		{
			var ps = m.GetParameters();
			return ps.Length == 4 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int)
								&& ps[2].ParameterType == typeof(int) && ps[3].ParameterType == typeof(int);
		});

		if (miLINE != null) { miLINE.Invoke(s, new object[] { x0, y0, x1, y1 }); return; }

		// フォールバック：Bresenham で PSET
		int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
		int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
		int err = dx + dy, cx = x0, cy = y0;
		while (true)
		{
			s.PSET(cx, cy, true, false);
			if (cx == x1 && cy == y1) break;
			int e2 = 2 * err;
			if (e2 >= dy) { err += dy; cx += sx; }
			if (e2 <= dx) { err += dx; cy += sy; }
		}
	}

	public static void LOCATE(this PC8001Screen s, int col, int row)
	{
		miLOCATE ??= FindMethod(s, LOCATE_NAMES, m =>
		{
			var ps = m.GetParameters();
			return ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int);
		});
		if (miLOCATE != null) { miLOCATE.Invoke(s, new object[] { col, row }); }
		// 見つからなければ無視（PRINT 側が座標を持つ実装もあるため）
	}

	public static void PRINT(this PC8001Screen s, string text)
	{
		miPRINT ??= FindMethod(s, PRINT_NAMES, m =>
		{
			var ps = m.GetParameters();
			return ps.Length == 1 && ps[0].ParameterType == typeof(string);
		});
		if (miPRINT != null) { miPRINT.Invoke(s, new object[] { text }); }
		// 見つからなければ無視
	}
}
