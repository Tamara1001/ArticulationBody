using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Station.EditorTools
{
    /// <summary>
    /// Genera mallas low-poly con sombreado plano (cajas biseladas, prismas, esferas)
    /// y guarda en UV3 una normal suavizada por posición para el outline de casco invertido.
    /// </summary>
    public static class MeshFactory
    {
        public const string Folder = "Assets/_Project/Meshes";
        private static readonly Dictionary<string, Mesh> Cache = new();

        public static void ResetCache() => Cache.Clear();

        // ------------------------------------------------------------------ API

        public static Mesh Box(Vector3 size, float bevel = 0.01f)
        {
            string key = $"Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_b{bevel:0.###}";
            return Cached(key, () => BuildBeveledBox(size * 0.5f, Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f)));
        }

        /// <summary>Prisma de N lados (cilindro low-poly). axis: 0 = X, 1 = Y, 2 = Z.</summary>
        public static Mesh Prism(int sides, float radius, float length, int axis)
        {
            string key = $"Prism_{sides}_{radius:0.###}_{length:0.###}_a{axis}";
            return Cached(key, () => BuildPrism(sides, radius, length, axis));
        }

        public static Mesh Sphere(float radius, int segments = 8)
        {
            string key = $"Sphere_{radius:0.###}_{segments}";
            return Cached(key, () => BuildSphere(radius, segments));
        }

        // ------------------------------------------------------------------ construcción

        private class Builder
        {
            public readonly List<Vector3> V = new();
            public readonly List<Vector3> N = new();
            public readonly List<int> T = new();

            /// <summary>Agrega un polígono convexo plano con la normal deseada (corrige el orden si hace falta).</summary>
            public void Poly(IList<Vector3> pts, Vector3 normal)
            {
                normal.Normalize();
                int start = V.Count;
                foreach (var p in pts) { V.Add(p); N.Add(normal); }
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    int a = start, b = start + i, c = start + i + 1;
                    // Unity: la cara frontal es la de orden horario; normal = cross(b-a, c-a)
                    if (Vector3.Dot(Vector3.Cross(V[b] - V[a], V[c] - V[a]), normal) < 0f) (b, c) = (c, b);
                    T.Add(a); T.Add(b); T.Add(c);
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetTriangles(T, 0);
                m.SetUVs(3, SmoothNormals(V, N));
                m.RecalculateBounds();
                return m;
            }
        }

        private static List<Vector3> SmoothNormals(List<Vector3> v, List<Vector3> n)
        {
            var sum = new Dictionary<Vector3Int, Vector3>();
            var seen = new Dictionary<Vector3Int, List<Vector3>>();
            Vector3Int Key(Vector3 p) => new(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
            for (int i = 0; i < v.Count; i++)
            {
                var k = Key(v[i]);
                if (!seen.TryGetValue(k, out var list)) { list = new List<Vector3>(); seen[k] = list; sum[k] = Vector3.zero; }
                // Cada normal de cara distinta cuenta una sola vez
                bool dup = false;
                foreach (var e in list) if (Vector3.Dot(e, n[i]) > 0.999f) { dup = true; break; }
                if (!dup) { list.Add(n[i]); sum[k] += n[i]; }
            }
            var result = new List<Vector3>(v.Count);
            for (int i = 0; i < v.Count; i++) result.Add(sum[Key(v[i])].normalized);
            return result;
        }

        private static Mesh BuildBeveledBox(Vector3 h, float b)
        {
            var bd = new Builder();
            float[] s = { -1f, 1f };

            // Caras (inset por el bisel)
            for (int axis = 0; axis < 3; axis++)
            foreach (float sign in s)
            {
                int u = (axis + 1) % 3, w = (axis + 2) % 3;
                var pts = new List<Vector3>();
                float[,] loop = { { -1, -1 }, { -1, 1 }, { 1, 1 }, { 1, -1 } };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = Vector3.zero;
                    p[axis] = sign * h[axis];
                    p[u] = loop[i, 0] * (h[u] - b);
                    p[w] = loop[i, 1] * (h[w] - b);
                    pts.Add(p);
                }
                Vector3 n = Vector3.zero; n[axis] = sign;
                bd.Poly(pts, n);
            }

            if (b > 1e-5f)
            {
                // Chaflanes de arista
                for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                foreach (float si in s)
                foreach (float sj in s)
                {
                    int k = 3 - i - j;
                    var pts = new List<Vector3>();
                    foreach (var (ai, aj, kk) in new[] { (h[i], h[j] - b, -1f), (h[i], h[j] - b, 1f), (h[i] - b, h[j], 1f), (h[i] - b, h[j], -1f) })
                    {
                        Vector3 p = Vector3.zero;
                        p[i] = si * ai; p[j] = sj * aj; p[k] = kk * (h[k] - b);
                        pts.Add(p);
                    }
                    Vector3 n = Vector3.zero; n[i] = si; n[j] = sj;
                    bd.Poly(pts, n);
                }

                // Esquinas
                foreach (float sx in s)
                foreach (float sy in s)
                foreach (float sz in s)
                {
                    var pts = new List<Vector3>
                    {
                        new(sx * h.x, sy * (h.y - b), sz * (h.z - b)),
                        new(sx * (h.x - b), sy * h.y, sz * (h.z - b)),
                        new(sx * (h.x - b), sy * (h.y - b), sz * h.z),
                    };
                    bd.Poly(pts, new Vector3(sx, sy, sz));
                }
            }
            return bd.ToMesh("Box");
        }

        private static Vector3 AxisVec(float a, float r, float along, int axis) => axis switch
        {
            0 => new Vector3(along, Mathf.Cos(a) * r, Mathf.Sin(a) * r),
            1 => new Vector3(Mathf.Cos(a) * r, along, Mathf.Sin(a) * r),
            _ => new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, along),
        };

        private static Mesh BuildPrism(int sides, float r, float len, int axis)
        {
            var bd = new Builder();
            float hl = len * 0.5f;
            var capA = new List<Vector3>();
            var capB = new List<Vector3>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = AxisVec(a0, r, -hl, axis); var p1 = AxisVec(a1, r, -hl, axis);
                var q0 = AxisVec(a0, r, hl, axis); var q1 = AxisVec(a1, r, hl, axis);
                Vector3 mid = AxisVec((a0 + a1) * 0.5f, 1f, 0f, axis);
                bd.Poly(new[] { p0, q0, q1, p1 }, mid);
                capA.Add(p0); capB.Add(q0);
            }
            Vector3 ax = Vector3.zero; ax[axis] = 1f;
            bd.Poly(capA, -ax);
            bd.Poly(capB, ax);
            return bd.ToMesh("Prism");
        }

        private static Mesh BuildSphere(float r, int seg)
        {
            var bd = new Builder();
            int rings = Mathf.Max(3, seg / 2);
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < seg; j++)
            {
                float t0 = Mathf.PI * i / rings, t1 = Mathf.PI * (i + 1) / rings;
                float p0 = 2f * Mathf.PI * j / seg, p1 = 2f * Mathf.PI * (j + 1) / seg;
                Vector3 P(float t, float p) => new(Mathf.Sin(t) * Mathf.Cos(p) * r, Mathf.Cos(t) * r, Mathf.Sin(t) * Mathf.Sin(p) * r);
                var pts = new List<Vector3>();
                pts.Add(P(t0, p0));
                if (i > 0) pts.Add(P(t0, p1));
                pts.Add(P(t1, p1));
                if (i < rings - 1) pts.Add(P(t1, p0));
                Vector3 c = Vector3.zero;
                foreach (var p in pts) c += p;
                bd.Poly(pts, c / pts.Count);
            }
            return bd.ToMesh("Sphere");
        }

        // ------------------------------------------------------------------ assets

        private static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out var m) && m) return m;
            m = build();
            m.name = key;
            string path = $"{Folder}/{key}.asset";
            AssetDatabase.CreateAsset(m, path);
            Cache[key] = m;
            return m;
        }
    }
}
