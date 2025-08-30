using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Rendering;

public class MantrapView3D : MonoBehaviour
{
	public PC8001Screen screen;

	// 迷路
	const int B = 21, C = 21;            // 1=壁, 0=通路
	int[,] M = new int[B, C];

	// 位置と向き（0=N,1=E,2=S,3=W）
	int px, py, dir = 1;

	// === 画面レイアウト ===
	// 3D表示は 1:1 の正方形。右側はステータス用に空ける
	const int VIEW_SIZE = 96;         // 正方領域の一辺（<=100）
	const int VX0 = 8;                // 左上X
	const int VY0 = 2;                // 左上Y
	static int VX1 => VX0 + VIEW_SIZE - 1;
	static int VY1 => VY0 + VIEW_SIZE - 1;

	// ステータス領域（右側）
	static int SX0 => VX1 + 2;        // ステータスの左端
	const int SY0 = 2;

	// パースの深度
	const int MAXD = 6;
	readonly int[] xL = new int[MAXD + 1];
	readonly int[] xR = new int[MAXD + 1];
	readonly int[] yT = new int[MAXD + 1];
	readonly int[] yB = new int[MAXD + 1];

	void Awake() { PC8.Bind(screen); }

	IEnumerator Start()
	{
		PC8.COLOR(5, 0);
		PC8.CLS();

		GenerateMazeDFS();
		px = B - 2; py = C / 2; if (M[px, py] == 1) FindNearestPassage(ref px, ref py);

		PrecomputePerspective();   // 1:1 正方で算出
		Render3D();
		MiniMap();

		while (true)
		{
			bool need = false;

			//if( Input.GetKeyDown(KeyCode.LeftArrow)) { dir = (dir + 3) & 3; need = true; }
			//if (Input.GetKeyDown(KeyCode.RightArrow)) { dir = (dir + 1) & 3; need = true; }
			//if (Input.GetKeyDown(KeyCode.UpArrow)) need |= TryStep(+1);
			//if (Input.GetKeyDown(KeyCode.DownArrow)) need |= TryStep(-1);

			if (Input.GetKeyDown(KeyCode.G)) { GenerateMazeDFS(); FindNearestPassage(ref px, ref py); need = true; }
			if (Input.GetKeyDown(KeyCode.C)) { PC8.CLS(); need = true; }

			if (need)
			{
				Render3D();
				MiniMap();
			}
			if (!isTurning)
			{
				if (Input.GetKeyDown(KeyCode.LeftArrow)) StartCoroutine(TurnAnimWire(+1));
				if (Input.GetKeyDown(KeyCode.RightArrow)) StartCoroutine(TurnAnimWire(-1));

				// 前後移動は回転中は抑止（好みで）
				if (Input.GetKeyDown(KeyCode.UpArrow)) { if (TryStep(+1)) Render3D(); }
				if (Input.GetKeyDown(KeyCode.DownArrow)) { if (TryStep(-1)) Render3D(); }
			}
			yield return null;
		}
	}


