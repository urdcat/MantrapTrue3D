using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MantrapTrue3D : MonoBehaviour
{
	// === �O���Q�ƁiPC8001Screen �̂݁j ===
	public PC8001Screen screen;

	// === ��ʃ��C�A�E�g ===
	const int VIEW_W = 640;
	const int VIEW_H = 400;
	const int VX0 = 20, VY0 = 16;        // �r���[�t���[������
	const int VX1 = 620, VY1 = 384;      // �r���[�t���[���E��
	static int VCX => (VX0 + VX1) >> 1;
	static int VCY => (VY0 + VY1) >> 1;

	// === �J����/���e ===
	const float FOV_DEG = 60f;           // �cFOV
	float focal;                          // �œ_�����i�s�N�Z���j
	const float NEAR = 0.10f;
	const float WALL_H = 1.0f;
	const float CAM_H = 0.5f;

	// === ���H ===
	public int mazeW = 31;               // �����
	public int mazeH = 31;
	int[,] M;                             // 0=�ʘH / 1=��
	const int PASS = 0, WALL = 1;

	// === ���C���[�p�Ő� ===
	struct Edge { public Vector3 a, b; public Edge(Vector3 A, Vector3 B) { a = A; b = B; } }
	List<Edge> edges = new List<Edge>();

	// === Z�o�b�t�@�i1/z�j ===
	float[,] zbuf = new float[VIEW_H, VIEW_W];
	const float EDGE_BIAS = 2e-4f;       // �����킸���Ɏ�O��
	const float Z_EPS = 3e-4f;       // ���̂Ԃ񂾂���O�Ȃ����`��

	// === �v���C���[ ===
	int gx, gy;                           // �}�X���W
	int dir4;                             // 0=N,1=E,2=S,3=W
	Vector3 camPos;
	float camYaw;                         // ���W�A��
	const float TAU = Mathf.PI * 2f;
	static float NormalizeYaw(float a) => Mathf.Repeat(a, TAU);
	static readonly int[] DX4 = { 0, +1, 0, -1 };
	static readonly int[] DY4 = { -1, 0, +1, 0 };
	static float YawFromDir(int d) => (d == 0 ? Mathf.PI : d == 1 ? -Mathf.PI / 2f : d == 2 ? 0f : +Mathf.PI / 2f);

	// === ���́E�ړ� ===
	bool isTurning = false;
	bool isMoving = false;
	public int TURN_FRAMES = 10;         // ��]�A�j��
	const float MOVE_TIME = 0.14f;      // 1�}�X�ړ�����
	bool showMini = true;

	// === ���[�e�B���e�B ===
	Vector3 CellCenter(int x, int y) => new Vector3(x + 0.5f, 0, y + 0.5f);

	void Awake()
	{
		QualitySettings.vSyncCount = 1;   // VSync
		Application.targetFrameRate = 30; // ���삵�₷��fps

		float fov = FOV_DEG * Mathf.Deg2Rad;
		focal = ((VY1 - VY0) - 2) * 0.5f / Mathf.Tan(fov * 0.5f);
	}

	void Start()
	{
		NewMaze();

		// �����ʒu�i�ŏ��Ɍ��������ʘH�j
		for (int y = 1; y < mazeH - 1; y++)
		{
			bool done = false;
			for (int x = 1; x < mazeW - 1; x++)
			{
				if (M[x, y] == PASS) { gx = x; gy = y; done = true; break; }
			}
			if (done) break;
		}
		dir4 = 2; // S ����
		camYaw = NormalizeYaw(YawFromDir(dir4));
		camPos = CellCenter(gx, gy) + new Vector3(0, CAM_H, 0);

		StartCoroutine(MainLoop());
	}

	IEnumerator MainLoop()
	{
		Render3D();
		while (true)
		{
			if (!isTurning && !isMoving)
			{
				// ��]�F�E=+1�i���v����Yaw�����j�A��=-1
				if (Input.GetKeyDown(KeyCode.LeftArrow)) StartCoroutine(Turn(-1));
				if (Input.GetKeyDown(KeyCode.RightArrow)) StartCoroutine(Turn(+1));

				// ����ړ��i�������ϖ����j
				if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
				{
					if (CanStep(+1, out int nx, out int ny)) StartCoroutine(MoveLinear(nx, ny));
				}
				if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
				{
					if (CanStep(-1, out int nx, out int ny)) StartCoroutine(MoveLinear(nx, ny));
				}

				if (Input.GetKeyDown(KeyCode.G)) { NewMaze(); Render3D(); }
				if (Input.GetKeyDown(KeyCode.M)) { showMini = !showMini; Render3D(); }
			}
			yield return null;
		}
	}

	// === ���H�������Ő��\�z ===
	void NewMaze()
	{
		M = new int[mazeW, mazeH];
		for (int y = 0; y < mazeH; y++) for (int x = 0; x < mazeW; x++) M[x, y] = WALL;

		System.Random rng = new System.Random();
		int sx = 1, sy = 1;
		Carve(sx, sy, rng);
		BuildEdgesFromMaze();
	}

	void Carve(int x, int y, System.Random rng)
	{
		M[x, y] = PASS;
		int[] dir = { 0, 1, 2, 3 };
		for (int i = 0; i < 4; i++) { int j = rng.Next(4); (dir[i], dir[j]) = (dir[j], dir[i]); }
		foreach (int d in dir)
		{
			int dx = (d == 1 ? +2 : d == 3 ? -2 : 0);
			int dy = (d == 2 ? +2 : d == 0 ? -2 : 0);
			int nx = x + dx, ny = y + dy;
			if (nx <= 0 || nx >= mazeW - 1 || ny <= 0 || ny >= mazeH - 1) continue;
			if (M[nx, ny] == WALL)
			{
				M[x + dx / 2, y + dy / 2] = PASS;
				Carve(nx, ny, rng);
			}
		}
	}

	void BuildEdgesFromMaze()
	{
		edges.Clear();
		for (int y = 0; y < mazeH; y++)
			for (int x = 0; x < mazeW; x++)
			{
				if (M[x, y] != PASS) continue;

				// �����E�i�Oor�ǁj
				if (x - 1 < 0 || M[x - 1, y] == WALL)
					AddQuadEdges(new Vector3(x, 0, y), new Vector3(x, 0, y + 1),
								 new Vector3(x, 1, y + 1), new Vector3(x, 1, y));
				// �E���E
				if (x + 1 >= mazeW || M[x + 1, y] == WALL)
					AddQuadEdges(new Vector3(x + 1, 0, y + 1), new Vector3(x + 1, 0, y),
								 new Vector3(x + 1, 1, y), new Vector3(x + 1, 1, y + 1));
				// ��i�k�j
				if (y - 1 < 0 || M[x, y - 1] == WALL)
					AddQuadEdges(new Vector3(x + 1, 0, y), new Vector3(x, 0, y),
								 new Vector3(x, 1, y), new Vector3(x + 1, 1, y));
				// ���i��j
				if (y + 1 >= mazeH || M[x, y + 1] == WALL)
					AddQuadEdges(new Vector3(x, 0, y + 1), new Vector3(x + 1, 0, y + 1),
								 new Vector3(x + 1, 1, y + 1), new Vector3(x, 1, y + 1));
			}
	}

	void AddQuadEdges(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
	{
		edges.Add(new Edge(a, b));
		edges.Add(new Edge(b, c));
		edges.Add(new Edge(c, d));
		edges.Add(new Edge(d, a));
	}

	// === ���͌n ===
	bool CanStep(int step, out int nx, out int ny)
	{
		nx = gx + DX4[dir4] * step;
		ny = gy + DY4[dir4] * step;
		if ((uint)nx >= (uint)mazeW || (uint)ny >= (uint)mazeH) return false;
		return M[nx, ny] == PASS;
	}

	IEnumerator MoveLinear(int nx, int ny)
	{
		isMoving = true;
		Vector3 p0 = camPos;
		Vector3 p1 = CellCenter(nx, ny) + new Vector3(0, CAM_H, 0);

		for (float t = 0; t < 1f;)
		{
			t += Time.deltaTime / MOVE_TIME;
			float e = Mathf.Clamp01(t); e = e * e * (3 - 2 * e);
			camPos = Vector3.Lerp(p0, p1, e);
			Render3D();
			yield return null;
		}
		gx = nx; gy = ny; camPos = p1;
		Render3D();
		isMoving = false;
	}

	IEnumerator Turn(int sign) // +1=�E�i�����j, -1=���i�����j
	{
		isTurning = true;
		try
		{
			float y0 = NormalizeYaw(camYaw);
			float delta = sign * (Mathf.PI / 2f);
			for (int f = 1; f <= TURN_FRAMES; f++)
			{
				float t = f / (float)TURN_FRAMES; t = t * t * (3 - 2 * t);
				camYaw = NormalizeYaw(y0 + delta * t); // �P��
				Render3D();
				yield return null;
			}
			dir4 = (dir4 + (sign > 0 ? 1 : 3)) & 3;
			camYaw = NormalizeYaw(YawFromDir(dir4));   // �X�i�b�v
			Render3D();
		}
		finally { isTurning = false; }
	}

	// === �����_�����O ===
	void Render3D()
	{
		// �S����
		screen.CLS();

		// �g
		DrawFrameOnly();

		// Z������
		for (int y = VY0 + 1; y <= VY1 - 1; y++)
			for (int x = VX0 + 1; x <= VX1 - 1; x++)
				zbuf[y, x] = float.NegativeInfinity;

		// ��v���p�X�iY�����q�b�g�̂ݔ��œh��j
		PrepassColumnZ(paintOnlyIfYWall: true);

		// �Ő��iZ�͏��������Ȃ��j
		foreach (var e in edges)
		{
			Vector3 av = ToView(e.a), bv = ToView(e.b);
			if (av.z <= NEAR && bv.z <= NEAR) continue;

			// �߃N���b�v
			if (av.z <= NEAR || bv.z <= NEAR)
			{
				float t = (NEAR - av.z) / (bv.z - av.z);
				if (av.z <= NEAR) av = Vector3.Lerp(av, bv, t);
				else bv = Vector3.Lerp(bv, av, 1f - t);
			}

			float invZa = 1f / av.z + EDGE_BIAS;
			float invZb = 1f / bv.z + EDGE_BIAS;

			int x0 = VCX + Mathf.RoundToInt((av.x * focal) * invZa);
			int y0 = VCY - Mathf.RoundToInt((av.y * focal) * invZa);
			int x1 = VCX + Mathf.RoundToInt((bv.x * focal) * invZb);
			int y1 = VCY - Mathf.RoundToInt((bv.y * focal) * invZb);

			DrawLineZ(x0, y0, invZa, x1, y1, invZb, PlotLine);
		}

		DrawStatus();
		if (showMini) DrawMiniMap();
	}

	// ���E���r���[
	Vector3 ToView(Vector3 w)
	{
		Vector3 p = w - camPos;
		float cy = Mathf.Cos(camYaw), sy = Mathf.Sin(camYaw);
		return new Vector3(cy * p.x + sy * p.z, p.y, -sy * p.x + cy * p.z);
	}

	// === �J����Z�v���p�X�iWolf3D���j ===
	void PrepassColumnZ(bool paintOnlyIfYWall = true)
	{
		for (int x = VX0 + 1; x <= VX1 - 1; x++)
		{
			float ndc = (x - VCX) / focal;      // �J������Ԃ� z=1
			float cy = Mathf.Cos(camYaw), sy = Mathf.Sin(camYaw);
			float dirX = cy * ndc - sy * 1f;
			float dirZ = sy * ndc + cy * 1f;

			float posX = camPos.x, posZ = camPos.z;
			int mapX = Mathf.FloorToInt(posX);
			int mapY = Mathf.FloorToInt(posZ);

			int stepX = (dirX < 0f) ? -1 : 1;
			int stepY = (dirZ < 0f) ? -1 : 1;

			float deltaX = (dirX == 0f) ? 1e30f : Mathf.Abs(1f / dirX);
			float deltaY = (dirZ == 0f) ? 1e30f : Mathf.Abs(1f / dirZ);

			float sideDistX = (dirX < 0f) ? (posX - mapX) * deltaX : (mapX + 1f - posX) * deltaX;
			float sideDistY = (dirZ < 0f) ? (posZ - mapY) * deltaY : (mapY + 1f - posZ) * deltaY;

			bool hit = false; int side = 0;
			for (int it = 0; it < mazeW + mazeH + 4; it++)
			{
				if (sideDistX < sideDistY) { sideDistX += deltaX; mapX += stepX; side = 0; }
				else { sideDistY += deltaY; mapY += stepY; side = 1; }

				if (mapX < 0 || mapX >= mazeW || mapY < 0 || mapY >= mazeH || M[mapX, mapY] == WALL) { hit = true; break; }
			}
			if (!hit) continue;

			// ���������i����␳�ς݁j
			float t = (side == 0)
				? (mapX - posX + (1 - stepX) * 0.5f) / dirX   // X�ʃq�b�g��Y������
				: (mapY - posZ + (1 - stepY) * 0.5f) / dirZ;  // Y�ʃq�b�g
			if (t < NEAR) t = NEAR;

			float invZ = 1f / t;
			int yTop = VCY - Mathf.RoundToInt(((WALL_H - CAM_H) * focal) * invZ);
			int yBottom = VCY + Mathf.RoundToInt((CAM_H * focal) * invZ);
			yTop = Mathf.Clamp(yTop, VY0 + 1, VY1 - 1);
			yBottom = Mathf.Clamp(yBottom, VY0 + 1, VY1 - 1);
			if (yTop > yBottom) { var tmp = yTop; yTop = yBottom; yBottom = tmp; }

			bool paint = (!paintOnlyIfYWall) || (side == 0);

			for (int y = yTop; y <= yBottom; y++)
			{
				if (invZ > zbuf[y, x])
				{
					zbuf[y, x] = invZ;                    // Z�����ێ�
					if (paint) screen.PSET(x, y, true, false); // Y�����ǂ����h��
				}
			}
		}
	}

	// === ���`��i1/z��ԕt���EZ�͏��������Ȃ��j ===
	delegate void PixelPlotter(int x, int y, float invZ);

	void DrawLineZ(int x0, int y0, float z0, int x1, int y1, float z1, PixelPlotter plotter)
	{
		int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
		int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
		int err = dx + dy;

		int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
		if (n == 0) { plotter(x0, y0, z0); return; }

		float dz = (z1 - z0) / Mathf.Max(1, n);
		float z = z0;

		int cx = x0, cy = y0;
		while (true)
		{
			plotter(cx, cy, z);
			if (cx == x1 && cy == y1) break;

			int e2 = 2 * err;
			if (e2 >= dy) { err += dy; cx += sx; z += dz; }
			if (e2 <= dx) { err += dx; cy += sy; }
		}
	}

	void PlotLine(int x, int y, float invZ)
	{
		if (x <= VX0 || x >= VX1 || y <= VY0 || y >= VY1) return;
		if (invZ >= zbuf[y, x] + Z_EPS)
			screen.PSET(x, y, true, false);
	}

	// === �t���[�� & UI ===
	void DrawFrameOnly()
	{
		screen.LINE(VX0, VY0, VX1, VY0);
		screen.LINE(VX1, VY0, VX1, VY1);
		screen.LINE(VX1, VY1, VX0, VY1);
		screen.LINE(VX0, VY1, VX0, VY0);
	}

	void DrawStatus()
	{
		int colPx = (VX1 + 8) / 2;            // �K���ȉE���]��
		int col = colPx / 8;                // 8px/char �O��
		int yawDeg = Mathf.RoundToInt(NormalizeYaw(camYaw) * Mathf.Rad2Deg) % 360;

		screen.LOCATE(col, 2); screen.PRINT($"X:{gx:00} Y:{gy:00}");
		screen.LOCATE(col, 4); screen.PRINT($"DIR:{"NESW"[dir4]} Yaw:{yawDeg:000}");
		screen.LOCATE(col, 6); screen.PRINT(isTurning ? "Turning" : (isMoving ? "Moving" : "Idle"));
		screen.LOCATE(col, 8); screen.PRINT("����:Turn  ����/WS:Step  G:New  M:Mini");
	}

	void DrawMiniMap(int R = 6)
	{
		int x0 = VX1 - (R * 2 + 1) - 3;
		int y0 = VY1 - (R * 2 + 1) - 3;

		// �g
		screen.LINE(x0 - 1, y0 - 1, x0 + (R * 2 + 1), y0 - 1);
		screen.LINE(x0 + (R * 2 + 1), y0 - 1, x0 + (R * 2 + 1), y0 + (R * 2 + 1));
		screen.LINE(x0 + (R * 2 + 1), y0 + (R * 2 + 1), x0 - 1, y0 + (R * 2 + 1));
		screen.LINE(x0 - 1, y0 + (R * 2 + 1), x0 - 1, y0 - 1);

		// �s�P�ʃ����`��i�y�ʁj
		for (int dy = -R; dy <= R; dy++)
		{
			int sx = -1, ex = -1;
			int yy = y0 + (dy + R);
			for (int dx = -R; dx <= R; dx++)
			{
				int mx = gx + dx, my = gy + dy;
				bool wall = (mx < 0 || mx >= mazeW || my < 0 || my >= mazeH) ? true : (M[mx, my] == WALL);

				if (wall) { if (sx == -1) sx = dx; ex = dx; }
				if (!wall || dx == R)
				{
					if (sx != -1)
					{
						int x1 = x0 + (sx + R);
						int x2 = x0 + (ex + R);
						screen.LINE(x1, yy, x2, yy);
						sx = ex = -1;
					}
				}
			}
		}

		// ���@�i�\���j
		int px = x0 + R, py = y0 + R;
		screen.PSET(px, py, true, true);
		screen.PSET(px + 1, py, true, true);
		screen.PSET(px - 1, py, true, true);
		screen.PSET(px, py + 1, true, true);
		screen.PSET(px, py - 1, true, true);
	}
}
