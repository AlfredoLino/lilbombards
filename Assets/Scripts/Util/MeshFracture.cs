using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Parte una malla en trozos reales: cada trozo es un grupo de triangulos vecinos de la malla
    /// original (conserva su curvatura, UV y normales). Se le anaden caras traseras para que se vea
    /// bien desde cualquier lado mientras gira en el aire.
    /// </summary>
    public static class MeshFracture
    {
        public struct Piece
        {
            public Mesh mesh;
            public Vector3 center; // centro del trozo en espacio local de la malla original
        }

        public static List<Piece> Split(Mesh src, int count, float radiusFrac)
        {
            var result = new List<Piece>();
            if (src == null || !src.isReadable || count <= 0) return result;
            var v = src.vertices;
            var n = src.normals;
            var uv = src.uv;
            var tris = src.triangles;
            int triCount = tris.Length / 3;
            if (triCount == 0) return result;

            var cent = new Vector3[triCount];
            for (int i = 0; i < triCount; i++)
                cent[i] = (v[tris[i * 3]] + v[tris[i * 3 + 1]] + v[tris[i * 3 + 2]]) / 3f;

            float baseR = src.bounds.size.magnitude * radiusFrac;
            for (int k = 0; k < count; k++)
            {
                Vector3 seed = cent[Random.Range(0, triCount)];
                float r = baseR * Random.Range(0.75f, 1.3f);
                float r2 = r * r;
                var map = new Dictionary<int, int>();
                var nv = new List<Vector3>();
                var nn = new List<Vector3>();
                var nuv = new List<Vector2>();
                var nt = new List<int>();
                for (int i = 0; i < triCount; i++)
                {
                    if ((cent[i] - seed).sqrMagnitude > r2) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int vi = tris[i * 3 + j];
                        if (!map.TryGetValue(vi, out int ni))
                        {
                            ni = nv.Count;
                            map[vi] = ni;
                            nv.Add(v[vi]);
                            nn.Add(vi < n.Length ? n[vi] : Vector3.up);
                            nuv.Add(vi < uv.Length ? uv[vi] : Vector2.zero);
                        }
                        nt.Add(ni);
                    }
                }
                if (nt.Count < 3) continue;

                Vector3 c = Vector3.zero;
                foreach (var p in nv) c += p;
                c /= nv.Count;
                for (int i = 0; i < nv.Count; i++) nv[i] -= c;

                // Caras traseras (los trozos de cascara son de una sola cara).
                int baseCount = nv.Count;
                for (int i = 0; i < baseCount; i++)
                {
                    nv.Add(nv[i]);
                    nn.Add(-nn[i]);
                    nuv.Add(nuv[i]);
                }
                int tc = nt.Count;
                for (int t = 0; t < tc; t += 3)
                {
                    nt.Add(nt[t] + baseCount);
                    nt.Add(nt[t + 2] + baseCount);
                    nt.Add(nt[t + 1] + baseCount);
                }

                var m = new Mesh { name = "Fragment" };
                if (nv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(nv);
                m.SetNormals(nn);
                m.SetUVs(0, nuv);
                m.SetTriangles(nt, 0);
                m.RecalculateBounds();
                result.Add(new Piece { mesh = m, center = c });
            }
            return result;
        }
    }
}
