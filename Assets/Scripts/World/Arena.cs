using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Mapa "Puente de Bloques": una plataforma de bloques de juguete (madera natural a los lados,
    /// madera pintada en el centro) sobre columnas que se pierden en un mar de nubes, con rocas
    /// oscuras desenfocadas al fondo e iluminacion calida y teatral, al estilo de BombSquad.
    /// Caerse de la plataforma = muerte.
    /// </summary>
    public class Arena : MonoBehaviour
    {
        public static Arena I;

        public float HalfW = 9f;
        public float HalfD = 6f;
        public float Corner = 0.15f;

        public readonly List<Vector3> SpawnPoints = new List<Vector3>();
        public readonly List<Vector3> PowerupPoints = new List<Vector3>();
        public readonly List<Vector3> TntSpots = new List<Vector3>();

        const float FloorThick = 1.2f;
        const float Bevel = 0.1f;

        Material wood;
        readonly Dictionary<string, Material> paints = new Dictionary<string, Material>();

        static readonly Color Orange = new Color(0.96f, 0.5f, 0.1f);
        static readonly Color Yellow = new Color(0.98f, 0.78f, 0.12f);
        static readonly Color Green = new Color(0.16f, 0.5f, 0.22f);
        static readonly Color Red = new Color(0.82f, 0.16f, 0.12f);
        static readonly Color Blue = new Color(0.16f, 0.36f, 0.82f);

        public static Arena Build()
        {
            var go = new GameObject("Arena");
            var a = go.AddComponent<Arena>();
            I = a;
            a.BuildLighting();
            a.BuildMain();
            a.BuildBackground();
            a.BuildPoints();
            return a;
        }

        /// <summary>Distancia al borde de la plataforma (positiva dentro).</summary>
        public float EdgeDistance(Vector3 p)
        {
            float qx = Mathf.Abs(p.x) - (HalfW - Corner);
            float qz = Mathf.Abs(p.z) - (HalfD - Corner);
            float ox = Mathf.Max(qx, 0f), oz = Mathf.Max(qz, 0f);
            float sdf = Mathf.Sqrt(ox * ox + oz * oz) + Mathf.Min(Mathf.Max(qx, qz), 0f) - Corner;
            return -sdf;
        }

        // ------------------------------------------------------------------ Luz y atmosfera

        void BuildLighting()
        {
            // Luz principal calida desde arriba-delante, con sombras marcadas.
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(50f, -28f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.74f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.3f;
            RenderSettings.sun = sun;

            // Contraluz frio para recortar las siluetas (brillos de borde).
            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(transform, false);
            rimGo.transform.rotation = Quaternion.Euler(28f, 165f, 0f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.45f, 0.55f, 0.85f);
            rim.intensity = 0.45f;
            rim.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.34f, 0.4f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.22f, 0.2f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.1f, 0.09f);

            var sky = new Material(Gfx.GetShader("LB/Sky", "Skybox/Procedural"));
            if (sky.HasProperty("_TopColor"))
            {
                sky.SetColor("_TopColor", new Color(0.05f, 0.045f, 0.045f));
                sky.SetColor("_HorizonColor", new Color(0.14f, 0.13f, 0.12f));
                sky.SetColor("_BottomColor", new Color(0.07f, 0.065f, 0.06f));
                sky.SetColor("_CloudColor", new Color(0.17f, 0.16f, 0.15f));
            }
            RenderSettings.skybox = sky;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.1f, 0.09f, 0.085f);
            RenderSettings.fogStartDistance = 32f;
            RenderSettings.fogEndDistance = 90f;

            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 60f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.antiAliasing = 4;
        }

        // ------------------------------------------------------------------ Plataforma de bloques

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

        /// <summary>Vuelve semitransparente un bloque (misma textura y color, 35% opaco, sin sombra).</summary>
        static void Ghost(GameObject block)
        {
            var mr = block.GetComponent<MeshRenderer>();
            var src = mr.sharedMaterial;
            var m = new Material(Gfx.GetShader("LB/ToonFade", "Transparent/Diffuse"));
            m.CopyPropertiesFromMaterial(src);
            var c = src.color;
            c.a = 0.35f;
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

        void BuildMain()
        {
            wood = Gfx.Toon(Color.white, 0.35f, 0.45f, 0.12f, Gfx.Wood());
            float top = 0f, bot = -FloorThick;
            float mx = 3.6f;

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

            // Barandas bajas de bloques (evitan caidas tontas; se pueden saltar).
            const float rh = 1.6f, rt = 0.36f; // barandas altas (mas que un salto): solo se sale volando por explosiones o golpes fuertes
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
        }

        // ------------------------------------------------------------------ Fondo

        void BuildBackground()
        {
            // Rocas grandes y oscuras alrededor (la profundidad de campo las desenfoca).
            var rnd = new System.Random(7);
            var rockMats = new[]
            {
                Gfx.Toon(new Color(0.42f, 0.39f, 0.36f), 0.05f, 0.2f, 0.05f),
                Gfx.Toon(new Color(0.34f, 0.32f, 0.3f), 0.05f, 0.2f, 0.05f),
                Gfx.Toon(new Color(0.5f, 0.46f, 0.4f), 0.05f, 0.2f, 0.05f),
            };
            for (int i = 0; i < 34; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float rad = 19f + (float)rnd.NextDouble() * 18f;
                Vector3 p = new Vector3(Mathf.Cos(ang) * rad * 1.15f, -14f + (float)rnd.NextDouble() * 16f, Mathf.Sin(ang) * rad);
                if (p.z < -8f) p.y -= 8f; // las de delante, mas abajo para no tapar la camara
                float s = 5f + (float)rnd.NextDouble() * 9f;
                var go = new GameObject("Rock");
                go.transform.SetParent(transform, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 40f - 20f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 40f - 20f);
                go.transform.localScale = new Vector3(s, s * (0.7f + (float)rnd.NextDouble() * 0.5f), s * (0.8f + (float)rnd.NextDouble() * 0.4f));
                go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.Rock(i, 0.3f);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = rockMats[i % rockMats.Length];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Suelo lejano oscuro.
            var floor = Gfx.Part("FarFloor", transform, Gfx.Prim(PrimitiveType.Plane), Gfx.Toon(new Color(0.1f, 0.09f, 0.08f), 0f, 0.1f, 0f), new Vector3(0f, -18f, 0f), new Vector3(30f, 1f, 30f));
            floor.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Mar de nubes bajo la plataforma.
            Clouds(new Vector3(0f, -9f, 2f), new Vector3(70f, 5f, 55f), 260, new Vector2(7f, 14f), 0.5f);
            Clouds(new Vector3(0f, -4.5f, 0f), new Vector3(34f, 2f, 26f), 70, new Vector2(3f, 6f), 0.32f);
        }

        void Clouds(Vector3 center, Vector3 box, int count, Vector2 size, float alpha)
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
            Color c = new Color(0.82f, 0.8f, 0.78f);
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
        }

        void BuildPoints()
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f + Mathf.PI / 8f;
                SpawnPoints.Add(new Vector3(Mathf.Cos(a) * 6.3f, 0.15f, Mathf.Sin(a) * 3.7f));
            }
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
    }
}