	void DrawFrameOnly()
	{
		// 正方ビュー枠
		PC8.LINE(VX0, VY0, VX1, VY0);
		PC8.LINE(VX1, VY0, VX1, VY1);
		PC8.LINE(VX1, VY1, VX0, VY1);
		PC8.LINE(VX0, VY1, VX0, VY0);
	}
	// ============ 描画 ============
	void Render3D()
	{
		PC8.CLS();

		DrawFrameOnly();

		DrawStatus();

		// 壁フラグ配列（0..MAXD）
		bool[] L = new bool[MAXD + 1];
		bool[] R = new bool[MAXD + 1];
		bool[] F = new bool[MAXD + 1];

		for (int d = 0; d <= MAXD; d++)
		{
			int cx = px + d * DX(dir);
			int cy = py + d * DY(dir);
			L[d] = IsWall(cx + DXL(dir), cy + DYL(dir));   // 左側（区間 d の側壁）
			R[d] = IsWall(cx + DXR(dir), cy + DYR(dir));   // 右側
			F[d] = IsWall(px + d * DX(dir), py + d * DY(dir)); // 距離 d の正面
		}

		// 最初に当たる正面
		int front = -1;
		for (int d = 1; d <= MAXD; d++) { if (F[d]) { front = d; break; } }

		int limit = (front == -1) ? MAXD : front;

		// ==== 左右を「奥→手前」に描く ====
		DrawSideFromFarToNear(L, xL, limit, front);
		DrawSideFromFarToNear(R, xR, limit, front);

		// 視野ギリギリの近い壁処理　(本来切り分けなくても作れる気もする)
		if (L[0] )
		{ // 斜め線
			PC8.LINE(xL[0], yT[0], VX0, VY0);
			PC8.LINE(xL[0], yB[0], VX0, VY1);
		}
		else
		{ // 縦線と横線で] の形、ただしfront==1なら縦線は無し
			if( front>1) PC8.LINE(xL[0], yT[0], xL[0], yB[0]);
			PC8.LINE(VX0, yT[0], xL[0], yT[0]);
			PC8.LINE(VX0, yB[0], xL[0], yB[0]);
		}
		if( R[0] )
		{　// 斜め線
			PC8.LINE(xR[0], yT[0], VX1, VY0);
			PC8.LINE(xR[0], yB[0], VX1, VY1);
		}
		else
		{ // 縦線と横線で [ の形、ただしfront==1なら縦線は無し
			if ( front>1 ) PC8.LINE(xR[0], yT[0], xR[0], yB[0]);
			PC8.LINE(VX1, yT[0], xR[0], yT[0]);
			PC8.LINE(VX1, yB[0], xR[0], yB[0]);
		}

		// ==== 正面処理 ====
		if (front == -1)
		{
			// 視野に前面が無い：×型の消失点

			PC8.LINE(xL[MAXD], yT[MAXD], xR[MAXD], yB[MAXD]);
			PC8.LINE(xR[MAXD], yT[MAXD], xL[MAXD], yB[MAXD]);
			return;
		}
		else
		{
			int fpos = 0;
			if (front > 0) fpos = front - 1;

			// front の左右の開放状態（あなたの指摘通り L/R を使う）
			if (L[fpos])
			{ // 左側が閉じている、縦線を引く
				PC8.LINE(xL[fpos], yT[fpos], xL[fpos], yB[fpos]);
			}
			if (R[fpos])
			{ // 右側が閉じている、縦線を引く
				PC8.LINE(xR[fpos], yT[fpos], xR[fpos], yB[fpos]);
			}

			// 前面の壁（中央の横線のみ）
			PC8.LINE(xL[fpos], yT[fpos], xR[fpos], yT[fpos]);
			PC8.LINE(xL[fpos], yB[fpos], xR[fpos], yB[fpos]);
		}
	}
	void DrawSideFromFarToNear(bool[] S, int[] xPos, int limit, int front)
	{
		// d: 区間境界 1..limit を、奥→手前で処理
		for (int d = 1; d < limit; d++)
		{
			// 区間 d の座標（境界＝d、手前境界＝d-1）
			int xFar = xPos[d];
			int xNear = xPos[d-1];

			int yTfar = yT[d];
			int yBfar = yB[d];
			int yTnear = yT[d - 1];
			int yBnear = yB[d - 1];

			if (S[d] )
			{
				// 壁：薄い面（斜めの上下）
				PC8.LINE(xFar, yTfar, xNear, yTnear);
				PC8.LINE(xFar, yBfar, xNear, yBnear);
			}
			else
			{
				// 通路：その深度の水平ライン（上・下）

				PC8.LINE(xFar, yTfar, xNear, yTfar);
				PC8.LINE(xFar, yBfar, xNear, yBfar);
				if (d < limit -1 )
				{
					PC8.LINE(xFar, yTfar, xFar, yBfar);
				}
				PC8.LINE(xNear,yTnear, xNear, yBnear);
			}
		}
	}

	// MantrapView3D 内に追加
	const int TURN_FRAMES = 6;   // 4～8で好み
	const float TURN_GAMMA = 1.6f;// 深度ウェイト（大きいほど近景だけ強く回る）
	bool isTurning = false;

	const float PILLAR_IN_FADE = 0.70f; // 反対側柱の出現開始(0..1)

