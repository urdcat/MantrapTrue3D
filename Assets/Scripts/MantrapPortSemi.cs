using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MantrapPortSemi : MonoBehaviour
{
	public PC8001Screen screen;

	// ---- 迷路サイズ（PC-8001のMantrap想定値に合わせて 21x21） ----
	const int B = 21;  // x方向のセル数（壁込みグリッド）
	const int C = 21;  // y方向のセル数（壁込みグリッド）
					   // M[x,y] : 1=壁, 0=通路
	int[,] M = new int[B, C];

	// 表示スケール（セミグラは 160x100）
	// 21 * 4 = 84 なので 4px/マスなら上下左右に余白を取れる
	const int CELL = 4;
	const int X0 = 8;   // 左余白
	const int Y0 = 12;  // 上余白

	// プレイヤー初期位置（右端手前, 中段あたりに寄せる）
	int px = B - 2, py = C / 2;

	bool pen = false;   // お絵描きデモはデフォルトOFF
	bool xor = false;

	IEnumerator Start()
	{
		PC8.Bind(screen);
		PC8.COLOR(7, 0);
		PC8.CLS();
		DrawHeader();

		GenerateMazeDFS();     // 迷路生成
		DrawMazeTopDown();     // 俯瞰表示
		DrawPlayerMarker();    // 位置マーカー

		while (true)
		{
			// 操作用ホットキー
			if (Input.GetKeyDown(KeyCode.G)) { GenerateMazeDFS(); RedrawAll(); }
			if (Input.GetKeyDown(KeyCode.C)) { PC8.CLS(); DrawHeader(); DrawMazeTopDown(); DrawPlayerMarker(); }
			if (Input.GetKeyDown(KeyCode.Z)) pen = !pen;
			if (Input.GetKeyDown(KeyCode.X)) xor = !xor;

			// 矢印でプレイヤーをマス移動（壁は通れない）
			int p = PC8.INP(4);
			int dx = 0, dy = 0;
			if ((p & (1 << 7)) == 0) dx = +1;
			if ((p & (1 << 3)) == 0) dx = -1;
			if ((p & (1 << 5)) == 0) dy = -1;
			if ((p & (1 << 1)) == 0) dy = +1;

			if ((dx | dy) != 0)
			{
				TryMove(dx, dy);
				RedrawAll();
			}

			yield return null;
		}
	}

	void RedrawAll()
	{
		PC8.CLS();
		DrawHeader();
		DrawMazeTopDown();
		DrawPlayerMarker();
	}

	void DrawHeader()
	{
		// タイトルは1行下に
		PC8.LOCATE(0, 1);
		PC8.PRINT("PC-8001 160x100  SEMIGFX  DEMO   G:new  C:clear  Arrows:move");
		// 外枠（タイトルを潰さない位置に）
		PC8.LINE(0, 2, 159, 2);
		PC8.LINE(159, 2, 159, 97);
		PC8.LINE(159, 97, 0, 97);
		PC8.LINE(0, 97, 0, 2);
	}

	// ==== 迷路生成（再帰的バックトラック＝DFS） ====
	void GenerateMazeDFS(int? seed = null)
	{
		// すべて壁で初期化
		for (int y = 0; y < C; y++)
			for (int x = 0; x < B; x++)
				M[x, y] = 1;

		int cw = (B - 1) / 2; // 論理セル幅（奇数座標）
		int ch = (C - 1) / 2; // 論理セル高さ

		bool[,] vis = new bool[cw, ch];
		System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

		// スタートは右端手前・中央付近（Mantrapの初期位置イメージ）
		int cx = cw - 1;
		int cy = ch / 2;

		// 実グリッド座標（奇数）へ開通
		OpenCell(cx, cy);

		Stack<(int x, int y)> st = new Stack<(int x, int y)>();
		vis[cx, cy] = true;
		st.Push((cx, cy));

		while (st.Count > 0)
		{
			var (x, y) = st.Peek();

			// 未訪問の近傍（4方向）
			var nb = new List<(int nx, int ny, int wx, int wy)>();
			void Add(int nx, int ny)
			{
				if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) return;
				if (vis[nx, ny]) return;
				// 壁位置（論理セル → 実グリッド）
				int gx = 2 * x + 1, gy = 2 * y + 1;
				int gnx = 2 * nx + 1, gny = 2 * ny + 1;
				int wx = (gx + gnx) / 2, wy = (gy + gny) / 2;
				nb.Add((nx, ny, wx, wy));
			}
			Add(x + 1, y);
			Add(x - 1, y);
			Add(x, y + 1);
			Add(x, y - 1);

			if (nb.Count == 0)
			{
				st.Pop();
				continue;
			}

			var pick = nb[rng.Next(nb.Count)];
			// 壁を壊す
			M[pick.wx, pick.wy] = 0;
			// 移動先セルを開ける
			OpenCell(pick.nx, pick.ny);

			vis[pick.nx, pick.ny] = true;
			st.Push((pick.nx, pick.ny));
		}

		// 入り口/出口（任意）：左端と右端を開ける
		M[1, 1] = 0;           // 左上寄り
		M[B - 2, C - 2] = 0;   // 右下寄り

		// プレイヤー初期位置を通路に寄せる
		px = B - 2; py = C / 2;
		if (M[px, py] == 1)
		{ // 壁なら近傍の通路を探す
			for (int r = 1; r < B + C; r++)
				for (int dy = -r; dy <= r; dy++)
					for (int dx = -r; dx <= r; dx++)
					{
						int nx = px + dx, ny = py + dy;
						if (nx <= 0 || nx >= B - 1 || ny <= 0 || ny >= C - 1) continue;
						if (M[nx, ny] == 0) { px = nx; py = ny; dx = dy = r = B + C; break; }
					}
		}
	}

	void OpenCell(int cx, int cy)
	{
		int gx = 2 * cx + 1;
		int gy = 2 * cy + 1;
		M[gx, gy] = 0;
	}

	// ==== 表示（俯瞰） ====
	void DrawMazeTopDown()
	{
		// 壁マスを塗りつぶしで描く
		for (int y = 0; y < C; y++)
		{
			for (int x = 0; x < B; x++)
			{
				if (M[x, y] == 1) FillCell(x, y);
			}
		}
	}

	void FillCell(int gx, int gy)
	{
		int sx = X0 + gx * CELL;
		int sy = Y0 + gy * CELL;
		for (int yy = 0; yy < CELL; yy++)
			for (int xx = 0; xx < CELL; xx++)
				PC8.PSET(sx + xx, sy + yy, true, false);
	}

	void DrawPlayerMarker()
	{
		int sx = X0 + px * CELL;
		int sy = Y0 + py * CELL;
		// 小さな3x3の白点
		for (int yy = -1; yy <= 1; yy++)
			for (int xx = -1; xx <= 1; xx++)
				if (sx + xx >= 0 && sx + xx < 160 && sy + yy >= 0 && sy + yy < 100)
					PC8.PSET(sx + xx, sy + yy, true, false);
	}

	void TryMove(int dx, int dy)
	{
		int nx = Mathf.Clamp(px + dx, 0, B - 1);
		int ny = Mathf.Clamp(py + dy, 0, C - 1);
		if (M[nx, ny] == 0) { px = nx; py = ny; }
	}
}
