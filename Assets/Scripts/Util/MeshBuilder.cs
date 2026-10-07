using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>Generacion procedural de mallas (islas flotantes estilo mapa de BombSquad).</summary>
    public static class MeshBuilder
    {
        /// <summary>Contorno de rectangulo redondeado en XZ, en sentido antihorario.</summary>
        public static List<Vector2> RoundedRect(float halfW, float halfD, float r, int segs)
        {
            r = Mathf.Min(r, Mathf.Min(halfW, halfD));
            var pts = new List<Vector2>();
            var centers = new[]
            {
                new Vector2(halfW - r, halfD - r),
                new Vector2(-halfW + r, halfD - r),
                new Vector2(-halfW + r, -halfD + r),
                new Vector2(halfW - r, -halfD + r),
            };
            for (int c = 0; c < 4; c++)
            {
                float a0 = c * Mathf.PI * 0.5f;
                for (int s = 0; s <= segs; s++)
                {
                    float a = a0 + s * (Mathf.PI * 0.5f) / segs;
                    var p = centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (pts.Count > 0 && (pts[pts.Count - 1] - p).sqrMagnitude < 1e-6f) continue;
                    pts.Add(p);
                }
            }
            if ((pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f) pts.RemoveAt(pts.Count - 1);
            return pts;
        }

        public struct Ring
        {
            public float y, scale, jitter;
            public Ring(float y, float scale, float jitter) { this.y = y; this.scale = scale; this.jitter = jitter; }
        }

        /// <summary>
        /// Isla: cara superior plana (submesh 0) y laterales rocosos que se estrechan
        /// hacia una punta inferior (submesh 1).
        /// </summary>
        public static Mesh Island(List<Vector2> outline, float topY, Ring[] rings, float bottomY, float uvTop, int seed)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var top = new List<int>();
            var side = new List<int>();
            int n = outline.Count;

            // --- Cara superior (abanico)
            int c0 = verts.Count;
            verts.Add(new Vector3(0, topY, 0));
            uvs.Add(Vector2.zero);
            for (int i = 0; i < n; i++)
            {
                verts.Add(new Vector3(outline[i].x, topY, outline[i].y));
                uvs.Add(outline[i] * uvTop);
            }
            for (int i = 0; i < n; i++)
                AddTri(verts, top, c0, c0 + 1 + i, c0 + 1 + (i + 1) % n, Vector3.up);

            // --- Laterales
            float perim = 0f;
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                cum[i] = perim;
                perim += (outline[(i + 1) % n] - outline[i]).magnitude;
            }
            cum[n] = perim;

            var ringStart = new int[rings.Length + 1];
            for (int k = 0; k <= rings.Length; k++)
            {
                ringStart[k] = verts.Count;
                float y = k == 0 ? topY : rings[k - 1].y;
                float sc = k == 0 ? 1f : rings[k - 1].scale;
                float jit = k == 0 ? 0f : rings[k - 1].jitter;
                for (int i = 0; i <= n; i++)
                {
                    int ii = i % n;
                    float noise = (Mathf.PerlinNoise(ii * 0.37f + seed * 3.1f, k * 1.7f + seed) - 0.5f) * 2f;
                    float s = sc * (1f + noise * jit);
                    verts.Add(new Vector3(outline[ii].x * s, y + noise * jit * 0.6f, outline[ii].y * s));
                    uvs.Add(new Vector2(cum[i] * 0.25f, y * 0.25f));
                }
            }
            for (int k = 0; k < rings.Length; k++)
            {
                int a = ringStart[k], b = ringStart[k + 1];
                for (int i = 0; i < n; i++)
                {
                    Vector2 o = (outline[i] + outline[(i + 1) % n]) * 0.5f;
                    Vector3 outward = new Vector3(o.x, 0f, o.y).normalized;
                    AddTri(verts, side, a + i, a + i + 1, b + i + 1, outward);
                    AddTri(verts, side, a + i, b + i + 1, b + i, outward);
                }
            }
            int tip = verts.Count;
            verts.Add(new Vector3(0f, bottomY, 0f));
            uvs.Add(new Vector2(0f, bottomY * 0.25f));
            int last = ringStart[rings.Length];
            for (int i = 0; i < n; i++)
            {
                Vector2 o = outline[i];
                Vector3 outward = (new Vector3(o.x, 0f, o.y).normalized + Vector3.down).normalized;
                AddTri(verts, side, last + i, last + i + 1, tip, outward);
            }

            var m = new Mesh { name = "Island" };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.subMeshCount = 2;
            m.SetTriangles(top, 0);
            m.SetTriangles(side, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Anade un triangulo orientado para que su cara visible apunte hacia expectedNormal.</summary>
        static void AddTri(List<Vector3> v, List<int> tris, int a, int b, int c, Vector3 expectedNormal)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, expectedNormal) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        /// <summary>
        /// Caja con aristas redondeadas (bloque de juguete). UV planares por cara en metros * uvScale,
        /// para que la veta de la madera tenga el mismo tamano en todos los bloques.
        /// </summary>
        public static Mesh RoundedBox(Vector3 size, float r, float uvScale = 0.5f, Vector2 uvOffset = default)
        {
            Vector3 h = size * 0.5f;
            r = Mathf.Min(r, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.95f);
            Vector3 inner = h - Vector3.one * r;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            var faces = new[]
            {
                (n: Vector3.up, u: Vector3.right, v: Vector3.forward),
                (n: Vector3.down, u: Vector3.right, v: Vector3.back),
                (n: Vector3.right, u: Vector3.back, v: Vector3.up),
                (n: Vector3.left, u: Vector3.forward, v: Vector3.up),
                (n: Vector3.forward, u: Vector3.right, v: Vector3.up),
                (n: Vector3.back, u: Vector3.left, v: Vector3.up),
            };

            foreach (var f in faces)
            {
                float hu = Mathf.Abs(Vector3.Dot(h, f.u)), hv = Mathf.Abs(Vector3.Dot(h, f.v)), hn = Mathf.Abs(Vector3.Dot(h, f.n));
                var cu = AxisSteps(hu, r);
                var cv = AxisSteps(hv, r);
                int start = verts.Count;
                for (int j = 0; j < cv.Count; j++)
                    for (int i = 0; i < cu.Count; i++)
                    {
                        Vector3 p = f.n * hn + f.u * cu[i] + f.v * cv[j];
                        Vector3 c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = p - c;
                        Vector3 n = d.sqrMagnitude > 1e-10f ? d.normalized : f.n;
                        Vector3 pos = c + n * r;
                        verts.Add(pos);
                        norms.Add(n);
                        uvs.Add(new Vector2(Vector3.Dot(pos, f.u), Vector3.Dot(pos, f.v)) * uvScale + uvOffset);
                    }
                int w = cu.Count;
                for (int j = 0; j < cv.Count - 1; j++)
                    for (int i = 0; i < w - 1; i++)
                    {
                        int a = start + j * w + i;
                        AddTri(verts, tris, a, a + w, a + 1, f.n);
                        AddTri(verts, tris, a + 1, a + w, a + w + 1, f.n);
                    }
            }

            var m = new Mesh { name = "RoundedBox" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Coordenadas a lo largo de un eje: mas densas en los bordes redondeados.</summary>
        static List<float> AxisSteps(float half, float r)
        {
            var l = new List<float>();
            float[] edge = { 0f, 0.25f, 0.55f, 0.8f, 1f };
            foreach (var e in edge) l.Add(-half + r * e);
            float span = 2f * (half - r);
            int mid = Mathf.Max(1, Mathf.CeilToInt(span / 1.0f));
            for (int k = 1; k < mid; k++) l.Add(-half + r + span * k / mid);
            for (int e = edge.Length - 1; e >= 0; e--) l.Add(half - r * edge[e]);
            return l;
        }

        /// <summary>Esfera deformada con ruido: rocas del fondo.</summary>
        public static Mesh Rock(int seed, float roughness = 0.25f)
        {
            const int lat = 14, lon = 20;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float o = seed * 13.37f;
            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat;
                float th = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float ph = (float)(x % lon) / lon * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float n = Mathf.PerlinNoise(d.x * 1.3f + o, d.y * 1.3f + d.z * 0.7f + o) - 0.5f;
                    n += (Mathf.PerlinNoise(d.z * 3.1f + o, d.x * 3.1f - d.y + o) - 0.5f) * 0.4f;
                    float rad = 0.5f * (1f + n * roughness * 2f);
                    if (d.y < -0.3f) rad *= Mathf.Lerp(1f, 0.85f, (-d.y - 0.3f) / 0.7f);
                    verts.Add(d * rad);
                }
            }
            int w = lon + 1;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * w + x;
                    Vector3 c = (verts[a] + verts[a + w + 1]) * 0.5f;
                    AddTri(verts, tris, a, a + 1, a + w + 1, c);
                    AddTri(verts, tris, a, a + w + 1, a + w, c);
                }
            // Soldar la costura para normales suaves.
            for (int y = 0; y <= lat; y++) verts[y * w + lon] = verts[y * w];
            var m = new Mesh { name = "Rock" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Plano grande subdividido (para el agua con olas en el vertice).</summary>
        public static Mesh Grid(float size, int cells)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float step = size / cells;
            for (int z = 0; z <= cells; z++)
                for (int x = 0; x <= cells; x++)
                    verts.Add(new Vector3(-size * 0.5f + x * step, 0f, -size * 0.5f + z * step));
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int i = z * (cells + 1) + x;
                    AddTri(verts, tris, i, i + cells + 1, i + 1, Vector3.up);
                    AddTri(verts, tris, i + 1, i + cells + 1, i + cells + 2, Vector3.up);
                }
            var m = new Mesh { name = "Grid" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