	// 角度 theta[rad], ease(0..1), sign(+1=左, -1=右)
	void DrawTurnFrame(float theta, float ease, int sign)
	{
		// ビュー領域だけ消去
		for (int y = VY0 + 1; y <= VY1 - 1; y++)
			for (int x = VX0 + 1; x <= VX1 - 1; x++)
				PC8.PSET(x, y, false, false);

		DrawFrameOnly();  // 枠だけ再描画

		// 疑似回転：x座標を「元→目標」を補間（yは既存のyT/yB）
		float ca = Mathf.Cos(theta), sa = Mathf.Sin(theta);
		int VCX = (VX0 + VX1) / 2;

		int[] xLr = new int[MAXD + 1];
		int[] xRr = new int[MAXD + 1];

		for (int d = 0; d <= MAXD; d++)
		{
			// 近いほど強く回る
			float w = Mathf.Pow(1f - (d / (float)MAXD), TURN_GAMMA);

			// 目標“完全回転”の左右比率（左右端を±1でY軸回転）
			// x' = x*cosθ + d*sinθ, z' = d*cosθ - x*sinθ, 画面偏差 ~ x'/z'
			float rLeft = ((-1f) * ca + d * sa) / (d * ca - (-1f) * sa);
			float rRight = ((+1f) * ca + d * sa) / (d * ca - (+1f) * sa);

			// その深度のスケール係数 K[d] を元の左右幅から推定
			float K = (xR[d] - VCX) * d;

			int xLrot = Mathf.Clamp(VCX + Mathf.RoundToInt(K * rLeft), VX0 + 1, VX1 - 1);
			int xRrot = Mathf.Clamp(VCX + Mathf.RoundToInt(K * rRight), VX0 + 1, VX1 - 1);

			float a = ease * w; // 補間係数
			xLr[d] = Mathf.RoundToInt(Mathf.Lerp(xL[d], xLrot, a));
			xRr[d] = Mathf.RoundToInt(Mathf.Lerp(xR[d], xRrot, a));

			// 左右の交差防止
			xLr[d] = Mathf.Clamp(xLr[d], VX0 + 1, xRr[d] - 1);
			xRr[d] = Mathf.Clamp(xRr[d], xLr[d] + 1, VX1 - 1);
		}

		// 1) 斜めの上下エッジ（薄い側壁）だけを全深度で描く：横線は描かない
		for (int d = 0; d < MAXD; d++)
		{
			PC8.LINE(xLr[d], yT[d], xLr[d + 1], yT[d + 1]); // 左上辺
			PC8.LINE(xLr[d], yB[d], xLr[d + 1], yB[d + 1]); // 左下辺
			PC8.LINE(xRr[d], yT[d], xRr[d + 1], yT[d + 1]); // 右上辺
			PC8.LINE(xRr[d], yB[d], xRr[d + 1], yB[d + 1]); // 右下辺
		}

		// 2) 柱（縦線）
		bool leftTurn = (sign > 0);
		bool rightTurn = !leftTurn;

		// 回転側の“手前柱”は常に描く（角の柱を見せる）
		if (leftTurn) PC8.LINE(xLr[0], yT[0], xLr[0], yB[0]);   // 左へ回転→左手前柱
		else PC8.LINE(xRr[0], yT[0], xRr[0], yB[0]);   // 右へ回転→右手前柱

		// 反対側の柱は終盤で立ち上げる（にゅっと出てくる）
		if (ease >= PILLAR_IN_FADE)
		{
			float k = (ease - PILLAR_IN_FADE) / (1f - PILLAR_IN_FADE); // 0→1
			int dEmer = Mathf.Clamp(Mathf.RoundToInt((1f - k) * 2f), 0, 2); // 近い方から出す

			if (leftTurn) PC8.LINE(xRr[dEmer], yT[dEmer], xRr[dEmer], yB[dEmer]);
			else PC8.LINE(xLr[dEmer], yT[dEmer], xLr[dEmer], yB[dEmer]);
		}

		// ステータスは固定で
		DrawStatus();
	}

	IEnumerator TurnAnimWire(int sign) // 左:+1, 右:-1
	{
		isTurning = true;

		for (int f = 1; f <= TURN_FRAMES; f++)
		{
			float t = f / (float)TURN_FRAMES;
			// 80年代の“ぬるっ”とした加減
			float ease = t * t * (3f - 2f * t);
			float theta = sign * (Mathf.PI * 0.5f) * ease;

			DrawTurnFrame(theta, ease, sign);
			yield return null;
		}

		// 方向を確定して通常描画へ
		dir = (dir + (sign > 0 ? 1 : 3)) & 3;
		Render3D();
		isTurning = false;
	}

	void DrawStatus()
	{
		// 文字の座標は「列/行」。SX0 はピクセルなので 2 で割ってだいたいの列に寄せる
		int col = Mathf.Clamp(SX0 / 2, 0, 79);
		int row = 1;

		// DIR を文字列で
		string dirStr = (dir == 0) ? "N" : (dir == 1) ? "E" : (dir == 2) ? "S" : "W";

		PC8.LOCATE(col, row);
		PC8.PRINT("X:" + px.ToString("00") + "  Y:" + py.ToString("00") + "  DIR:" + dirStr);

		PC8.LOCATE(col, row + 2);
		PC8.PRINT("G:NEW  C:CLEAR");

		PC8.LOCATE(col, row + 4);
		PC8.PRINT("ARROWS: MOVE/TURN");
	}


