using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// PC-8001 セミグラ表示にワイヤーフレーム迷路を投影する・FPS操作対応版
public class MantrapTrue3D : MonoBehaviour
{
	// ===== 依存 =====
	[SerializeField] private PC8001Screen screen;   // Inspector で割り当て（Awakeで自動探索もする）

	// ===== 画面/ビューポート =====
	const int SCR_W = 160, SCR_H = 100;             // PC-8001 セミグラ解像度
	const int VIEW_SIZE = 96;                        // 正方ビューポート
	const int VX0 = 8, VY0 = 2;                      // 左上
	static int VX1 => VX0 + VIEW_SIZE - 1;           // 右下
	static int VY1 => VY0 + VIEW_SIZE - 1;
	static int VCX => (VX0 + VX1) / 2;
	static int VCY => (VY0 + VY1) / 2;

	// ===== 迷路 =====
	const int B = 21, C = 21;                        // (x:0..B-1, y:0..C-1) 奇数推奨
	const int PASS = 0, WALL = 1;
	int[,] M = new int[B, C];

	// ===== カメラ／投影 =====
	Vector3 camPos;                                  // 物理の位置（衝突はこちら）
	float camYaw = 0f;                             // ラジアン。0=南(+Z)、+90=東(+X)、+180=北(-Z)、-90=西(-X)
	const float CAM_H = 0.5f;                        // 目線高さ
	const float CELL = 1.0f;                        // 1セル=1.0
	const float FOV_DEG = 60f;
	const float NEAR = 0.12f;
	float focal;                                     // 焦点距離（画素）

	// 「見た目だけ」カメラを少し後退させる（壁ベタ付き時の見やすさ向上）
	const float CAM_BACK = 0.14f;                    // 0.10〜0.16 あたりで好み
	Vector3 eyePos;                                  // 描画用の視点

	// ===== 操作モード =====
	[SerializeField] bool fpsMode = true;            // true:連続(FPS)操作 / false:旧グリッド操作

	// 連続(FPS)操作パラメータ
	const float MOVE_SPEED = 1.8f;                // セル/秒（前後）
	const float STRAFE_SPEED = 1.6f;                // セル/秒（左右）
	const float TURN_SPEED = 150f;                // 度/秒（キー押しっぱ）
	const float PLAYER_RADIUS = 0.28f;               // 円キャラ半径（壁スレスレ時の余白）

	// 旧グリッド操作（残しておく）
	bool isTurning = false, isMoving = false;
	const int TURN_FRAMES = 6, MOVE_FRAMES = 6;
	float moveCooldown = 0f;
	const float MOVE_REPEAT = 0.12f;

	// 方位（配列座標系：x=東+1, y=南+1）
	static readonly int[] DX4 = { 0, +1, 0, -1 };    // N,E,S,W
	static readonly int[] DY4 = { -1, 0, +1, 0 };
	int dir4 = 1;                                    // 初期方位：E
	int gx, gy;                                      // 表示用の格子位置（FPS時は camPos から追随）

	// ===== 1D オクルージョン（列ごとの 1/z 最大値だけで隠線） =====
	// 旧:
	// float[] zcol = new float[SCR_W];
	// const float EDGE_BIAS   = 1e-4f;
	// const float OCCL_EPS    = 2e-4f;

	// 新: 固定小数点 Q20.12 くらい
	const bool USE_1D_OCCL = true;

	// 新: Q22（≈×4,194,304）※Q24 でもOK
	const int ZFP_SHIFT = 22;
	const int ZFP_ONE = 1 << ZFP_SHIFT;

