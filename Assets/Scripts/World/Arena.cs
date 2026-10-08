using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Mapas. Cada uno construye por codigo su suelo (con colisiones), decorado, iluminacion, cielo y niebla,
    /// y define los puntos de aparicion, de powerups y de TNT. El borde jugable se describe con poligonos
    /// (suelos y agujeros) para que los bots sepan donde esta el vacio en cualquier mapa.
    /// Caerse = muerte.
    /// </summary>
    public class Arena : MonoBehaviour
    {
        public static Arena I;

        public static readonly string[] MapNames =
        {
            "Puente de Bloques",
            "Isla Tropical",
            "Tres Islas",
            "Coliseo de Lava",
            "Cumbre Nevada",
        };

        public static readonly string[] MapInfo =
        {
            "Bloques de juguete sobre las nubes, con barandas altas.",
            "Isla de hierba en mitad del mar. Sin barandas: cuidado con el borde.",
            "Tres plataformas unidas por puentes estrechos al atardecer.",
            "Arena cerrada por muros, de noche, con un pozo de lava en el centro.",
            "Cima de una montaña nevada con pinos y bloques de hielo para cubrirse.",
        };

        public static int MapCount => MapNames.Length;

        public int Map;
        /// <summary>Medio ancho/fondo aproximado del area jugable.</summary>
        public float HalfW = 9f;
        public float HalfD = 6f;
        /// <summary>Limites del punto al que mira la camara (x min/max, z min/max).</summary>
        public Vector2 CamX = new Vector2(-4f, 4f);
        public Vector2 CamZ = new Vector2(-2.5f, 3f);

        public readonly List<Vector3> SpawnPoints = new List<Vector3>();
        public readonly List<Vector3> PowerupPoints = new List<Vector3>();
        public readonly List<Vector3> TntSpots = new List<Vector3>();

        // Contornos del suelo (XZ) y de los agujeros, para EdgeDistance.
        readonly List<List<Vector2>> floors = new List<List<Vector2>>();
        readonly List<List<Vector2>> holes = new List<List<Vector2>>();
        /// <summary>Puntos de paso para los bots (puentes, rodear el pozo...).</summary>
        readonly List<Vector3> waypoints = new List<Vector3>();
        /// <summary>Distancia maxima de la camara (mapas mas anchos necesitan alejarse mas).</summary>
        public float CamMaxDist = 26f;

        const float FloorThick = 1.2f;
        const float Bevel = 0.1f;

        Material wood;
        readonly Dictionary<string, Material> paints = new Dictionary<string, Material>();

        static readonly Color Orange = new Color(0.96f, 0.5f, 0.1f);
        static readonly Color Yellow = new Color(0.98f, 0.78f, 0.12f);
        static readonly Color Green = new Color(0.16f, 0.5f, 0.22f);
        static readonly Color Red = new Color(0.82f, 0.16f, 0.12f);
        static readonly Color Blue = new Color(0.16f, 0.36f, 0.82f);

        public static Arena Build(int map)
        {
            if (I != null)
            {
                I.gameObject.SetActive(false); // sus colisiones dejan de existir ya, no al final del frame
                Destroy(I.gameObject);
            }
            map = Mathf.Clamp(map, 0, MapCount - 1);
            var go = new GameObject("Arena - " + MapNames[map]);
            var a = go.AddComponent<Arena>();
            a.Map = map;
            I = a;
            a.wood = Gfx.Toon(Color.white, 0.35f, 0.45f, 0.12f, Gfx.Wood());
            switch (map)
            {
                case 1: a.BuildTropical(); break;
                case 2: a.BuildThreeIslands(); break;
                case 3: a.BuildColosseum(); break;
                case 4: a.BuildSnow(); break;
                default: a.BuildBlocks(); break;
            }
            return a;
        }

        // ------------------------------------------------------------------ Borde (para los bots)

        /// <summary>Distancia al borde del suelo (positiva dentro, negativa sobre el vacio o un agujero).</summary>
        public float EdgeDistance(Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            float d = -999f;
            foreach (var f in floors) d = Mathf.Max(d, PolyInside(f, q));
            foreach (var h in holes) d = Mathf.Min(d, -PolyInside(h, q));
            return d;
        }

        /// <summary>Direccion horizontal hacia el borde mas cercano.</summary>
        public Vector3 Outward(Vector3 p)
        {
            const float e = 0.25f;
            float dx = EdgeDistance(p + Vector3.right * e) - EdgeDistance(p - Vector3.right * e);
            float dz = EdgeDistance(p + Vector3.forward * e) - EdgeDistance(p - Vector3.forward * e);
            var g = new Vector3(-dx, 0f, -dz);
            if (g.sqrMagnitude < 1e-6f)
            {
                g = new Vector3(p.x, 0f, p.z);
                if (g.sqrMagnitude < 1e-4f) g = Vector3.right;
            }
            return g.normalized;
        }

        /// <summary>El camino recto entre dos puntos va siempre por suelo firme.</summary>
        public bool ClearPath(Vector3 a, Vector3 b, float margin = 0.35f)
        {
            a.y = b.y = 0f;
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.4f));
            for (int i = 1; i <= n; i++)
                if (EdgeDistance(Vector3.Lerp(a, b, i / (float)n)) < margin) return false;
            return true;
        }

        /// <summary>
        /// A donde caminar para llegar a 'to': directo si se puede; si no, al punto de paso que mejor acerca
        /// (asi los bots cruzan por los puentes y rodean el pozo en vez de tirarse al vacio).
        /// </summary>
        public Vector3 Steer(Vector3 from, Vector3 to)
        {
            if (waypoints.Count == 0 || ClearPath(from, to)) return to;
            Vector3 best = to;
            float bestCost = float.MaxValue;
            foreach (var w in waypoints)
            {
                if (Vector3.Distance(Flat(from), w) < 0.6f || !ClearPath(from, w)) continue;
                float cost = Vector3.Distance(Flat(from), w) + Vector3.Distance(w, Flat(to)) * (ClearPath(w, to) ? 1f : 2.5f);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = w;
                }
            }
            return best;
        }

        /// <summary>Punto aleatorio sobre suelo firme (para que los bots paseen).</summary>
        public Vector3 RandomSafePoint(float margin = 1.2f)
        {
            for (int i = 0; i < 12; i++)
            {
                var p = new Vector3(Random.Range(-HalfW, HalfW), 0f, Random.Range(-HalfD, HalfD));
                if (EdgeDistance(p) > margin) return p;
            }
            return SpawnPoints.Count > 0 ? SpawnPoints[Random.Range(0, SpawnPoints.Count)] : Vector3.zero;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static float PolyInside(List<Vector2> poly, Vector2 q)
        {
            float best = float.MaxValue;
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, (a + ab * t - q).sqrMagnitude);
                if ((b.y > q.y) != (a.y > q.y) && q.x < (a.x - b.x) * (q.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
            }
            float d = Mathf.Sqrt(best);
            return inside ? d : -d;
        }

        static List<Vector2> Rect(float x0, float z0, float x1, float z1) =>
            new List<Vector2> { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };

        /// <summary>Contorno tipo "rectangulo redondeado" (superelipse) con algo de irregularidad natural.</summary>
        static List<Vector2> SuperEllipse(float rx, float rz, float power, int n, float jitter, int seed)
        {
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / power) * rx;
                float z = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / power) * rz;
                float k = 1f + (Mathf.PerlinNoise(i * 0.31f + seed * 1.7f, seed * 0.9f) - 0.5f) * 2f * jitter / Mathf.Max(rx, rz);
                pts.Add(new Vector2(x * k, z * k));
            }
            return pts;
        }

        static List<Vector2> Ellipse(float rx, float rz, int n)
        {
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                pts.Add(new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * rz));
            }
            return pts;
        }

        void RingPoints(List<Vector3> list, float rx, float rz, int count, float offset, float y)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count + offset;
                list.Add(new Vector3(Mathf.Cos(a) * rx, y, Mathf.Sin(a) * rz));
            }
        }

        // ------------------------------------------------------------------ Luz y atmosfera

        void Lighting(Color sunColor, float sunIntensity, Vector3 sunEuler, Color rimColor, float rimIntensity,
                      Color ambSky, Color ambEquator, Color ambGround,
                      Color skyTop, Color skyHorizon, Color skyBottom, Color skyClouds,
                      Color fog, float fogStart, float fogEnd)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(sunEuler);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.3f;
            RenderSettings.sun = sun;

            // Contraluz para recortar las siluetas (brillos de borde).
            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(transform, false);
            rimGo.transform.rotation = Quaternion.Euler(28f, sunEuler.y + 193f, 0f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = rimColor;
            rim.intensity = rimIntensity;
            rim.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambSky;
            RenderSettings.ambientEquatorColor = ambEquator;
            RenderSettings.ambientGroundColor = ambGround;

            var sky = new Material(Gfx.GetShader("LB/Sky", "Skybox/Procedural"));
            if (sky.HasProperty("_TopColor"))
            {
                sky.SetColor("_TopColor", skyTop);
                sky.SetColor("_HorizonColor", skyHorizon);
                sky.SetColor("_BottomColor", skyBottom);
                sky.SetColor("_CloudColor", skyClouds);
            }
            RenderSettings.skybox = sky;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;

            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 60f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.antiAliasing = 4;
        }

        Light PointLight(Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject("PointLight");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        // ------------------------------------------------------------------ Piezas

        Material Paint(Color c)
        {
            string k = ColorUtility.ToHtmlStringRGB(c);
            if (!paints.TryGetValue(k, out var m))
            {
                m = Gfx.Toon(c, 0.8f, 0.78f, 0.22f, Gfx.PaintedWood());
                paints[k] = m;
            }
            return m;
        }

        /// <summary>Bloque de juguete con bordes redondeados. min/max en coordenadas del mundo.</summary>
        GameObject Block(string name, Vector3 min, Vector3 max, Material mat, bool collider = true)
        {
            Vector3 size = max - min;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = (min + max) * 0.5f;
            var offset = new Vector2(Random.Range(0f, 10f), Random.Range(0f, 10f));
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.RoundedBox(size, Bevel, 0.42f, offset);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = size;
                Compat.SetPhysMat(bc, 0.6f, 0f, false);
            }
            return go;
        }

        /// <summary>Bloque girado sobre el eje vertical (muros curvos, hielo...). center = centro de la base.</summary>
        GameObject RotBlock(string name, Vector3 baseCenter, Vector3 size, float yaw, Material mat, bool collider = true, float bevel = Bevel)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(baseCenter + Vector3.up * size.y * 0.5f, Quaternion.Euler(0f, yaw, 0f));
            var offset = new Vector2(Random.Range(0f, 10f), Random.Range(0f, 10f));
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.RoundedBox(size, bevel, 0.42f, offset);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = size;
                Compat.SetPhysMat(bc, 0.6f, 0f, false);
            }
            return go;
        }

        /// <summary>Colision invisible (para suelos cuyo dibujo esta hecho de piezas sueltas).</summary>
        void SolidBox(Vector3 min, Vector3 max)
        {
            var go = new GameObject("Solid");
            go.transform.SetParent(transform, false);
            go.transform.position = (min + max) * 0.5f;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = max - min;
            Compat.SetPhysMat(bc, 0.6f, 0f, false);
        }

        /// <summary>Vuelve semitransparente un bloque (misma textura y color, 35% opaco, sin sombra).</summary>
        static void Ghost(GameObject block, float alpha = 0.35f)
        {
            var mr = block.GetComponent<MeshRenderer>();
            var src = mr.sharedMaterial;
            var m = new Material(Gfx.GetShader("LB/ToonFade", "Transparent/Diffuse"));
            m.CopyPropertiesFromMaterial(src);
            var c = src.color;
            c.a = alpha;
            m.color = c;
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Column(float x, float z, float topY, float bottomY, Material ringMat)
        {
            // Columna apilada: bloque cuadrado + cilindro torneado, repetido hasta las nubes.
            float y = topY;
            int k = 0;
            while (y > bottomY)
            {
                Block("ColBlock", new Vector3(x - 0.65f, y - 0.5f, z - 0.65f), new Vector3(x + 0.65f, y, z + 0.65f), k == 0 ? wood : ringMat, false);
                y -= 0.5f;
                float h = 2.6f;
                var cyl = Gfx.Part("ColCyl", transform, Gfx.Prim(PrimitiveType.Cylinder), wood, new Vector3(x, y - h * 0.5f, z), new Vector3(0.95f, h * 0.5f, 0.95f));
                cyl.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                y -= h;
                k++;
            }
        }

        /// <summary>Isla con cara superior plana caminable (MeshCollider) y laterales de roca.</summary>
        GameObject IslandMesh(string name, List<Vector2> outline, MeshBuilder.Ring[] rings, float bottomY, Material top, Material side, int seed, bool collider)
        {
            var mesh = MeshBuilder.Island(outline, 0f, rings, bottomY, 0.25f, seed);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { top, side };
            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                Compat.SetPhysMat(mc, 0.6f, 0f, false);
            }
            return go;
        }

        void Water(float y, float size, Color deep, Color shallow, Color sparkle)
        {
            var m = new Material(Gfx.GetShader("LB/Water", "Unlit/Color"));
            if (m.HasProperty("_DeepColor"))
            {
                m.SetColor("_DeepColor", deep);
                m.SetColor("_ShallowColor", shallow);
                m.SetColor("_SparkleColor", sparkle);
            }
            else m.color = shallow;
            var go = Gfx.Part("Water", transform, MeshBuilder.Grid(size, Mathf.Clamp(Mathf.RoundToInt(size / 2f), 8, 120)), m, new Vector3(0f, y, 0f), Vector3.one);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Rocks(int seed, int count, float radMin, float radMax, float yMin, float yMax, float sMin, float sMax, Material[] mats, float frontDrop)
        {
            var rnd = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float rad = radMin + (float)rnd.NextDouble() * (radMax - radMin);
                Vector3 p = new Vector3(Mathf.Cos(ang) * rad * 1.15f, yMin + (float)rnd.NextDouble() * (yMax - yMin), Mathf.Sin(ang) * rad);
                if (p.z < -8f) p.y -= frontDrop; // las de delante, mas abajo para no tapar la camara
                float s = sMin + (float)rnd.NextDouble() * (sMax - sMin);
                var go = new GameObject("Rock");
                go.transform.SetParent(transform, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 40f - 20f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 40f - 20f);
                go.transform.localScale = new Vector3(s, s * (0.7f + (float)rnd.NextDouble() * 0.5f), s * (0.8f + (float)rnd.NextDouble() * 0.4f));
                go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.Rock(seed * 100 + i, 0.3f);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[i % mats.Length];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        ParticleSystem Clouds(Vector3 center, Vector3 box, int count, Vector2 size, float alpha, Color c)
        {
            var go = new GameObject("Clouds");
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 10f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = count / 22f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.2f), new GradientAlphaKey(alpha, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Gfx.Particle(false);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 50f; // dibujar las nubes por detras de los efectos
            ps.Play();
            return ps;
        }

        /// <summary>Particulas pequenas que caen (nieve) o suben (brasas).</summary>
        void Drift(string name, Vector3 center, Vector3 box, int count, Vector2 size, Vector2 lifetime, Vector3 velocity, Color c, bool additive, float boost)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = c;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = count / ((lifetime.x + lifetime.y) * 0.5f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(velocity.x - 0.4f, velocity.x + 0.4f);
            vel.y = new ParticleSystem.MinMaxCurve(velocity.y * 0.7f, velocity.y * 1.3f);
            vel.z = new ParticleSystem.MinMaxCurve(velocity.z - 0.4f, velocity.z + 0.4f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Gfx.Particle(additive, boost);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        // ================================================================== 0. Puente de Bloques

        void BuildBlocks()
        {
            HalfW = 9f;
            HalfD = 6f;
            Lighting(new Color(1f, 0.9f, 0.74f), 1.3f, new Vector3(50f, -28f, 0f), new Color(0.45f, 0.55f, 0.85f), 0.45f,
                new Color(0.34f, 0.34f, 0.4f), new Color(0.24f, 0.22f, 0.2f), new Color(0.12f, 0.1f, 0.09f),
                new Color(0.05f, 0.045f, 0.045f), new Color(0.14f, 0.13f, 0.12f), new Color(0.07f, 0.065f, 0.06f), new Color(0.17f, 0.16f, 0.15f),
                new Color(0.1f, 0.09f, 0.085f), 32f, 90f);

            float top = 0f, bot = -FloorThick;
            float mx = 3.6f;
            floors.Add(MeshBuilder.RoundedRect(HalfW, HalfD, 0.15f, 2));

            // Suelo: madera a los lados y bloques pintados en el centro (como el puente de BombSquad).
            Block("WoodL1", new Vector3(-HalfW, bot, -HalfD), new Vector3(-mx, top, 0f), wood);
            Block("WoodL2", new Vector3(-HalfW, bot, 0f), new Vector3(-mx, top, HalfD), wood);
            Block("WoodR1", new Vector3(mx, bot, -HalfD), new Vector3(HalfW, top, 0f), wood);
            Block("WoodR2", new Vector3(mx, bot, 0f), new Vector3(HalfW, top, HalfD), wood);
            Color[] front = { Orange, Yellow, Green };
            Color[] back = { Yellow, Green, Orange };
            float w3 = (2f * mx) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x0 = -mx + i * w3, x1 = x0 + w3;
                Block("PaintF" + i, new Vector3(x0, bot, -HalfD), new Vector3(x1, top, 0f), Paint(front[i]));
                Block("PaintB" + i, new Vector3(x0, bot, 0f), new Vector3(x1, top, HalfD), Paint(back[i]));
            }

            // Barandas altas (mas que un salto): solo se sale volando por explosiones o golpes fuertes.
            const float rh = 1.6f, rt = 0.36f;
            float zf = HalfD - rt;
            // Baranda delantera semitransparente: no tapa a los personajes que esten pegados a ella.
            Ghost(Block("RailFL", new Vector3(-HalfW, top, -HalfD), new Vector3(-mx, top + rh, -zf), wood));
            Ghost(Block("RailFM", new Vector3(-mx, top, -HalfD), new Vector3(mx, top + rh, -zf), Paint(Red)));
            Ghost(Block("RailFR", new Vector3(mx, top, -HalfD), new Vector3(HalfW, top + rh, -zf), wood));
            Block("RailBL", new Vector3(-HalfW, top, zf), new Vector3(-mx, top + rh, HalfD), wood);
            Block("RailBM", new Vector3(-mx, top, zf), new Vector3(mx, top + rh, HalfD), Paint(Blue));
            Block("RailBR", new Vector3(mx, top, zf), new Vector3(HalfW, top + rh, HalfD), wood);
            Block("RailL", new Vector3(-HalfW, top, -zf), new Vector3(-HalfW + rt, top + rh, zf), Paint(Red));
            Block("RailR", new Vector3(HalfW - rt, top, -zf), new Vector3(HalfW, top + rh, zf), Paint(Blue));

            // Columnas que bajan hasta las nubes.
            foreach (float x in new[] { -6.6f, -3.6f, 0f, 3.6f, 6.6f })
                foreach (float z in new[] { -3.4f, 3.4f })
                    Column(x, z, bot, -16f, Paint(x < 0f ? Orange : (x > 0f ? Green : Yellow)));

            // Arcos pintados colgando bajo el tramo central (solo decorativos).
            for (int i = 0; i < 3; i++)
            {
                float x0 = -mx + i * w3 + 0.05f, x1 = x0 + w3 - 0.1f;
                foreach (float zs in new[] { -1f, 1f })
                {
                    float zA = zs * (HalfD - 0.5f), zB = zs * (HalfD - 1.2f);
                    Block("Arch", new Vector3(x0, bot - 0.7f, Mathf.Min(zA, zB)), new Vector3(x1, bot, Mathf.Max(zA, zB)), Paint(front[i]), false);
                }
            }

            // Fondo: rocas oscuras desenfocadas, suelo lejano y mar de nubes.
            Rocks(7, 34, 19f, 37f, -14f, 2f, 5f, 14f, new[]
            {
                Gfx.Toon(new Color(0.42f, 0.39f, 0.36f), 0.05f, 0.2f, 0.05f),
                Gfx.Toon(new Color(0.34f, 0.32f, 0.3f), 0.05f, 0.2f, 0.05f),
                Gfx.Toon(new Color(0.5f, 0.46f, 0.4f), 0.05f, 0.2f, 0.05f),
            }, 8f);
            var floor = Gfx.Part("FarFloor", transform, Gfx.Prim(PrimitiveType.Plane), Gfx.Toon(new Color(0.1f, 0.09f, 0.08f), 0f, 0.1f, 0f), new Vector3(0f, -18f, 0f), new Vector3(30f, 1f, 30f));
            floor.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Color cloud = new Color(0.82f, 0.8f, 0.78f);
            Clouds(new Vector3(0f, -9f, 2f), new Vector3(70f, 5f, 55f), 260, new Vector2(7f, 14f), 0.5f, cloud);
            Clouds(new Vector3(0f, -4.5f, 0f), new Vector3(34f, 2f, 26f), 70, new Vector2(3f, 6f), 0.32f, cloud);

            RingPoints(SpawnPoints, 6.3f, 3.7f, 8, Mathf.PI / 8f, 0.15f);
            PowerupPoints.AddRange(new[]
            {
                new Vector3(-5f, 2.5f, 2.6f), new Vector3(5f, 2.5f, 2.6f),
                new Vector3(-5f, 2.5f, -2.6f), new Vector3(5f, 2.5f, -2.6f),
                new Vector3(0f, 2.5f, 3.8f), new Vector3(0f, 2.5f, -3.8f),
                new Vector3(-2.6f, 2.5f, 0f), new Vector3(2.6f, 2.5f, 0f),
            });
            TntSpots.Add(new Vector3(-7f, 0.02f, 0f));
            TntSpots.Add(new Vector3(7f, 0.02f, 0f));
        }

        // ================================================================== 1. Isla Tropical

        void BuildTropical()
        {
            HalfW = 9f;
            HalfD = 6f;
            Lighting(new Color(1f, 0.93f, 0.8f), 1.25f, new Vector3(52f, -35f, 0f), new Color(0.55f, 0.75f, 1f), 0.4f,
                new Color(0.52f, 0.62f, 0.75f), new Color(0.42f, 0.46f, 0.42f), new Color(0.2f, 0.24f, 0.2f),
                new Color(0.18f, 0.42f, 0.86f), new Color(0.72f, 0.86f, 0.98f), new Color(0.3f, 0.55f, 0.8f), new Color(1f, 1f, 1f),
                new Color(0.62f, 0.78f, 0.92f), 45f, 150f);

            var grass = Gfx.Toon(Color.white, 0.08f, 0.25f, 0.18f, Gfx.Grass());
            var sand = Gfx.Toon(new Color(1f, 0.93f, 0.75f), 0.05f, 0.2f, 0.1f, Gfx.Rock());
            var outline = SuperEllipse(9f, 6f, 4f, 72, 0.35f, 3);
            floors.Add(outline);
            IslandMesh("Island", outline, new[]
            {
                new MeshBuilder.Ring(-0.6f, 1.03f, 0.03f),
                new MeshBuilder.Ring(-1.6f, 1.06f, 0.06f),
                new MeshBuilder.Ring(-3.2f, 0.95f, 0.1f),
                new MeshBuilder.Ring(-6f, 0.7f, 0.12f),
            }, -9f, grass, sand, 3, true);

            // Palmeras al fondo (el tronco estorba: sirve de cobertura) y una roca.
            Palm(new Vector3(-6.6f, 0f, 3.7f), 14f);
            Palm(new Vector3(6.4f, 0f, 3.9f), -18f);
            Palm(new Vector3(-7.4f, 0f, -2.8f), 160f);
            var rockMat = Gfx.Toon(new Color(0.62f, 0.58f, 0.52f), 0.05f, 0.25f, 0.1f, Gfx.Rock());
            var boulder = Gfx.Part("Boulder", transform, MeshBuilder.Rock(41, 0.25f), rockMat, new Vector3(0.4f, 0.1f, 4.3f), new Vector3(1.7f, 1.2f, 1.3f));
            var bmc = boulder.AddComponent<MeshCollider>();
            bmc.convex = true;
            bmc.sharedMesh = boulder.GetComponent<MeshFilter>().sharedMesh;

            // Mar e islas lejanas.
            Water(-3f, 260f, new Color(0.03f, 0.3f, 0.5f), new Color(0.15f, 0.62f, 0.75f), new Color(0.85f, 0.97f, 1f));
            var rnd = new System.Random(5);
            for (int i = 0; i < 9; i++)
            {
                float ang = Mathf.Lerp(0.15f, 0.85f, (float)rnd.NextDouble()) * Mathf.PI;
                float rad = 38f + (float)rnd.NextDouble() * 45f;
                float s = 4f + (float)rnd.NextDouble() * 7f;
                var o = SuperEllipse(s, s * 0.7f, 2.5f, 32, s * 0.15f, 20 + i);
                var isl = IslandMesh("FarIsland", o, new[] { new MeshBuilder.Ring(-1.5f, 1.05f, 0.1f), new MeshBuilder.Ring(-4f, 0.8f, 0.1f) }, -6f, grass, sand, 20 + i, false);
                isl.transform.position = new Vector3(Mathf.Cos(ang) * rad * 1.3f, -2.4f + (float)rnd.NextDouble() * 1.5f, Mathf.Sin(ang) * rad);
                isl.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                isl.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Clouds(new Vector3(0f, 16f, 70f), new Vector3(160f, 8f, 30f), 50, new Vector2(14f, 26f), 0.75f, Color.white);

            RingPoints(SpawnPoints, 6.2f, 3.8f, 8, Mathf.PI / 8f, 0.15f);
            PowerupPoints.AddRange(new[]
            {
                new Vector3(-4.6f, 2.5f, 2.4f), new Vector3(4.6f, 2.5f, 2.4f),
                new Vector3(-4.6f, 2.5f, -2.4f), new Vector3(4.6f, 2.5f, -2.4f),
                new Vector3(0f, 2.5f, 2.2f), new Vector3(0f, 2.5f, -3.6f),
                new Vector3(-2.4f, 2.5f, 0f), new Vector3(2.4f, 2.5f, 0f),
            });
            TntSpots.Add(new Vector3(-7.2f, 0.02f, 0.4f));
            TntSpots.Add(new Vector3(7.2f, 0.02f, -0.4f));
        }

        void Palm(Vector3 basePos, float yaw)
        {
            var trunkMat = Gfx.Toon(new Color(0.62f, 0.45f, 0.28f), 0.05f, 0.2f, 0.1f, Gfx.Wood());
            var leafMat = Gfx.Toon(new Color(0.22f, 0.62f, 0.2f), 0.25f, 0.4f, 0.25f);
            var root = new GameObject("Palm");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(basePos, Quaternion.Euler(0f, yaw, 0f));

            // Tronco curvado hecho de segmentos.
            Vector3 p = Vector3.zero;
            Vector3 dir = Vector3.up;
            for (int i = 0; i < 6; i++)
            {
                float len = 0.75f;
                float r = Mathf.Lerp(0.42f, 0.28f, i / 5f);
                var seg = Gfx.Part("Trunk", root.transform, Gfx.Prim(PrimitiveType.Cylinder), trunkMat, p + dir * len * 0.5f, new Vector3(r, len * 0.55f, r));
                seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                p += dir * len;
                dir = Quaternion.Euler(0f, 0f, -6f) * dir;
            }
            // Hojas: elipsoides alargados colgando.
            for (int k = 0; k < 7; k++)
            {
                var leaf = Gfx.Part("Leaf", root.transform, Gfx.Prim(PrimitiveType.Sphere), leafMat, p, new Vector3(0.5f, 0.08f, 2.1f));
                leaf.transform.localRotation = Quaternion.Euler(0f, k * 360f / 7f, 0f) * Quaternion.Euler(28f, 0f, 0f);
                leaf.transform.localPosition = p + leaf.transform.localRotation * new Vector3(0f, 0f, 0.9f);
            }
            var coco = Gfx.Toon(new Color(0.35f, 0.22f, 0.12f), 0.3f, 0.4f, 0.1f);
            for (int k = 0; k < 3; k++)
                Gfx.Part("Coconut", root.transform, Gfx.Prim(PrimitiveType.Sphere), coco, p + Quaternion.Euler(0f, k * 120f, 0f) * new Vector3(0.22f, -0.25f, 0f), Vector3.one * 0.28f);

            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.2f, 0f);
            cap.height = 2.4f;
            cap.radius = 0.38f;
        }

        // ================================================================== 2. Tres Islas

        void BuildThreeIslands()
        {
            HalfW = 11f;
            HalfD = 4.5f;
            CamX = new Vector2(-6f, 6f);
            CamZ = new Vector2(-2f, 2.5f);
            CamMaxDist = 29f;
            foreach (float x in new[] { -6.2f, -3.5f, 0f, 3.5f, 6.2f }) waypoints.Add(new Vector3(x, 0f, 0f));
            Lighting(new Color(1f, 0.72f, 0.48f), 1.2f, new Vector3(26f, -62f, 0f), new Color(0.62f, 0.45f, 0.95f), 0.55f,
                new Color(0.46f, 0.36f, 0.48f), new Color(0.46f, 0.32f, 0.27f), new Color(0.16f, 0.1f, 0.12f),
                new Color(0.17f, 0.11f, 0.33f), new Color(1f, 0.56f, 0.3f), new Color(0.5f, 0.28f, 0.38f), new Color(1f, 0.72f, 0.6f),
                new Color(0.72f, 0.48f, 0.44f), 34f, 110f);

            float top = 0f, bot = -FloorThick;
            // Plataformas laterales y central.
            float sx0 = 5f, sx1 = 11f, sz = 4.5f, cx = 2f, cz = 2.6f, bz = 0.9f;
            floors.Add(Rect(-sx1, -sz, -sx0, sz));
            floors.Add(Rect(sx0, -sz, sx1, sz));
            floors.Add(Rect(-cx, -cz, cx, cz));
            // Los puentes se solapan con las plataformas: asi la union no cuenta como borde.
            floors.Add(Rect(-sx0 - 1f, -bz, -cx + 1f, bz));
            floors.Add(Rect(cx - 1f, -bz, sx0 + 1f, bz));

            Color[] left = { Blue, Yellow };
            Color[] right = { Red, Orange };
            float xm = (sx0 + sx1) * 0.5f;
            for (int ix = 0; ix < 2; ix++)
                for (int iz = 0; iz < 2; iz++)
                {
                    float z0 = iz == 0 ? -sz : 0f, z1 = iz == 0 ? 0f : sz;
                    bool w = (ix + iz) % 2 == 0;
                    float lx0 = ix == 0 ? -sx1 : -xm, lx1 = ix == 0 ? -xm : -sx0;
                    Block("Left", new Vector3(lx0, bot, z0), new Vector3(lx1, top, z1), w ? wood : Paint(left[ix]));
                    float rx0 = ix == 0 ? sx0 : xm, rx1 = ix == 0 ? xm : sx1;
                    Block("Right", new Vector3(rx0, bot, z0), new Vector3(rx1, top, z1), w ? Paint(right[ix]) : wood);
                }
            Block("CenterA", new Vector3(-cx, bot, -cz), new Vector3(0f, top, cz), Paint(Green));
            Block("CenterB", new Vector3(0f, bot, -cz), new Vector3(cx, top, cz), Paint(Yellow));

            // Puentes de tablones (dibujo) sobre una colision continua.
            var plank = Gfx.Toon(new Color(0.85f, 0.7f, 0.5f), 0.1f, 0.3f, 0.1f, Gfx.Wood());
            var rope = Gfx.Toon(new Color(0.75f, 0.62f, 0.4f), 0.05f, 0.2f, 0.1f);
            foreach (float s in new[] { -1f, 1f })
            {
                float a = s < 0f ? -sx0 : cx, b = s < 0f ? -cx : sx0;
                SolidBox(new Vector3(a, -0.45f, -bz), new Vector3(b, top, bz));
                int n = 6;
                float step = (b - a) / n;
                for (int i = 0; i < n; i++)
                    Block("Plank", new Vector3(a + i * step + 0.03f, -0.35f, -bz), new Vector3(a + (i + 1) * step - 0.03f, top, bz), plank, false);
                foreach (float z in new[] { -bz - 0.05f, bz + 0.05f })
                {
                    foreach (float x in new[] { a + 0.15f, b - 0.15f })
                        Gfx.Part("Post", transform, Gfx.Prim(PrimitiveType.Cylinder), wood, new Vector3(x, 0.35f, z), new Vector3(0.14f, 0.35f, 0.14f));
                    Gfx.Part("Rope", transform, Gfx.Prim(PrimitiveType.Cylinder), rope, new Vector3((a + b) * 0.5f, 0.55f, z), new Vector3(0.05f, (b - a) * 0.5f - 0.1f, 0.05f))
                        .transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }

            foreach (float x in new[] { -10f, -6f, 6f, 10f })
                foreach (float z in new[] { -3.3f, 3.3f })
                    Column(x, z, bot, -16f, Paint(x < 0f ? Blue : Red));
            Column(0f, -1.4f, bot, -16f, Paint(Green));
            Column(0f, 1.4f, bot, -16f, Paint(Yellow));

            Rocks(13, 30, 22f, 40f, -14f, 3f, 5f, 13f, new[]
            {
                Gfx.Toon(new Color(0.55f, 0.38f, 0.38f), 0.05f, 0.2f, 0.05f),
                Gfx.Toon(new Color(0.42f, 0.3f, 0.36f), 0.05f, 0.2f, 0.05f),
                Gfx.Toon(new Color(0.62f, 0.45f, 0.4f), 0.05f, 0.2f, 0.05f),
            }, 8f);
            Color cloud = new Color(1f, 0.74f, 0.64f);
            Clouds(new Vector3(0f, -9f, 2f), new Vector3(80f, 5f, 55f), 260, new Vector2(7f, 14f), 0.55f, cloud);
            Clouds(new Vector3(0f, -4.5f, 0f), new Vector3(40f, 2f, 22f), 70, new Vector2(3f, 6f), 0.32f, cloud);

            SpawnPoints.AddRange(new[]
            {
                new Vector3(-9.3f, 0.15f, -2.8f), new Vector3(9.3f, 0.15f, 2.8f),
                new Vector3(-9.3f, 0.15f, 2.8f), new Vector3(9.3f, 0.15f, -2.8f),
                new Vector3(-6.3f, 0.15f, 0f), new Vector3(6.3f, 0.15f, 0f),
                new Vector3(0f, 0.15f, 1.6f), new Vector3(0f, 0.15f, -1.6f),
            });
            PowerupPoints.AddRange(new[]
            {
                new Vector3(-8f, 2.5f, -3.4f), new Vector3(8f, 2.5f, 3.4f),
                new Vector3(-8f, 2.5f, 3.4f), new Vector3(8f, 2.5f, -3.4f),
                new Vector3(-8f, 2.5f, 0f), new Vector3(8f, 2.5f, 0f),
                new Vector3(0f, 2.5f, 0f),
            });
            TntSpots.Add(new Vector3(-9.8f, 0.02f, 0f));
            TntSpots.Add(new Vector3(9.8f, 0.02f, 0f));
        }

        // ================================================================== 3. Coliseo de Lava

        void BuildColosseum()
        {
            HalfW = 8.6f;
            HalfD = 6.2f;
            CamX = new Vector2(-3.5f, 3.5f);
            CamZ = new Vector2(-2.5f, 2.5f);
            Lighting(new Color(0.68f, 0.75f, 1f), 0.95f, new Vector3(55f, 25f, 0f), new Color(1f, 0.5f, 0.25f), 0.55f,
                new Color(0.26f, 0.27f, 0.38f), new Color(0.28f, 0.2f, 0.17f), new Color(0.32f, 0.13f, 0.05f),
                new Color(0.02f, 0.02f, 0.07f), new Color(0.13f, 0.08f, 0.15f), new Color(0.05f, 0.03f, 0.04f), new Color(0.16f, 0.12f, 0.2f),
                new Color(0.06f, 0.05f, 0.08f), 30f, 95f);

            const int n = 48;
            float rx = 8.6f, rz = 6.2f, px = 2.1f, pz = 1.7f;
            var outer = Ellipse(rx, rz, n);
            var pit = Ellipse(px, pz, n);
            floors.Add(outer);
            holes.Add(pit);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                waypoints.Add(new Vector3(Mathf.Cos(a) * (px + 1.5f), 0f, Mathf.Sin(a) * (pz + 1.5f)));
            }

            var floorMat = Gfx.Toon(new Color(0.9f, 0.8f, 0.64f), 0.1f, 0.3f, 0.15f, Gfx.Tiles());
            var stone = Gfx.Toon(new Color(0.42f, 0.38f, 0.36f), 0.05f, 0.2f, 0.1f, Gfx.Rock());
            var floorMesh = MeshBuilder.Annulus(outer, pit, 0f, -FloorThick, -8f, 0.2f);
            var fl = new GameObject("Floor");
            fl.transform.SetParent(transform, false);
            fl.AddComponent<MeshFilter>().sharedMesh = floorMesh;
            fl.AddComponent<MeshRenderer>().sharedMaterials = new[] { floorMat, stone };
            var mc = fl.AddComponent<MeshCollider>();
            mc.sharedMesh = floorMesh;
            Compat.SetPhysMat(mc, 0.6f, 0f, false);

            // Borde del pozo: anillo de piedra oscura a ras de suelo.
            var rimMat = Gfx.Toon(new Color(0.25f, 0.2f, 0.19f), 0.1f, 0.3f, 0.2f, Gfx.Rock());
            var rimMesh = MeshBuilder.Annulus(Ellipse(px + 0.45f, pz + 0.45f, n), pit, 0.02f, -0.1f, -0.1f, 0.3f);
            Gfx.Part("PitRim", transform, rimMesh, rimMat, Vector3.zero, Vector3.one).GetComponent<MeshRenderer>().sharedMaterials = new[] { rimMat, rimMat };

            // Lava al fondo del pozo (el shader del agua con colores de fuego) y su resplandor.
            var lava = new Material(Gfx.GetShader("LB/Water", "Unlit/Color"));
            if (lava.HasProperty("_DeepColor"))
            {
                lava.SetColor("_DeepColor", new Color(0.55f, 0.05f, 0f));
                lava.SetColor("_ShallowColor", new Color(1f, 0.38f, 0.04f));
                lava.SetColor("_SparkleColor", new Color(1f, 0.88f, 0.35f));
            }
            Gfx.Part("Lava", transform, MeshBuilder.Grid(8f, 16), lava, new Vector3(0f, -5.2f, 0f), Vector3.one)
                .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PointLight(new Vector3(0f, -1.8f, 0f), new Color(1f, 0.42f, 0.1f), 3.2f, 7.5f);
            Drift("Embers", new Vector3(0f, -3f, 0f), new Vector3(3.4f, 0.5f, 2.6f), 70, new Vector2(0.04f, 0.11f), new Vector2(2.2f, 3.5f),
                new Vector3(0f, 1.6f, 0f), new Color(1f, 0.55f, 0.15f), true, 2f);

            // Muro que rodea la arena (el de delante, semitransparente) con pilares y antorchas.
            const int segs = 28;
            float wr = 0.5f, wh = 1.6f;
            var wallA = Gfx.Toon(new Color(0.72f, 0.62f, 0.5f), 0.08f, 0.3f, 0.12f, Gfx.Rock());
            var wallB = Gfx.Toon(new Color(0.6f, 0.5f, 0.42f), 0.08f, 0.3f, 0.12f, Gfx.Rock());
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector2 p0 = new Vector2(Mathf.Cos(a0) * (rx + wr * 0.5f), Mathf.Sin(a0) * (rz + wr * 0.5f));
                Vector2 p1 = new Vector2(Mathf.Cos(a1) * (rx + wr * 0.5f), Mathf.Sin(a1) * (rz + wr * 0.5f));
                Vector2 mid = (p0 + p1) * 0.5f, d = p1 - p0;
                float yaw = -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                var seg = RotBlock("Wall", new Vector3(mid.x, -0.05f, mid.y), new Vector3(d.magnitude + 0.12f, wh, wr), yaw, i % 2 == 0 ? wallA : wallB);
                if (mid.y < -1.5f) Ghost(seg);

                if (i % 4 == 0)
                {
                    var pillar = RotBlock("Pillar", new Vector3(p0.x, -0.05f, p0.y), new Vector3(0.8f, wh + 0.6f, 0.8f), yaw, stone);
                    if (p0.y < -1.5f) Ghost(pillar);
                    else Torch(new Vector3(p0.x, wh + 0.55f, p0.y));
                }
            }

            // Graderio exterior (detras de un foso: lo que sale volando por encima del muro, cae).
            var standMat = Gfx.Toon(new Color(0.3f, 0.27f, 0.27f), 0.05f, 0.2f, 0.08f, Gfx.Rock());
            for (int k = 0; k < 6; k++)
            {
                float o = 4.2f + k * 1.3f;
                var st = MeshBuilder.Annulus(Ellipse(rx + o + 1.3f, rz + o + 1.3f, 40), Ellipse(rx + o, rz + o, 40), 0.4f + k * 0.8f, -6f, -6f, 0.3f);
                var sgo = Gfx.Part("Stand", transform, st, standMat, Vector3.zero, Vector3.one);
                sgo.GetComponent<MeshRenderer>().sharedMaterials = new[] { standMat, standMat };
                sgo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Lerp(0.15f, 0.85f, i / 5f) * Mathf.PI;
                Torch(new Vector3(Mathf.Cos(a) * (rx + 12f), 5.6f, Mathf.Sin(a) * (rz + 12f)));
            }

            RingPoints(SpawnPoints, 6.4f, 4.4f, 8, Mathf.PI / 8f, 0.15f);
            RingPoints(PowerupPoints, 4.4f, 3.2f, 8, 0f, 2.5f);
            TntSpots.Add(new Vector3(0f, 0.02f, 4.5f));
            TntSpots.Add(new Vector3(0f, 0.02f, -4.5f));
        }

        void Torch(Vector3 pos)
        {
            var metal = Gfx.Toon(new Color(0.2f, 0.18f, 0.17f), 0.4f, 0.5f, 0.1f);
            Gfx.Part("TorchBowl", transform, Gfx.Prim(PrimitiveType.Cylinder), metal, pos, new Vector3(0.45f, 0.1f, 0.45f));
            var flame = Gfx.Part("Flame", transform, Gfx.Prim(PrimitiveType.Sphere), Gfx.Unlit(new Color(1f, 0.62f, 0.2f), false), pos + Vector3.up * 0.22f, new Vector3(0.3f, 0.45f, 0.3f));
            flame.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Drift("Fire", pos + Vector3.up * 0.2f, new Vector3(0.25f, 0.1f, 0.25f), 24, new Vector2(0.12f, 0.28f), new Vector2(0.4f, 0.8f),
                new Vector3(0f, 1.4f, 0f), new Color(1f, 0.5f, 0.12f), true, 2.2f);
            PointLight(pos + Vector3.up * 0.6f, new Color(1f, 0.6f, 0.28f), 1.4f, 6f);
        }

        // ================================================================== 4. Cumbre Nevada

        void BuildSnow()
        {
            HalfW = 9f;
            HalfD = 6.2f;
            Lighting(new Color(1f, 0.97f, 0.92f), 1.1f, new Vector3(42f, -20f, 0f), new Color(0.6f, 0.75f, 1f), 0.5f,
                new Color(0.52f, 0.57f, 0.68f), new Color(0.42f, 0.45f, 0.5f), new Color(0.3f, 0.32f, 0.38f),
                new Color(0.32f, 0.46f, 0.72f), new Color(0.82f, 0.87f, 0.93f), new Color(0.66f, 0.72f, 0.8f), new Color(1f, 1f, 1f),
                new Color(0.76f, 0.81f, 0.88f), 32f, 115f);

            var snow = Gfx.Toon(new Color(0.86f, 0.89f, 0.94f), 0.35f, 0.5f, 0.3f);
            var cliff = Gfx.Toon(new Color(0.7f, 0.74f, 0.82f), 0.05f, 0.2f, 0.1f, Gfx.Rock());
            var outline = SuperEllipse(9f, 6.2f, 2.6f, 64, 0.3f, 11);
            floors.Add(outline);
            IslandMesh("Peak", outline, new[]
            {
                new MeshBuilder.Ring(-0.5f, 1.03f, 0.04f),
                new MeshBuilder.Ring(-2f, 1.0f, 0.1f),
                new MeshBuilder.Ring(-5f, 0.86f, 0.16f),
                new MeshBuilder.Ring(-10f, 0.62f, 0.2f),
                new MeshBuilder.Ring(-16f, 0.4f, 0.2f),
            }, -22f, snow, cliff, 11, true);

            // Pinos y bloques de hielo: cobertura contra las explosiones.
            Pine(new Vector3(-6.8f, 0f, 3.4f), 1.1f);
            Pine(new Vector3(6.6f, 0f, -3.3f), 0.95f);
            Pine(new Vector3(6.2f, 0f, 3.6f), 1.2f);
            var ice = Gfx.Toon(new Color(0.62f, 0.86f, 1f), 0.9f, 0.85f, 0.5f);
            foreach (var (pos, yaw) in new[] { (new Vector3(-1.8f, 0f, -1.6f), 20f), (new Vector3(2f, 0f, 1.5f), -15f) })
            {
                var b = RotBlock("Ice", pos, new Vector3(1.15f, 1.0f, 1.15f), yaw, ice, true, 0.18f);
                Ghost(b, 0.78f);
                Compat.SetPhysMat(b.GetComponent<Collider>(), 0.05f, 0f, true);
            }
            Snowman(new Vector3(0.3f, 0f, 4.7f));

            // Montanas al fondo, nubes abajo y nevada.
            Rocks(17, 26, 34f, 60f, -18f, -4f, 14f, 30f, new[]
            {
                Gfx.Toon(new Color(0.82f, 0.85f, 0.9f), 0.1f, 0.3f, 0.1f, Gfx.Rock()),
                Gfx.Toon(new Color(0.66f, 0.7f, 0.78f), 0.1f, 0.3f, 0.1f, Gfx.Rock()),
                Gfx.Toon(new Color(0.92f, 0.94f, 0.97f), 0.1f, 0.3f, 0.1f),
            }, 10f);
            Clouds(new Vector3(0f, -10f, 2f), new Vector3(90f, 5f, 60f), 260, new Vector2(8f, 15f), 0.6f, Color.white);
            Clouds(new Vector3(0f, -5f, 0f), new Vector3(36f, 2f, 26f), 60, new Vector2(3f, 6f), 0.3f, Color.white);
            Drift("Snowfall", new Vector3(0f, 12f, 1f), new Vector3(40f, 1f, 30f), 700, new Vector2(0.05f, 0.13f), new Vector2(7f, 10f),
                new Vector3(0.4f, -1.8f, 0f), new Color(1f, 1f, 1f, 0.85f), false, 1f);

            RingPoints(SpawnPoints, 6.3f, 3.9f, 8, Mathf.PI / 8f, 0.15f);
            PowerupPoints.AddRange(new[]
            {
                new Vector3(-4.6f, 2.5f, 0f), new Vector3(4.6f, 2.5f, 0f),
                new Vector3(0f, 2.5f, -3.4f), new Vector3(0f, 2.5f, 2.6f),
                new Vector3(-3.8f, 2.5f, 2.6f), new Vector3(3.8f, 2.5f, -2.4f),
                new Vector3(-3.9f, 2.5f, -2.8f), new Vector3(3.6f, 2.5f, 3.4f),
            });
            TntSpots.Add(new Vector3(-7.5f, 0.02f, 0f));
            TntSpots.Add(new Vector3(7.5f, 0.02f, 0f));
        }

        void Pine(Vector3 basePos, float scale)
        {
            var bark = Gfx.Toon(new Color(0.42f, 0.28f, 0.18f), 0.05f, 0.2f, 0.05f, Gfx.Wood());
            var needles = Gfx.Toon(new Color(0.1f, 0.36f, 0.24f), 0.15f, 0.3f, 0.2f);
            var snowCap = Gfx.Toon(new Color(0.9f, 0.93f, 0.97f), 0.3f, 0.5f, 0.3f);
            var root = new GameObject("Pine");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(basePos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            root.transform.localScale = Vector3.one * scale;
            Gfx.Part("Trunk", root.transform, Gfx.Prim(PrimitiveType.Cylinder), bark, new Vector3(0f, 0.45f, 0f), new Vector3(0.32f, 0.45f, 0.32f));
            float y = 0.7f;
            for (int i = 0; i < 3; i++)
            {
                float r = 1.15f - i * 0.3f, h = 1.3f - i * 0.15f;
                Gfx.Part("Needles", root.transform, MeshBuilder.Cone(r, h, 14), needles, new Vector3(0f, y, 0f), Vector3.one);
                Gfx.Part("Snow", root.transform, MeshBuilder.Cone(r * 0.55f, h * 0.45f, 14), snowCap, new Vector3(0f, y + h * 0.56f, 0f), Vector3.one);
                y += h * 0.55f;
            }
            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.2f, 0f);
            cap.height = 2.4f;
            cap.radius = 0.5f;
        }

        void Snowman(Vector3 basePos)
        {
            var white = Gfx.Toon(new Color(0.92f, 0.94f, 0.97f), 0.3f, 0.5f, 0.3f);
            var black = Gfx.Toon(new Color(0.08f, 0.08f, 0.1f), 0.6f, 0.6f, 0.1f);
            var carrot = Gfx.Toon(new Color(1f, 0.5f, 0.1f), 0.3f, 0.4f, 0.1f);
            var root = new GameObject("Snowman");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(basePos, Quaternion.Euler(0f, 180f, 0f));
            Gfx.Part("Base", root.transform, Gfx.Prim(PrimitiveType.Sphere), white, new Vector3(0f, 0.5f, 0f), Vector3.one * 1.1f);
            Gfx.Part("Mid", root.transform, Gfx.Prim(PrimitiveType.Sphere), white, new Vector3(0f, 1.25f, 0f), Vector3.one * 0.8f);
            Gfx.Part("Head", root.transform, Gfx.Prim(PrimitiveType.Sphere), white, new Vector3(0f, 1.85f, 0f), Vector3.one * 0.55f);
            Gfx.Part("Hat", root.transform, Gfx.Prim(PrimitiveType.Cylinder), black, new Vector3(0f, 2.22f, 0f), new Vector3(0.38f, 0.18f, 0.38f));
            Gfx.Part("Brim", root.transform, Gfx.Prim(PrimitiveType.Cylinder), black, new Vector3(0f, 2.06f, 0f), new Vector3(0.6f, 0.02f, 0.6f));
            Gfx.Part("Nose", root.transform, MeshBuilder.Cone(0.06f, 0.35f, 8), carrot, new Vector3(0f, 1.85f, -0.25f), Vector3.one)
                .transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            foreach (float x in new[] { -0.1f, 0.1f })
                Gfx.Part("Eye", root.transform, Gfx.Prim(PrimitiveType.Sphere), black, new Vector3(x, 1.94f, -0.24f), Vector3.one * 0.07f);
            var sc = root.AddComponent<CapsuleCollider>();
            sc.center = new Vector3(0f, 1f, 0f);
            sc.height = 2f;
            sc.radius = 0.55f;
        }
    }
}