	// ============ パース ============
	void PrecomputePerspective()
	{
		// VIEW_SIZE の中央に収める。1:1なので水平/垂直で同じ縮尺
		int VCX = (VX0 + VX1) / 2;
		int VCY = (VY0 + VY1) / 2;

		float c = 0.6f;
		xL[0] = VX0+4;
		xR[0] = VX1-4;
		yT[0] = VY0+4;
		yB[0] = VY1-4;
		for (int d = 1; d <= MAXD; d++)
		{
			float s = 1f / (d + c);
			int half = Mathf.RoundToInt(((VIEW_SIZE) * 0.45f) * s)+3;

			xL[d] = Mathf.Clamp(VCX - half, VX0 + 1, VX1 - 1);
			xR[d] = Mathf.Clamp(VCX + half, VX0 + 1, VX1 - 1);
			yT[d] = Mathf.Clamp(VCY - half, VY0 + 1, VY1 - 1);
			yB[d] = Mathf.Clamp(VCY + half, VY0 + 1, VY1 - 1);
		}
	}

	// ============ 入力/迷路 ============

	bool TryStep(int sign)
	{
		int nx = px + sign * DX(dir);
		int ny = py + sign * DY(dir);
		if (!IsWall(nx, ny)) { px = nx; py = ny; return true; }
		return false;
	}

	bool IsWall(int x, int y)
	{
		if (x < 0 || x >= B || y < 0 || y >= C) return true;
		return M[x, y] == 1;
	}

	static int DX(int d) => (d == 1 ? +1 : (d == 3 ? -1 : 0));
	static int DY(int d) => (d == 0 ? -1 : (d == 2 ? +1 : 0));
	static int DXL(int d) => (d == 0 ? -1 : (d == 1 ? 0 : (d == 2 ? +1 : 0)));
	static int DYL(int d) => (d == 0 ? 0 : (d == 1 ? -1 : (d == 2 ? 0 : +1)));
	static int DXR(int d) => -DXL(d);
	static int DYR(int d) => -DYL(d);

	void GenerateMazeDFS(int? seed = null)
	{
		for (int y = 0; y < C; y++) for (int x = 0; x < B; x++) M[x, y] = 1;
		int cw = (B - 1) / 2, ch = (C - 1) / 2;
		bool[,] vis = new bool[cw, ch];
		System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
		int sx = cw - 1, sy = ch / 2;
		OpenCell(sx, sy); vis[sx, sy] = true;
		var st = new Stack<(int x, int y)>();
		st.Push((sx, sy));
		while (st.Count > 0)
		{
			var (x, y) = st.Peek();
			var nb = new List<(int nx, int ny, int wx, int wy)>();
			void Add(int nx, int ny)
			{
				if (nx < 0 || nx >= cw || ny < 0 || ny >= ch || vis[nx, ny]) return;
				int gx = 2 * x + 1, gy = 2 * y + 1, gnx = 2 * nx + 1, gny = 2 * ny + 1;
				nb.Add((nx, ny, (gx + gnx) / 2, (gy + gny) / 2));
			}
			Add(x + 1, y); Add(x - 1, y); Add(x, y + 1); Add(x, y - 1);
			if (nb.Count == 0) { st.Pop(); continue; }
			var pick = nb[rng.Next(nb.Count)];
			M[pick.wx, pick.wy] = 0; OpenCell(pick.nx, pick.ny);
			vis[pick.nx, pick.ny] = true; st.Push((pick.nx, pick.ny));
		}
		M[1, 1] = 0; M[B - 2, C - 2] = 0;
	}
	void OpenCell(int cx, int cy) { M[2 * cx + 1, 2 * cy + 1] = 0; }

	void FindNearestPassage(ref int x, ref int y)
	{
		for (int r = 0; r < B + C; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					int nx = x + dx, ny = y + dy;
					if (nx <= 0 || nx >= B - 1 || ny <= 0 || ny >= C - 1) continue;
					if (M[nx, ny] == 0) { x = nx; y = ny; return; }
				}
	}

	void MiniMap()
	{
		for (int yp = -2; yp < 2; yp++)
		{
			for (int xp = -2; xp < 2; xp++)
			{
				if( IsWall( px + xp, py + yp) )
				{
					int mx = 130 + xp * 3;
					int my = 80 + yp * 3;
					PC8.LINE( mx, my,mx +2,my );
					PC8.LINE( mx, my + 1, mx + 2, my + 1);
					PC8.LINE( mx, my + 2, mx + 2, my + 2);
				}
			}
		}
	}
}
