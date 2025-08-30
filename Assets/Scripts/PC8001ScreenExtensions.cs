using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// PC8001Screen �� Compact ���� API �� (CLS/PSET/LINE/LOCATE/PRINT) ��ǉ�����݊��g���B
/// �\�Ȃ� PC8001Screen �����uClear/SetPixel/DrawLine/Locate/Print�v�������t���N�V�����ŌĂсA
/// ������Ȃ��ꍇ�͈��S�ȃt�H�[���o�b�N�iBresenham ���j�ŕ`�悵�܂��B
/// ��������邾���� MantrapTrue3D.cs �� screen.CLS()/PSET()/LINE()/LOCATE()/PRINT() ���ʂ�܂��B
/// </summary>
public static class PC8001ScreenExtensions
{
	// ���m�̃��\�b�h�����i�召��ʂȂ��j
	static readonly string[] CLS_NAMES = { "CLS", "Cls", "Clear", "CLEAR" };
	static readonly string[] PSET_NAMES = { "PSET", "PSet", "SetPixel", "Plot" };
	static readonly string[] LINE_NAMES = { "LINE", "Line", "DrawLine" };
	static readonly string[] LOCATE_NAMES = { "LOCATE", "Locate", "SetCursor", "Cursor" };
	static readonly string[] PRINT_NAMES = { "PRINT", "Print", "Write", "WriteText" };

	// �y���L���b�V��
	static MethodInfo miCLS, miPSET, miLINE, miLOCATE, miPRINT;

	// ��ʃT�C�Y�̐���i������� 640x400 ������j
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

	// ========== ���J�F�݊� API ==========
	public static void CLS(this PC8001Screen s)
	{
		miCLS ??= FindMethod(s, CLS_NAMES);
		if (miCLS != null) { miCLS.Invoke(s, null); return; }

		// �t�H�[���o�b�N�F�S�ʏ����i��=false �����œh��j
		int w = GuessWidth(s), h = GuessHeight(s);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				s.PSET(x, y, false, false);
	}

	public static void PSET(this PC8001Screen s, int x, int y, bool white, bool invert)
	{
		// ��\�I�ȃV�O�l�`�������Ɏ���
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

		// �Ō�̎�i�F����������Ζق��Ė����i�N���b�V�������Ȃ��j
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

		// �t�H�[���o�b�N�FBresenham �� PSET
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
		// ������Ȃ���Ζ����iPRINT �������W�������������邽�߁j
	}

	public static void PRINT(this PC8001Screen s, string text)
	{
		miPRINT ??= FindMethod(s, PRINT_NAMES, m =>
		{
			var ps = m.GetParameters();
			return ps.Length == 1 && ps[0].ParameterType == typeof(string);
		});
		if (miPRINT != null) { miPRINT.Invoke(s, new object[] { text }); }
		// ������Ȃ���Ζ���
	}
}
