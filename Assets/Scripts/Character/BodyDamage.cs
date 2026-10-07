using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Division de una malla en trozos organicos (particion de Voronoi sobre la superficie, con
    /// semillas repartidas por area y una relajacion de Lloyd). Se calcula una vez por malla y se
    /// comparte entre todos los personajes.
    /// </summary>
    public class ChunkData
    {
        public int Count;
        public List<int>[] Tris;      // triangulos (indice de triangulo) de cada trozo
        public Vector3[] Centroid;    // centro del trozo (espacio local de la malla)
        public Vector3[] Normal;      // normal media del trozo
        public float[] Area;
        public int[] VertChunk;       // trozo al que pertenece cada vertice (-1 si ninguno)
        public List<int>[] Verts;     // vertices de cada trozo

        static readonly Dictionary<Mesh, Dictionary<int, ChunkData>> cache = new Dictionary<Mesh, Dictionary<int, ChunkData>>();

        public static ChunkData Get(Mesh m, int k)
        {
            if (!cache.TryGetValue(m, out var byK))
            {
                byK = new Dictionary<int, ChunkData>();
                cache[m] = byK;
            }
            if (!byK.TryGetValue(k, out var d))
            {
                d = Build(m, k);
                byK[k] = d;
            }
            return d;
        }

        static ChunkData Build(Mesh m, int k)
        {
            var v = m.vertices;
            var t = m.triangles;
            int T = t.Length / 3;
            var cen = new Vector3[T];
            var area = new float[T];
            var nrm = new Vector3[T];
            var cum = new float[T];
            float total = 0f;
            for (int i = 0; i < T; i++)
            {
                Vector3 a = v[t[i * 3]], b = v[t[i * 3 + 1]], c = v[t[i * 3 + 2]];
                Vector3 cr = Vector3.Cross(b - a, c - a);
                area[i] = cr.magnitude * 0.5f;
                nrm[i] = cr.sqrMagnitude > 1e-20f ? cr.normalized : Vector3.up;
                cen[i] = (a + b + c) / 3f;
                total += area[i];
                cum[i] = total;
            }
            k = Mathf.Clamp(k, 1, Mathf.Max(1, T));

            // Semillas repartidas por area, separadas entre si (muestreo con rechazo).
            var rnd = new System.Random(1234 + v.Length * 7 + T);
            var seeds = new List<Vector3>();
            float minD = Mathf.Sqrt(total / k) * 0.65f;
            for (int attempt = 0; attempt < k * 40 && seeds.Count < k; attempt++)
            {
                float r = (float)rnd.NextDouble() * total;
                int lo = 0, hi = T - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (cum[mid] < r) lo = mid + 1; else hi = mid;
                }
                Vector3 p = cen[lo];
                bool ok = true;
                foreach (var s in seeds)
                    if ((s - p).sqrMagnitude < minD * minD) { ok = false; break; }
                if (ok || attempt > k * 30) seeds.Add(p);
            }
            int K = seeds.Count;
            var assign = new int[T];

            for (int iter = 0; iter < 3; iter++)
            {
                for (int i = 0; i < T; i++)
                {
                    int best = 0;
                    float bd = float.MaxValue;
                    for (int s = 0; s < K; s++)
                    {
                        float d = (seeds[s] - cen[i]).sqrMagnitude;
                        if (d < bd) { bd = d; best = s; }
                    }
                    assign[i] = best;
                }
                if (iter == 2) break;
                // Relajacion de Lloyd: cada semilla al centro de su region.
                var sum = new Vector3[K];
                var w = new float[K];
                for (int i = 0; i < T; i++)
                {
                    sum[assign[i]] += cen[i] * area[i];
                    w[assign[i]] += area[i];
                }
                for (int s = 0; s < K; s++)
                    if (w[s] > 0f) seeds[s] = sum[s] / w[s];
            }

            // Compactar (quitar regiones vacias) y calcular datos de cada trozo.
            var remap = new int[K];
            for (int s = 0; s < K; s++) remap[s] = -1;
            int count = 0;
            for (int i = 0; i < T; i++)
                if (remap[assign[i]] < 0) remap[assign[i]] = count++;

            var d2 = new ChunkData
            {
                Count = count,
                Tris = new List<int>[count],
                Verts = new List<int>[count],
                Centroid = new Vector3[count],
                Normal = new Vector3[count],
                Area = new float[count],
                VertChunk = new int[v.Length],
            };
            for (int c = 0; c < count; c++)
            {
                d2.Tris[c] = new List<int>();
                d2.Verts[c] = new List<int>();
            }
            for (int i = 0; i < v.Length; i++) d2.VertChunk[i] = -1;
            var csum = new Vector3[count];
            for (int i = 0; i < T; i++)
            {
                int c = remap[assign[i]];
                d2.Tris[c].Add(i);
                csum[c] += cen[i] * area[i];
                d2.Normal[c] += nrm[i] * area[i];
                d2.Area[c] += area[i];
                for (int j = 0; j < 3; j++)
                {
                    int vi = t[i * 3 + j];
                    if (d2.VertChunk[vi] != c)
                    {
                        if (d2.VertChunk[vi] < 0) d2.Verts[c].Add(vi);
                        d2.VertChunk[vi] = c;
                    }
                }
            }
            for (int c = 0; c < count; c++)
            {
                d2.Centroid[c] = d2.Area[c] > 0f ? csum[c] / d2.Area[c] : Vector3.zero;
                d2.Normal[c] = d2.Normal[c].sqrMagnitude > 1e-12f ? d2.Normal[c].normalized : Vector3.up;
            }
            // Lista de vertices completa por trozo (un vertice compartido puede estar en varios).
            for (int c = 0; c < count; c++) d2.Verts[c].Clear();
            for (int c = 0; c < count; c++)
            {
                var seen = new HashSet<int>();
                foreach (int tri in d2.Tris[c])
                    for (int j = 0; j < 3; j++)
                        if (seen.Add(t[tri * 3 + j])) d2.Verts[c].Add(t[tri * 3 + j]);
            }
            return d2;
        }
    }

    /// <summary>Una pieza del cuerpo con su malla propia dividida en trozos que se van desprendiendo.</summary>
    public class BodyPart
    {
        public Renderer R;
        public MeshFilter MF;
        public Mesh Mesh;
        public ChunkData D;
        public float Depth;          // 0 armadura, 0.5 ropa, 1 piel, 2 musculo, 3 hueso
        public bool Destructible;
        public float MaxHp;
        public bool[] Alive;
        public float[] Hp;
        public Color[] Colors;       // r = hollin, g = grietas/desgaste
        public Vector3[] V, N;
        public Vector2[] UV;
        public int[] OrigTris;
        public Transform Container;
        public Vector3[] ContPos;
        public List<int>[] CoverPart, CoverChunk;
        public bool Dirty;
        public bool Detached;        // pertenece a una extremidad cortada (ya no es del personaje)
    }

    /// <summary>
    /// Sistema de dano por trozos: cada pieza (casco, peto, piel, musculo...) esta dividida en muchos
    /// trozos con su propia resistencia. Una explosion dana solo los trozos EXPUESTOS que miran hacia ella,
    /// segun su orientacion real y su distancia; al romperse, el trozo sale volando con su forma exacta y
    /// deja ver la capa de debajo justo en ese sitio. El hollin y las grietas se pintan por vertice.
    /// </summary>
    public class BodyDamage
    {
        readonly List<BodyPart> parts = new List<BodyPart>();
        readonly HashSet<Renderer> registered = new HashSet<Renderer>();
        readonly Collider ignore;
        bool finished;

        public BodyDamage(Collider ignoreCollider)
        {
            ignore = ignoreCollider;
        }

        public bool IsRegistered(Renderer r) => registered.Contains(r);

        public void AddPart(Renderer r, Transform container, float depth, bool destructible, float hp, int chunks)
        {
            if (r == null || !registered.Add(r)) return;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
            var src = mf.sharedMesh;
            var p = new BodyPart
            {
                R = r,
                MF = mf,
                Mesh = Object.Instantiate(src),
                D = ChunkData.Get(src, chunks),
                Depth = depth,
                Destructible = destructible,
                MaxHp = hp,
                V = src.vertices,
                N = src.normals,
                UV = src.uv,
                OrigTris = src.triangles,
                Container = container,
            };
            if (p.N == null || p.N.Length != p.V.Length) p.N = new Vector3[p.V.Length];
            p.Mesh.name = src.name + "_dmg";
            mf.sharedMesh = p.Mesh;
            p.Alive = new bool[p.D.Count];
            p.Hp = new float[p.D.Count];
            for (int i = 0; i < p.D.Count; i++)
            {
                p.Alive[i] = true;
                p.Hp[i] = hp;
            }
            p.Colors = new Color[p.V.Length];
            for (int i = 0; i < p.Colors.Length; i++) p.Colors[i] = new Color(0f, 0f, 0f, 1f);
            p.Mesh.colors = p.Colors;
            parts.Add(p);
            if (finished)
            {
                // Pieza anadida despues (p. ej. un munon): sin nada encima.
                p.ContPos = new Vector3[p.D.Count];
                p.CoverPart = new List<int>[p.D.Count];
                p.CoverChunk = new List<int>[p.D.Count];
                for (int i = 0; i < p.D.Count; i++)
                {
                    p.ContPos[i] = p.Container.InverseTransformPoint(r.transform.TransformPoint(p.D.Centroid[i]));
                    p.CoverPart[i] = new List<int>();
                    p.CoverChunk[i] = new List<int>();
                }
            }
        }

        /// <summary>
        /// Las piezas bajo 'root' (una extremidad cortada) dejan de ser del personaje.
        /// Devuelve sus mallas para que las destruya quien se queda con ellas.
        /// </summary>
        public List<Mesh> Detach(Transform root)
        {
            var meshes = new List<Mesh>();
            foreach (var p in parts)
            {
                if (p.Detached || p.R == null || !p.R.transform.IsChildOf(root)) continue;
                p.Detached = true;
                if (p.Mesh != null) meshes.Add(p.Mesh);
            }
            return meshes;
        }

        /// <summary>
        /// Dano de las capas en unos contenedores (0..1): fraccion de trozos destruidos, pesando mas
        /// las capas profundas (perder musculo es peor que perder armadura).
        /// </summary>
        public float LayerDamage(Transform a, Transform b = null, Transform c = null)
        {
            float sum = 0f, wsum = 0f;
            foreach (var p in parts)
            {
                if (p.Detached || !p.Destructible) continue;
                if (p.Container != a && p.Container != b && p.Container != c) continue;
                int dead = 0;
                for (int i = 0; i < p.D.Count; i++) if (!p.Alive[i]) dead++;
                float w = 1f + p.Depth * 1.5f;
                sum += w * dead / Mathf.Max(1, p.D.Count);
                wsum += w;
            }
            return wsum > 0f ? sum / wsum : 0f;
        }

        /// <summary>Fraccion de trozos destruidos (piel, musculo, ropa, armadura) en los contenedores dados.</summary>
        public float StrippedFraction(Transform a, Transform b)
        {
            int total = 0, dead = 0;
            foreach (var p in parts)
            {
                if (p.Detached || !p.Destructible) continue;
                if (p.Container != a && (b == null || p.Container != b)) continue;
                for (int i = 0; i < p.D.Count; i++)
                {
                    total++;
                    if (!p.Alive[i]) dead++;
                }
            }
            return total > 0 ? (float)dead / total : 0.5f;
        }

        /// <summary>Calcula que trozos tapan a cuales (capas exteriores sobre interiores, mismo contenedor).</summary>
        public void Finish()
        {
            finished = true;
            foreach (var p in parts)
            {
                var tr = p.R.transform;
                p.ContPos = new Vector3[p.D.Count];
                for (int i = 0; i < p.D.Count; i++)
                    p.ContPos[i] = p.Container.InverseTransformPoint(tr.TransformPoint(p.D.Centroid[i]));
            }
            foreach (var p in parts)
            {
                p.CoverPart = new List<int>[p.D.Count];
                p.CoverChunk = new List<int>[p.D.Count];
                float sP = ScaleIn(p);
                for (int i = 0; i < p.D.Count; i++)
                {
                    p.CoverPart[i] = new List<int>();
                    p.CoverChunk[i] = new List<int>();
                    float ri = 1.3f * Mathf.Sqrt(p.D.Area[i]) * sP;
                    for (int qi = 0; qi < parts.Count; qi++)
                    {
                        var q = parts[qi];
                        if (q == p || q.Container != p.Container || q.Depth >= p.Depth || !q.Destructible) continue;
                        float sQ = ScaleIn(q);
                        for (int j = 0; j < q.D.Count; j++)
                        {
                            float rj = 1.3f * Mathf.Sqrt(q.D.Area[j]) * sQ;
                            float r = Mathf.Max(ri, rj);
                            if ((p.ContPos[i] - q.ContPos[j]).sqrMagnitude < r * r)
                            {
                                p.CoverPart[i].Add(qi);
                                p.CoverChunk[i].Add(j);
                            }
                        }
                    }
                }
            }
        }

        static float ScaleIn(BodyPart p)
        {
            Vector3 a = p.R.transform.lossyScale, b = p.Container.lossyScale;
            return (Mathf.Abs(a.x / b.x) + Mathf.Abs(a.y / b.y) + Mathf.Abs(a.z / b.z)) / 3f;
        }

        /// <summary>Un trozo esta expuesto si ya no queda (casi) nada encima.</summary>
        bool Exposed(BodyPart p, int i)
        {
            var cp = p.CoverPart[i];
            if (cp.Count == 0) return true;
            int alive = 0;
            for (int k = 0; k < cp.Count; k++)
                if (parts[cp[k]].Alive[p.CoverChunk[i][k]]) alive++;
            return alive == 0 || alive < cp.Count * 0.34f;
        }

        static Vector3 NormalToWorld(Transform tr, Vector3 n)
        {
            Vector3 s = tr.lossyScale;
            var l = new Vector3(n.x / (Mathf.Abs(s.x) > 1e-5f ? s.x : 1f), n.y / (Mathf.Abs(s.y) > 1e-5f ? s.y : 1f), n.z / (Mathf.Abs(s.z) > 1e-5f ? s.z : 1f));
            return (tr.rotation * l).normalized;
        }

        struct Candidate
        {
            public BodyPart part;
            public int chunk;
            public Vector3 normal;
        }

        public void ApplyBlast(Vector3 blast, float strength, float radius)
        {
            // Foto de que estaba expuesto ANTES de esta explosion (una bomba no atraviesa todas las capas de golpe).
            var exposed = new bool[parts.Count][];
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var p = parts[pi];
                exposed[pi] = new bool[p.D.Count];
                if (p.Detached) continue;
                for (int i = 0; i < p.D.Count; i++) exposed[pi][i] = p.Alive[i] && Exposed(p, i);
            }

            var killed = new List<Candidate>();
            float minDepthKilled = 99f, maxDepthKilled = -1f;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var p = parts[pi];
                if (p.Detached || p.R == null) continue;
                var ex = exposed[pi];
                var tr = p.R.transform;
                Matrix4x4 l2w = tr.localToWorldMatrix;
                bool colorsChanged = false;

                // Hollin por vertice: cuanto mas mira hacia la explosion y mas cerca esta, mas se tizna.
                for (int vi = 0; vi < p.V.Length; vi++)
                {
                    int c = p.D.VertChunk[vi];
                    if (c < 0 || !ex[c]) continue;
                    Vector3 wp = l2w.MultiplyPoint3x4(p.V[vi]);
                    Vector3 to = blast - wp;
                    float dist = to.magnitude;
                    float prox = 1f - dist / (radius + 0.8f);
                    if (prox <= 0f) continue;
                    Vector3 wn = NormalToWorld(tr, p.N[vi]);
                    float facing = dist > 1e-4f ? Vector3.Dot(wn, to / dist) : 1f;
                    float jitter = 0.75f + 0.5f * Mathf.Repeat(vi * 0.61803f, 1f);
                    float s = strength * prox * (0.15f + 0.85f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.5f, 0.85f, facing))) * Tuning.SootPerBlast * jitter;
                    if (s < 0.003f) continue;
                    var col = p.Colors[vi];
                    col.r = Mathf.Min(1f, col.r + s);
                    p.Colors[vi] = col;
                    colorsChanged = true;
                }

                // Dano por trozo (solo trozos expuestos y destructibles).
                if (p.Destructible)
                {
                    for (int i = 0; i < p.D.Count; i++)
                    {
                        if (!ex[i]) continue;
                        Vector3 wp = tr.TransformPoint(p.D.Centroid[i]);
                        Vector3 to = blast - wp;
                        float dist = to.magnitude;
                        float prox = Mathf.Clamp01(1f - dist / (radius + 0.5f));
                        if (prox <= 0f) continue;
                        Vector3 wn = NormalToWorld(tr, p.D.Normal[i]);
                        float facing = dist > 1e-4f ? Vector3.Dot(wn, to / dist) : 1f;
                        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.6f, facing));
                        if (w < 0.01f) continue;
                        // Cada capa aguanta UNA explosion: todo trozo expuesto que mire a la bomba (dentro de su
                        // alcance) se rompe, sin importar la distancia. El borde del hueco es irregular: los trozos
                        // casi de canto solo se rompen a veces (y si no, se agrietan).
                        float dmg = w > Random.Range(0.12f, 0.4f) ? p.MaxHp + 1f : w * 0.6f;
                        p.Hp[i] -= dmg;
                        if (p.Hp[i] <= 0f)
                        {
                            p.Alive[i] = false;
                            p.Dirty = true;
                            killed.Add(new Candidate { part = p, chunk = i, normal = wn });
                            minDepthKilled = Mathf.Min(minDepthKilled, p.Depth);
                            maxDepthKilled = Mathf.Max(maxDepthKilled, p.Depth);
                        }
                        else
                        {
                            // Grietas en los trozos a punto de caer.
                            float wear = 1f - p.Hp[i] / p.MaxHp;
                            foreach (int vi in p.D.Verts[i])
                            {
                                var col = p.Colors[vi];
                                col.g = Mathf.Max(col.g, wear);
                                p.Colors[vi] = col;
                            }
                            colorsChanged = true;
                        }
                    }
                }

                if (colorsChanged) p.Mesh.colors = p.Colors;
                if (p.Dirty) Rebuild(p);
            }

            if (killed.Count == 0) return;

            // Los trozos que se desprenden salen volando (con un limite por explosion).
            for (int i = killed.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (killed[i], killed[j]) = (killed[j], killed[i]);
            }
            int n = Mathf.Min(killed.Count, Tuning.MaxChunkDebrisPerBlast);
            for (int i = 0; i < n; i++) SpawnChunk(killed[i], blast);

            // Sonido y polvo segun lo que se rompio.
            Vector3 center = killed[0].part.R.transform.position;
            if (minDepthKilled < 0.75f) Sfx.PlayAt(Sfx.ShieldBreak, center, Mathf.Clamp01(0.3f + killed.Count * 0.02f));
            if (maxDepthKilled >= 0.75f) Sfx.PlayAt(Sfx.Sticky, center, 0.55f);
        }

        void Rebuild(BodyPart p)
        {
            p.Dirty = false;
            var tl = new List<int>(p.OrigTris.Length);
            for (int c = 0; c < p.D.Count; c++)
            {
                if (!p.Alive[c]) continue;
                foreach (int tri in p.D.Tris[c])
                {
                    tl.Add(p.OrigTris[tri * 3]);
                    tl.Add(p.OrigTris[tri * 3 + 1]);
                    tl.Add(p.OrigTris[tri * 3 + 2]);
                }
            }
            p.Mesh.SetTriangles(tl, 0);
        }

        /// <summary>Crea el trozo que sale volando: la geometria exacta de ese trozo, con sus colores (hollin).</summary>
        void SpawnChunk(Candidate c, Vector3 blast)
        {
            var p = c.part;
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            foreach (int tri in p.D.Tris[c.chunk])
            {
                for (int j = 0; j < 3; j++)
                {
                    int vi = p.OrigTris[tri * 3 + j];
                    if (!map.TryGetValue(vi, out int ni))
                    {
                        ni = v.Count;
                        map[vi] = ni;
                        v.Add(p.V[vi]);
                        n.Add(p.N[vi]);
                        uv.Add(p.UV != null && vi < p.UV.Length ? p.UV[vi] : Vector2.zero);
                        col.Add(p.Colors[vi]);
                    }
                    t.Add(ni);
                }
            }
            if (t.Count < 3) return;
            Vector3 center = p.D.Centroid[c.chunk];
            for (int i = 0; i < v.Count; i++) v[i] -= center;
            int baseCount = v.Count;
            for (int i = 0; i < baseCount; i++)
            {
                v.Add(v[i]);
                n.Add(-n[i]);
                uv.Add(uv[i]);
                col.Add(col[i]);
            }
            int tc = t.Count;
            for (int k = 0; k < tc; k += 3)
            {
                t.Add(t[k] + baseCount);
                t.Add(t[k + 2] + baseCount);
                t.Add(t[k + 1] + baseCount);
            }
            var m = new Mesh { name = "Chunk" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetColors(col);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();

            var tr = p.R.transform;
            FX.SpawnChunk(m, tr.TransformPoint(center), tr.rotation, tr.lossyScale, p.R.sharedMaterial, blast, c.normal, ignore);
        }

        /// <summary>
        /// Salud: vuelve a crecer la capa mas profunda que este danada (p. ej. el musculo) y se limpia
        /// la mitad del hollin.
        /// </summary>
        public void Heal()
        {
            float deepest = -1f;
            foreach (var p in parts)
                if (p.Destructible && !p.Detached)
                    for (int i = 0; i < p.D.Count; i++)
                        if (!p.Alive[i]) { deepest = Mathf.Max(deepest, p.Depth); break; }
            foreach (var p in parts)
            {
                if (p.Detached) continue;
                bool changed = false;
                if (deepest >= 0f && p.Destructible && Mathf.Abs(p.Depth - deepest) < 0.01f)
                    for (int i = 0; i < p.D.Count; i++)
                        if (!p.Alive[i])
                        {
                            p.Alive[i] = true;
                            p.Hp[i] = p.MaxHp;
                            p.Dirty = true;
                        }
                for (int i = 0; i < p.Colors.Length; i++)
                {
                    var col = p.Colors[i];
                    col.r *= 0.5f;
                    col.g = 0f;
                    p.Colors[i] = col;
                    changed = true;
                }
                if (changed) p.Mesh.colors = p.Colors;
                if (p.Dirty) Rebuild(p);
            }
        }

        public void Dispose()
        {
            foreach (var p in parts)
                if (!p.Detached && p.Mesh != null) Object.Destroy(p.Mesh);
            parts.Clear();
        }
    }
}