	[System.Runtime.CompilerServices.MethodImpl(
		System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	static int FP(float x) => (int)Mathf.Round(x * ZFP_ONE);

	// バイアスは「最低1 LSB」を保証（丸めゼロ防止）
	const float EDGE_BIAS_F = 0.00025f;   // ←大きめに
	const float OCCL_EPS_F = 0.00050f;   // ←大きめに
	static readonly int EDGE_BIAS_FP = Mathf.Max(1, FP(EDGE_BIAS_F));
	static readonly int OCCL_EPS_FP = Mathf.Max(1, FP(OCCL_EPS_F));

	// 1Dマスク
	int[] zcol = new int[SCR_W];

	// ===== ミニマップ =====
	bool showMini = true;
	int miniCell = 4;                                // 1マスのピクセル（3〜4）
	int miniRange = 5;                               // 片側セル数（可視範囲）

	// ===== 3D ジオメトリ（面=矩形、描画は稜線＋1Dマスク） =====
	struct Edge { public Vector3 a, b; public Edge(Vector3 A, Vector3 B) { a = A; b = B; } }
	List<Edge> edges = new List<Edge>();

	struct Face
	{
		public Vector3 v0, v1, v2, v3;               // 矩形（反時計回り）
		public Vector3 center, normal;               // 便宜的（主に構築時）
		public Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
		{
			v0 = a; v1 = b; v2 = c; v3 = d; center = (a + b + c + d) / 4f; normal = Vector3.zero;
		}
	}
	List<Face> faces = new List<Face>();

	// 共面シーム除去用キー
	struct EdgeKey
	{
		public int ax, ay, az, bx, by, bz;
		public EdgeKey(Vector3 a, Vector3 b)
		{
			ax = Mathf.RoundToInt(a.x * 2f); ay = Mathf.RoundToInt(a.y * 2f); az = Mathf.RoundToInt(a.z * 2f);
			bx = Mathf.RoundToInt(b.x * 2f); by = Mathf.RoundToInt(b.y * 2f); bz = Mathf.RoundToInt(b.z * 2f);
			if (ax > bx || (ax == bx && (ay > by || (ay == by && az > bz)))) { (ax, bx) = (bx, ax); (ay, by) = (by, ay); (az, bz) = (bz, az); }
		}
		public override int GetHashCode() => ax * 73856093 ^ ay * 19349663 ^ az * 83492791 ^ bx * 1299709 ^ by * 2750159 ^ bz * 4256249;
		public override bool Equals(object o)
		{
			if (!(o is EdgeKey e)) return false;
			return ax == e.ax && ay == e.ay && az == e.az && bx == e.bx && by == e.by && bz == e.bz;
		}
	}
	struct EdgeAccum { public int ncode; public int count; public bool diffNormal; }
	static int NormalCode(Vector3 n)
	{
		if (Mathf.Abs(n.x) > 0.5f) return n.x > 0 ? +1 : -1;   // ±X
		return n.z > 0 ? +2 : -2;                               // ±Z
	}

	string lastMoveReason = "idle";

	// ===== Unity =====
	void Awake()
	{
		if (screen == null)
		{
			screen = GetComponentInChildren<PC8001Screen>(true);
			if (screen == null) screen = FindObjectOfType<PC8001Screen>(true);
		}
		if (screen == null) { Debug.LogError("PC8001Screen が見つかりません"); enabled = false; return; }
		PC8.Bind(screen);

		focal = (VIEW_SIZE - 2) * 0.5f / Mathf.Tan(0.5f * Mathf.Deg2Rad * FOV_DEG);
	}

	IEnumerator Start()
	{
		PC8.CLS();

		GenerateMazeDFS();
		ClampToPassage(ref gx, ref gy);
		camPos = CellCenter(gx, gy) + new Vector3(0, CAM_H, 0);
		camYaw = WrapPI(YawFromDir(dir4));

		BuildEdgesFromMaze();
		Render3D();

		while (true)
		{
			if (Input.GetKeyDown(KeyCode.T)) fpsMode = !fpsMode;
			if (Input.GetKeyDown(KeyCode.M)) showMini = !showMini;
			if (Input.GetKeyDown(KeyCode.G))
			{
				GenerateMazeDFS();
				BuildEdgesFromMaze();
				ClampToPassage(ref gx, ref gy);
				camPos = CellCenter(gx, gy) + new Vector3(0, CAM_H, 0);
				camYaw = WrapPI(YawFromDir(dir4));
				Render3D();
			}

			if (fpsMode)
			{
				// --- 連続(FPS)モード ---
				float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);

				// 旋回（押しっぱで角速度加算）
				float yawVel = 0f;
				if (Input.GetKey(KeyCode.LeftArrow)) yawVel -= TURN_SPEED;
				if (Input.GetKey(KeyCode.RightArrow)) yawVel += TURN_SPEED;
				camYaw = WrapPI(camYaw + yawVel * Mathf.Deg2Rad * dt);

				// 前後・ストレーフ
				float fwdIn = 0f, strafeIn = 0f;
				if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) fwdIn += 1f;
				if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) fwdIn -= 1f;
				if (Input.GetKey(KeyCode.D)) strafeIn += 1f;
				if (Input.GetKey(KeyCode.A)) strafeIn -= 1f;

				Vector2 fwd2 = new Vector2(Mathf.Sin(camYaw), Mathf.Cos(camYaw)); // +Z 前
				Vector2 right2 = new Vector2(fwd2.y, -fwd2.x);
				Vector2 wish = fwdIn * fwd2 * MOVE_SPEED + strafeIn * right2 * STRAFE_SPEED;
				Vector3 delta = new Vector3(wish.x, 0, wish.y) * dt;

				camPos = CollideAndSlide(camPos, delta, PLAYER_RADIUS);

				// 表示用グリッド座標を追随
				gx = Mathf.Clamp(Mathf.RoundToInt(camPos.x), 0, B - 1);
				gy = Mathf.Clamp(Mathf.RoundToInt(camPos.z), 0, C - 1);
				lastMoveReason = (delta.sqrMagnitude > 0f) ? "moving" : "idle";

				Render3D();
			}
			else
			{
				// --- 旧グリッドモード ---
				moveCooldown = Mathf.Max(0f, moveCooldown - Time.deltaTime);
				if (!isTurning && !isMoving)
				{
					if (Input.GetKeyDown(KeyCode.LeftArrow)) StartCoroutine(Turn(-1));
					if (Input.GetKeyDown(KeyCode.RightArrow)) StartCoroutine(Turn(+1));

					int mv = 0;
					bool upHeld = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
					bool dnHeld = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
					if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || (upHeld && moveCooldown == 0f)) mv = +1;
					if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) || (dnHeld && moveCooldown == 0f)) mv = -1;
					if (mv != 0) { StartCoroutine(MoveStep(mv)); moveCooldown = MOVE_REPEAT; }
				}
			}

			yield return null;
		}
	}

	// ====== 描画 ======
	void Render3D()
	{
		PC8.CLS();
		DrawFrameOnly();

		// 見た目だけ少し後ろに引く
		Vector3 fwd = new Vector3(Mathf.Sin(camYaw), 0, Mathf.Cos(camYaw));
		eyePos = camPos - fwd * CAM_BACK;

		// 1D 深度マスクを構築（各 x 列の最近面 1/z）
		for (int x = VX0 + 1; x <= VX1 - 1; x++) zcol[x] = int.MinValue;

		foreach (var f in faces)
		{
			Vector3 a = ToView(f.v0), b = ToView(f.v1), c = ToView(f.v2), d = ToView(f.v3);

			// 背面除去
			Vector3 n = Vector3.Cross(b - a, d - a);
			if (Vector3.Dot(n, -a) <= 0f) continue;

			var poly = ClipConvexNear(new[] { a, b, c, d }, NEAR);
			if (poly == null || poly.Count < 3) continue;

			// 画面Xの範囲
			int minX = VX1, maxX = VX0;
			for (int i = 0; i < poly.Count; i++)
			{
				var p = poly[i];
				float invZ = 1f / p.z;
				int sx = VCX + Mathf.RoundToInt((p.x * focal) * invZ);
				minX = Mathf.Min(minX, sx);
				maxX = Mathf.Max(maxX, sx);
			}
			minX = Mathf.Clamp(minX - 1, VX0 + 1, VX1 - 1);
			maxX = Mathf.Clamp(maxX + 1, VX0 + 1, VX1 - 1);

			if (minX > maxX) continue;

			// 平面: nv·X + d0 = 0（ビュー空間）
			Vector3 nv = Vector3.Cross(b - a, c - a);
			float d0 = -Vector3.Dot(nv, a);
			if (Mathf.Abs(d0) < 1e-8f) continue; // 退避

			// invZ(x) = -(nv.x*rx + nv.z)/d0,  rx = (x - VCX)/focal
			// → invZ(x) = px * x + q の一次式
			float px_f = -(nv.x / (d0 * focal));                // 係数（実数）
			float q_f = -((-nv.x * VCX / focal + nv.z) / d0);  // 切片

			// 最左列の invZ を固定小数点へ
			float invZ_min = px_f * minX + q_f;
			int invZ_i = FP(invZ_min);
			int step_i = FP(px_f);        // 列ごとの増分
			for (int x = minX; x <= maxX; x++) { if (invZ_i > zcol[x]) zcol[x] = invZ_i; invZ_i += step_i; }
		}

		// 稜線を描く（1Dマスクを参照して可視部分のみ）
		foreach (var e in edges)
		{
			Vector3 av = ToView(e.a), bv = ToView(e.b);
			if (av.z <= NEAR && bv.z <= NEAR) continue;

			// 近面クリップ
			if (av.z <= NEAR || bv.z <= NEAR)
			{
				float t = (NEAR - av.z) / (bv.z - av.z);
				if (av.z <= NEAR) av = Vector3.Lerp(av, bv, t);
				else bv = Vector3.Lerp(bv, av, 1f - t);
			}

			float ia = 1f / av.z, ib = 1f / bv.z;
			int x0 = VCX + Mathf.RoundToInt((av.x * focal) * ia);
			int y0 = VCY - Mathf.RoundToInt((av.y * focal) * ia);
			int x1 = VCX + Mathf.RoundToInt((bv.x * focal) * ib);
			int y1 = VCY - Mathf.RoundToInt((bv.y * focal) * ib);

			int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
			//if (steps == 0)
			//{
			//	if (!USE_1D_OCCL || (x0 > VX0 && x0 < VX1 && ia + EDGE_BIAS >= zcol[x0] - OCCL_EPS))
			//		PC8.PSET(x0, y0, true, false);
			//	continue;
			//}

			float fx = x0, fy = y0, fz = ia;
			float sx = (x1 - x0) / (float)steps;
			float sy = (y1 - y0) / (float)steps;
			float sz = (ib - ia) / (float)steps;

			for (int i = 0; i <= steps; i++)
			{
				int xi = Mathf.RoundToInt(fx);
				int yi = Mathf.RoundToInt(fy);
				// ループ内：描画判定
				if (xi > VX0 && xi < VX1 && yi > VY0 && yi < VY1)
				{
					if (!USE_1D_OCCL)
					{
						PC8.PSET(xi, yi, true, false);
					}
					else
					{
						// 置換：稜線ループ内の描画判定
						int fz_fp = FP(fz); // 1/z を固定小数点化

						// 近傍3列の中央値（両隣が有効な時だけ）でヒステリシス
						int zmask = zcol[xi];
						if (xi > VX0 + 1 && xi < VX1 - 1)
						{
							int a = zcol[xi - 1], b = zcol[xi], c = zcol[xi + 1];
							if (a != int.MinValue && c != int.MinValue)
							{
								// median3
								if (a > b) { var t = a; a = b; b = t; }
								if (b > c) { var t = b; b = c; c = t; }
								if (a > b) { var t = a; a = b; b = t; }
								zmask = b;
							}
						}
					}
				}

				fx += sx; fy += sy; fz += sz;
			}
		}

		DrawStatus();
		if (showMini) DrawMiniMap();
	}

	void DrawFrameOnly()
	{
		PC8.LINE(VX0, VY0, VX1, VY0);
		PC8.LINE(VX1, VY0, VX1, VY1);
		PC8.LINE(VX1, VY1, VX0, VY1);
		PC8.LINE(VX0, VY1, VX0, VY0);
	}

	void DrawStatus()
	{
		int col = (VX1 + 2) / 2;
		int yawDeg = Mathf.RoundToInt(Wrap360(camYaw * Mathf.Rad2Deg));
		PC8.LOCATE(col, 1); PC8.PRINT($"X:{camPos.x,5:0.00}  Z:{camPos.z,5:0.00}");
		PC8.LOCATE(col, 3); PC8.PRINT($"Yaw:{yawDeg:000}");
		PC8.LOCATE(col, 5); PC8.PRINT(fpsMode ? "Mode:FPS" : "Mode:GRID");
		PC8.LOCATE(col, 7); PC8.PRINT("T:Mode  G:New  M:Mini");
	}

	// ====== ミニマップ ======
	void DrawMiniMap()
	{
		int side = (miniRange * 2 + 1) * miniCell;
		int x0 = Mathf.Clamp(SCR_W - side - 2, VX1 + 2, SCR_W - side - 2);
		int y0 = SCR_H - side - 2;

		// 枠
		PC8.LINE(x0 - 1, y0 - 1, x0 + side, y0 - 1);
		PC8.LINE(x0 + side, y0 - 1, x0 + side, y0 + side);
		PC8.LINE(x0 + side, y0 + side, x0 - 1, y0 + side);
		PC8.LINE(x0 - 1, y0 + side, x0 - 1, y0 - 1);

		// ブロック（行ラン描画）
		for (int dy = -miniRange; dy <= miniRange; dy++)
		{
			int my = gy - dy; // 上=北
			for (int oy = 0; oy < miniCell; oy++)
			{
				int yy = y0 + (dy + miniRange) * miniCell + oy;
				int sx = int.MinValue, ex = int.MinValue;
				for (int dx = -miniRange; dx <= miniRange; dx++)
				{
					int mx = gx + dx;
					bool wall = (mx < 0 || mx >= B || my < 0 || my >= C) ? true : (M[mx, my] == WALL);
					if (wall) { if (sx == int.MinValue) sx = dx; ex = dx; }
					bool atEnd = (dx == miniRange);
					if ((!wall || atEnd) && sx != int.MinValue)
					{
						if (atEnd && wall) ex = dx;
						int x1 = x0 + (sx + miniRange) * miniCell;
						int x2 = x0 + (ex + miniRange) * miniCell + (miniCell - 1);
						PC8.LINE(x1, yy, x2, yy);
						sx = ex = int.MinValue;
					}
				}
			}
		}

		// 自機矢印（連続Yaw）
		int px = x0 + miniRange * miniCell;
		int py = y0 + miniRange * miniCell;
		DrawPlayerArrow(px, py);
	}

	void DrawPlayerArrow(int px, int py)
	{
		int cx = px + miniCell / 2, cy = py + miniCell / 2;
		int sx = Mathf.RoundToInt(Mathf.Sin(camYaw));   // x
		int sy = -Mathf.RoundToInt(Mathf.Cos(camYaw));  // 上=北
		PC8.PSET(cx, cy, true, true);                   // 本体
		PC8.PSET(cx + sx, cy + sy, true, true);         // 先端
		PC8.PSET(cx + sy, cy - sx, true, true);         // 翼
		PC8.PSET(cx - sy, cy + sx, true, true);         // 翼
	}

	// ====== 幾何 ======
	// ワールド→ビュー（R_y(-yaw)）。z>0 が正面
	Vector3 ToView(Vector3 w)
	{
		Vector3 p = w - eyePos; // ←描画は eyePos 基準
		float cy = Mathf.Cos(camYaw), sy = Mathf.Sin(camYaw);
		float vx = cy * p.x - sy * p.z;
		float vz = sy * p.x + cy * p.z;
		return new Vector3(vx, p.y, vz);
	}

	// 近面での凸多角形クリップ（ビュー空間）
	List<Vector3> ClipConvexNear(IList<Vector3> src, float nearZ)
	{
		List<Vector3> cur = new List<Vector3>(src);
		List<Vector3> nxt = new List<Vector3>(cur.Count + 2);
		if (cur.Count == 0) return null;

		nxt.Clear();
		for (int i = 0; i < cur.Count; i++)
		{
			Vector3 A = cur[i], B = cur[(i + 1) % cur.Count];
			bool Ain = A.z > nearZ, Bin = B.z > nearZ;
			if (Ain && Bin)
			{
				nxt.Add(B);
			}
			else if (Ain && !Bin)
			{
				float t = (nearZ - A.z) / (B.z - A.z);
				nxt.Add(new Vector3(Mathf.Lerp(A.x, B.x, t), Mathf.Lerp(A.y, B.y, t), nearZ));
			}
			else if (!Ain && Bin)
			{
				float t = (nearZ - A.z) / (B.z - A.z);
				nxt.Add(new Vector3(Mathf.Lerp(A.x, B.x, t), Mathf.Lerp(A.y, B.y, t), nearZ));
				nxt.Add(B);
			}
		}
		return (nxt.Count >= 3) ? nxt : null;
	}

	// ====== 迷路→面/稜線 ======
	void TryAddWall(int nx, int ny, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
	{
		bool solid = (nx < 0 || nx >= B || ny < 0 || ny >= C) || (M[nx, ny] == WALL);
		if (!solid) return;
		faces.Add(new Face(a, b, c, d));
	}

	void BuildEdgesFromMaze()
	{
		faces.Clear();
		edges.Clear();

		// 通路セルの外周に面を立てる（ローカル座標）
		for (int y = 0; y < C; y++)
			for (int x = 0; x < B; x++)
			{
				if (M[x, y] != PASS) continue;

				// 左（x-0.5）: 法線 +X にしたい → ★頂点順を入れ替え
				TryAddWall(x - 1, y,
					new Vector3(x - 0.5f, 0, y + 0.5f), new Vector3(x - 0.5f, 0, y - 0.5f),
					new Vector3(x - 0.5f, 1, y - 0.5f), new Vector3(x - 0.5f, 1, y + 0.5f));

				// 右（x+0.5）: 法線 -X
				TryAddWall(x + 1, y,
					new Vector3(x + 0.5f, 0, y - 0.5f), new Vector3(x + 0.5f, 0, y + 0.5f),
					new Vector3(x + 0.5f, 1, y + 0.5f), new Vector3(x + 0.5f, 1, y - 0.5f));

				// 北（y-0.5）: 法線 +Z
				TryAddWall(x, y - 1,
					new Vector3(x - 0.5f, 0, y - 0.5f), new Vector3(x + 0.5f, 0, y - 0.5f),
					new Vector3(x + 0.5f, 1, y - 0.5f), new Vector3(x - 0.5f, 1, y - 0.5f));

				// 南（y+0.5）: 法線 -Z
				TryAddWall(x, y + 1,
					new Vector3(x + 0.5f, 0, y + 0.5f), new Vector3(x - 0.5f, 0, y + 0.5f),
					new Vector3(x - 0.5f, 1, y + 0.5f), new Vector3(x + 0.5f, 1, y + 0.5f));
			}

		// スケール＆法線
		for (int i = 0; i < faces.Count; i++)
		{
			var f = faces[i];
			f.v0 *= CELL; f.v1 *= CELL; f.v2 *= CELL; f.v3 *= CELL;
			f.center = (f.v0 + f.v1 + f.v2 + f.v3) / 4f;
			f.normal = Vector3.Cross(f.v1 - f.v0, f.v3 - f.v0).normalized;
			faces[i] = f;
		}

		// 共面シーム除去 → 輪郭線生成
		var acc = new Dictionary<EdgeKey, EdgeAccum>(faces.Count * 4);
		void Acc(Vector3 a, Vector3 b, int nc)
		{
			var k = new EdgeKey(a, b);
			if (!acc.TryGetValue(k, out var s)) { s.ncode = nc; s.count = 1; s.diffNormal = false; }
			else { if (s.ncode == nc) s.count++; else s.diffNormal = true; }
			acc[k] = s;
		}
		foreach (var f in faces)
		{
			int nc = NormalCode(f.normal);
			Acc(f.v0, f.v1, nc); Acc(f.v1, f.v2, nc); Acc(f.v2, f.v3, nc); Acc(f.v3, f.v0, nc);
		}
		foreach (var kv in acc)
		{
			var s = kv.Value;
			if (s.diffNormal || s.count == 1)
			{
				var k = kv.Key;
				Vector3 a = new Vector3(k.ax / 2f, k.ay / 2f, k.az / 2f);
				Vector3 b = new Vector3(k.bx / 2f, k.by / 2f, k.bz / 2f);
				edges.Add(new Edge(a, b));
			}
		}
	}

	// ====== 操作（グリッドモード用） ======
	float YawFromDir(int d) => Mathf.Atan2(DX4[d], DY4[d]); // S=0°, E=+90°, N=180°, W=-90°
	static float WrapPI(float r) => Mathf.Repeat(r + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;  // [-π,π)
	static float Wrap360(float deg) => Mathf.Repeat(deg, 360f);

	IEnumerator Turn(int sign) // 右=+1, 左=-1
	{
		if (isTurning) yield break;
		isTurning = true;
		try
		{
			int dirNext = (dir4 + (sign > 0 ? 1 : 3)) & 3;
			float yaw0 = Wrap360(camYaw * Mathf.Rad2Deg);
			float yaw1 = Wrap360(YawFromDir(dirNext) * Mathf.Rad2Deg);
			for (int f = 1; f <= TURN_FRAMES; f++)
			{
				float t = f / (float)TURN_FRAMES; t = t * t * (3f - 2f * t);
				camYaw = WrapPI(Mathf.LerpAngle(yaw0, yaw1, t) * Mathf.Deg2Rad);
				Render3D(); yield return null;
			}
			dir4 = dirNext;
			camYaw = WrapPI(YawFromDir(dir4));
			Render3D();
		}
		finally { isTurning = false; }
	}

	IEnumerator MoveStep(int step)   // +1前進 -1後退
	{
		if (isMoving) yield break;
		isMoving = true;
		try
		{
			int dx = DX4[dir4], dy = DY4[dir4];
			int nx = gx + dx * step;
			int ny = gy + dy * step;
			if ((uint)nx >= B || (uint)ny >= C || M[nx, ny] == WALL) { lastMoveReason = (M[nx, ny] == WALL ? "wall" : "oob"); yield break; }

			Vector3 from = camPos;
			Vector3 to = CellCenter(nx, ny) + new Vector3(0, CAM_H, 0);
			for (int f = 1; f <= MOVE_FRAMES; f++)
			{
				float t = f / (float)MOVE_FRAMES; t = t * t * (3f - 2f * t);
				camPos = Vector3.Lerp(from, to, t);
				Render3D(); yield return null;
			}
			gx = nx; gy = ny; camPos = to; lastMoveReason = "ok"; Render3D();
		}
		finally { isMoving = false; }
	}

	// ====== 迷路生成（DFS） ======
	void GenerateMazeDFS(int? seed = null)
	{
		for (int y = 0; y < C; y++) for (int x = 0; x < B; x++) M[x, y] = WALL;

		int cw = (B - 1) / 2, ch = (C - 1) / 2;
		bool[,] vis = new bool[cw, ch];
		System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

		int sx = cw - 1, sy = ch / 2;
		OpenCell(sx, sy); vis[sx, sy] = true;
		var st = new Stack<(int x, int y)>(); st.Push((sx, sy));

		while (st.Count > 0)
		{
			var (x, y) = st.Peek();
			var nb = new List<(int nx, int ny, int wx, int wy)>();

			void Add(int nx, int ny)
			{
				if (nx < 0 || nx >= cw || ny < 0 || ny >= ch || vis[nx, ny]) return;
				int gx0 = 2 * x + 1, gy0 = 2 * y + 1, gnx = 2 * nx + 1, gny = 2 * ny + 1;
				nb.Add((nx, ny, (gx0 + gnx) / 2, (gy0 + gny) / 2));
			}

			Add(x + 1, y);
			Add(x - 1, y);
			Add(x, y + 1);
			Add(x, y - 1);

			if (nb.Count == 0) { st.Pop(); continue; }
			var p = nb[rng.Next(nb.Count)];
			M[p.wx, p.wy] = PASS; OpenCell(p.nx, p.ny); vis[p.nx, p.ny] = true; st.Push((p.nx, p.ny));
		}
		M[1, 1] = PASS; M[B - 2, C - 2] = PASS;
	}
	void OpenCell(int cx, int cy) { M[2 * cx + 1, 2 * cy + 1] = PASS; }

	void ClampToPassage(ref int x, ref int y)
	{
		x = Mathf.Clamp(x, 0, B - 1);
		y = Mathf.Clamp(y, 0, C - 1);
		if (M[x, y] == PASS) return;
		for (int r = 1; r < B + C; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					int nx = x + dx, ny = y + dy;
					if ((uint)nx < B && (uint)ny < C && M[nx, ny] == PASS) { x = nx; y = ny; return; }
				}
	}

	Vector3 CellCenter(int x, int y) => new Vector3(x, 0, y);

	// ====== 円キャラ×格子壁 Collide & Slide ======
	Vector3 CollideAndSlide(Vector3 start, Vector3 delta, float radius)
	{
		Vector3 pos = start;

		const float MAX_STEP = 0.15f;
		int sub = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / MAX_STEP));
		Vector3 step = delta / sub;

		IEnumerable<(int x, int y)> Around(Vector3 p)
		{
			int cx = Mathf.FloorToInt(p.x);
			int cy = Mathf.FloorToInt(p.z);
			for (int y = cy - 2; y <= cy + 2; y++)
				for (int x = cx - 2; x <= cx + 2; x++)
					if ((uint)x < B && (uint)y < C && M[x, y] == WALL) yield return (x, y);
		}

		for (int s = 0; s < sub; s++)
		{
			Vector3 next = pos + step;

			// 2～3回回すと安定
			for (int iter = 0; iter < 3; iter++)
			{
				bool separated = true;

				foreach (var (wx, wy) in Around(next))
				{
					// 壁AABBを半径ぶんだけ膨らませたボックス
					float minX = (wx - 0.5f) - radius;
					float maxX = (wx + 0.5f) + radius;
					float minZ = (wy - 0.5f) - radius;
					float maxZ = (wy + 0.5f) + radius;

					// 侵入していなければスキップ
					if (next.x <= minX || next.x >= maxX || next.z <= minZ || next.z >= maxZ) continue;

					// 4方向の貫通量
					float pxL = next.x - minX;   // 左面へ押し戻す量（右から入った）
					float pxR = maxX - next.x;   // 右面へ押し戻す量（左から入った）
					float pzB = next.z - minZ;   // 下（北）面
					float pzT = maxZ - next.z;   // 上（南）面

					// X/Z どちらが浅いか
					float fixX = Mathf.Min(pxL, pxR);
					float fixZ = Mathf.Min(pzB, pzT);

					if (fixX < fixZ)
					{
						// Xだけ動かす（符号は小さい方の面に合わせる）
						next.x += (pxL < pxR) ? -pxL : +pxR;
					}
					else
					{
						next.z += (pzB < pzT) ? -pzB : +pzT;
					}

					separated = false;
				}

				if (separated) break;
			}

			// 外周クランプ（はみ出し抑止）
			next.x = Mathf.Clamp(next.x, radius, (B - 1) - radius);
			next.z = Mathf.Clamp(next.z, radius, (C - 1) - radius);

			pos = next;
		}

		return pos;
	}
}
