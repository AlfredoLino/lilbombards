using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>Anima escala y color de un efecto simple y lo destruye al terminar.</summary>
    public class FxAnim : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<FxAnim> All = new System.Collections.Generic.List<FxAnim>();

        public float Life = 0.5f;
        public Vector3 S0, S1;
        public Color C0, C1;
        public Material Mat;
        public float Delay;
        public float EasePow = 0.35f;
        public bool Billboard;
        float t;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Update()
        {
            if (Delay > 0f)
            {
                Delay -= Time.deltaTime;
                return;
            }
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Life);
            float e = 1f - Mathf.Pow(1f - k, 1f / EasePow * 0.5f + 1f);
            transform.localScale = Vector3.LerpUnclamped(S0, S1, e);
            if (Mat != null) Mat.color = Color.Lerp(C0, C1, k);
            if (Billboard && CameraRig.I != null)
                transform.rotation = CameraRig.I.Cam.transform.rotation;
            if (k >= 1f) Destroy(gameObject);
        }

        void OnDestroy() { if (Mat != null) Destroy(Mat); }
    }

    /// <summary>
    /// Trozo de escombro (armadura, piel, musculo...) que se queda en el campo toda la partida.
    /// Sigue siendo fisico: las explosiones y los personajes lo mueven. Solo desaparece si cae al vacio,
    /// al volver a la sala, o si hay demasiados (se retiran los mas antiguos).
    /// </summary>
    public class DebrisFade : MonoBehaviour
    {
        public static readonly List<DebrisFade> All = new List<DebrisFade>();

        public float Life = 2f; // sin uso: los trozos ya no caducan
        public Material Mat;
        public Mesh OwnedMesh;

        void Start()
        {
            All.Add(this);
            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.SetDamping(0.3f, 1.5f); // que se asienten pronto y se "duerman"
            while (All.Count > Tuning.MaxFieldDebris)
            {
                var old = All[0];
                All.RemoveAt(0);
                if (old != null) Destroy(old.gameObject);
            }
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (Mat != null) Destroy(Mat);
            if (OwnedMesh != null) Destroy(OwnedMesh);
        }

        void Update()
        {
            if (transform.position.y < Tuning.KillY) Destroy(gameObject);
        }

        public static void ClearAll()
        {
            foreach (var d in new List<DebrisFade>(All))
                if (d != null) Destroy(d.gameObject);
            All.Clear();
        }
    }

    /// <summary>Marca persistente de explosion en el suelo (calcomania con textura de quemado).</summary>
    public class ScorchMark : MonoBehaviour
    {
        public static readonly List<ScorchMark> All = new List<ScorchMark>();
        static int counter;
        Material mat;

        public static void Create(Vector3 point, Vector3 normal, float radius, bool ice)
        {
            Color c = ice ? new Color(0.8f, 0.92f, 1f, 0.75f) : new Color(1f, 1f, 1f, 0.9f);
            var mat = Gfx.Unlit(c, false, ice ? Gfx.SoftCircle() : Gfx.ScorchTex());
            mat.renderQueue = 2980 + (counter % 10); // las nuevas encima de las viejas
            // Pequeno desfase por marca para que las superpuestas no parpadeen.
            Vector3 pos = point + normal * (0.012f + (counter % 12) * 0.0009f);
            counter++;
            var go = Gfx.Part("Scorch", null, Gfx.Prim(PrimitiveType.Quad), mat, pos, Vector3.one);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = radius * Random.Range(1.4f, 1.8f);
            go.transform.localScale = new Vector3(s, s, s);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var sm = go.AddComponent<ScorchMark>();
            sm.mat = mat;
            All.Add(sm);
            while (All.Count > Tuning.MaxScorchMarks)
            {
                var old = All[0];
                All.RemoveAt(0);
                if (old != null) Destroy(old.gameObject);
            }
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (mat != null) Destroy(mat);
        }

        public static void ClearAll()
        {
            foreach (var s in new List<ScorchMark>(All))
                if (s != null) Destroy(s.gameObject);
            All.Clear();
        }
    }

    /// <summary>Atenua una luz puntual y la destruye.</summary>
    public class LightFade : MonoBehaviour
    {
        public Light L;
        public float Life = 0.3f;
        float t, i0;

        void Start() { i0 = L.intensity; }

        void Update()
        {
            t += Time.deltaTime;
            L.intensity = Mathf.Lerp(i0, 0f, t / Life);
            if (t >= Life) Destroy(gameObject);
        }
    }

    /// <summary>Efectos: explosiones, chispas, humo, marcas de quemado, destellos.</summary>
    public static class FX
    {
        struct PS
        {
            public int burst;
            public float rate;
            public Vector2 life, speed, size;
            public Color c0, c1;
            public float a1;
            public float gravity;
            public float radius;
            public bool additive, stretch, loop;
            public float growth;
            public float drag;
            public float boost;   // > 1 = brillo HDR (bloom)
        }

        static ParticleSystem Make(Vector3 pos, PS p, Transform parent = null)
        {
            var go = new GameObject("FX_PS");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
            }
            else go.transform.position = pos;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = p.loop ? 1f : 0.2f;
            main.loop = p.loop;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(p.life.x, p.life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(p.speed.x, p.speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(p.size.x, p.size.y);
            main.startColor = Color.white; // el color real lo pone colorOverLifetime
            main.gravityModifier = p.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(64, p.burst * 2);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            if (!p.loop) main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission;
            em.rateOverTime = p.rate;
            if (p.burst > 0) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)p.burst) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = Mathf.Max(0.01f, p.radius);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(p.c0, 0f), new GradientColorKey(p.c1, 1f) },
                new[] { new GradientAlphaKey(p.c0.a, 0f), new GradientAlphaKey(p.c0.a * 0.8f, 0.4f), new GradientAlphaKey(p.a1, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            if (Mathf.Abs(p.growth - 1f) > 0.01f)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, p.growth));
            }

            if (p.drag > 0f)
            {
                var lim = ps.limitVelocityOverLifetime;
                lim.enabled = true;
                lim.limit = 0f;
                lim.dampen = p.drag;
                lim.drag = 0f;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Gfx.Particle(p.additive, p.boost > 0f ? p.boost : 1f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (p.stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }

        static GameObject Blob(Vector3 pos, PrimitiveType prim, Color c0, Color c1, bool additive, Vector3 s0, Vector3 s1, float life, float delay = 0f, Texture tex = null)
        {
            var mat = Gfx.Unlit(c0, additive, tex);
            var go = Gfx.Part("FX", null, Gfx.Prim(prim), mat, pos, s0);
            go.transform.position = pos;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var a = go.AddComponent<FxAnim>();
            a.Life = life;
            a.S0 = s0;
            a.S1 = s1;
            a.C0 = c0;
            a.C1 = c1;
            a.Mat = mat;
            a.Delay = delay;
            return go;
        }

        static void Flash(Vector3 pos, Color c, float intensity, float range, float life)
        {
            var go = new GameObject("FX_Light");
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            var f = go.AddComponent<LightFade>();
            f.L = l;
            f.Life = life;
        }

        // ------------------------------------------------------------------ Efectos publicos

        public static void Explosion(Vector3 pos, float radius, bool ice)
        {
            if (!Net.FxAllowed) return;
            Net.EvExplosion(pos, radius, ice);
            float d = radius * 2f;

            // 1) Destello central casi blanco, muy brillante (HDR -> bloom fuerte).
            Color core = ice ? new Color(2.2f, 3f, 3.6f, 1f) : new Color(4f, 3.3f, 1.8f, 1f);
            Blob(pos, PrimitiveType.Sphere, core, ice ? new Color(0.5f, 1.2f, 2f, 0f) : new Color(2.5f, 0.7f, 0.1f, 0f), true,
                Vector3.one * d * 0.15f, Vector3.one * d * 0.62f, 0.2f);

            // 2) Burbuja oscura semitransparente que se expande (la "onda" de las explosiones de BombSquad).
            Blob(pos, PrimitiveType.Sphere, new Color(0.04f, 0.03f, 0.03f, 0.5f), new Color(0.05f, 0.04f, 0.04f, 0f), false,
                Vector3.one * d * 0.35f, Vector3.one * d * 1.05f, 0.32f);

            // 3) Bolas de fuego: muchas manchas suaves amarillo -> naranja -> rojo.
            Make(pos, new PS
            {
                burst = 22, life = new Vector2(0.3f, 0.6f), speed = new Vector2(1.5f, radius * 2.2f), size = new Vector2(radius * 0.55f, radius * 1.0f),
                c0 = ice ? new Color(0.8f, 0.95f, 1f, 1f) : new Color(1f, 0.85f, 0.35f, 1f),
                c1 = ice ? new Color(0.3f, 0.6f, 1f, 1f) : new Color(0.9f, 0.18f, 0.03f, 1f), a1 = 0f,
                gravity = -0.3f, radius = radius * 0.25f, additive = true, growth = 1.7f, drag = 0.12f, boost = 2.6f,
            });

            // 4) Humo oscuro que sube despues del fuego.
            Make(pos, new PS
            {
                burst = 16, life = new Vector2(1.4f, 2.4f), speed = new Vector2(0.8f, 2.6f), size = new Vector2(radius * 0.8f, radius * 1.4f),
                c0 = ice ? new Color(0.85f, 0.95f, 1f, 0.55f) : new Color(0.16f, 0.14f, 0.13f, 0.75f),
                c1 = ice ? new Color(0.9f, 0.95f, 1f, 0f) : new Color(0.32f, 0.3f, 0.29f, 0f), a1 = 0f,
                gravity = -0.25f, radius = radius * 0.45f, additive = false, growth = 2.1f, drag = 0.1f,
            });

            // 5) Chispas en rafaga (estelas) y 6) brasas que flotan.
            Make(pos, new PS
            {
                burst = 55, life = new Vector2(0.3f, 0.85f), speed = new Vector2(8f, 20f), size = new Vector2(0.05f, 0.12f),
                c0 = ice ? new Color(0.85f, 0.95f, 1f, 1f) : new Color(1f, 0.92f, 0.55f, 1f),
                c1 = ice ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(1f, 0.35f, 0.05f, 1f), a1 = 0f,
                gravity = 1.4f, radius = 0.25f, additive = true, stretch = true, growth = 0.6f, drag = 0.04f, boost = 3f,
            });
            Make(pos, new PS
            {
                burst = 22, life = new Vector2(1.2f, 2.6f), speed = new Vector2(1f, 4f), size = new Vector2(0.04f, 0.08f),
                c0 = ice ? Color.white : new Color(1f, 0.7f, 0.25f, 1f), c1 = ice ? new Color(0.5f, 0.8f, 1f, 1f) : new Color(1f, 0.25f, 0.05f, 1f), a1 = 0f,
                gravity = -0.15f, radius = radius * 0.4f, additive = true, growth = 0.5f, drag = 0.06f, boost = 2.5f,
            });

            // 7) Escombros oscuros.
            if (!ice)
                Make(pos, new PS
                {
                    burst = 14, life = new Vector2(0.6f, 1.2f), speed = new Vector2(4f, 11f), size = new Vector2(0.07f, 0.16f),
                    c0 = new Color(0.12f, 0.1f, 0.08f, 1f), c1 = new Color(0.1f, 0.1f, 0.1f, 1f), a1 = 0f,
                    gravity = 2.5f, radius = 0.2f, additive = false, growth = 1f,
                });

            // 8) Anillo en el suelo.
            var ring = Blob(pos + Vector3.up * 0.1f, PrimitiveType.Quad, ice ? new Color(1.2f, 1.6f, 2f, 0.7f) : new Color(2f, 1.3f, 0.6f, 0.7f),
                new Color(1f, 0.4f, 0.1f, 0f), true, Vector3.one * d * 0.3f, Vector3.one * d * 1.5f, 0.3f, 0f, Gfx.Ring());
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Flash(pos + Vector3.up * 0.6f, ice ? new Color(0.5f, 0.8f, 1f) : new Color(1f, 0.55f, 0.2f), 9f, radius * 5f, 0.4f);
        }

        /// <summary>
        /// Marca de quemado en el suelo que se queda toda la partida. Varias bombas en el mismo sitio
        /// se superponen y oscurecen la zona. Las de hielo dejan escarcha.
        /// </summary>
        public static void Scorch(Vector3 point, Vector3 normal, float radius, bool ice)
        {
            if (!Net.FxAllowed) return;
            Net.EvScorch(point, normal, radius, ice);
            ScorchMark.Create(point, normal, radius, ice);
        }

        public static void PunchFlash(Vector3 pos, float power)
        {
            if (!Net.FxAllowed) return;
            Net.EvPosFloat(Net.Ev.PunchFlash, pos, power);
            power = Mathf.Clamp(power, 0.4f, 2f);
            var b = Blob(pos, PrimitiveType.Quad, new Color(1f, 1f, 0.9f, 0.9f), new Color(1f, 0.9f, 0.6f, 0f), true,
                Vector3.one * 0.3f * power, Vector3.one * 1.1f * power, 0.15f, 0f, Gfx.SoftCircle());
            b.GetComponent<FxAnim>().Billboard = true;
            Make(pos, new PS
            {
                burst = (int)(10 * power), life = new Vector2(0.15f, 0.35f), speed = new Vector2(3f, 7f), size = new Vector2(0.04f, 0.09f),
                c0 = new Color(1f, 1f, 0.85f, 1f), c1 = new Color(1f, 0.8f, 0.4f, 1f), a1 = 0f,
                gravity = 0.5f, radius = 0.1f, additive = true, stretch = true, growth = 0.5f,
            });
        }

        public static void FuseSparks(Transform parent)
        {
            Make(parent.position, new PS
            {
                rate = 45f, loop = true, life = new Vector2(0.12f, 0.3f), speed = new Vector2(0.8f, 2.4f), size = new Vector2(0.04f, 0.09f),
                c0 = new Color(1f, 0.95f, 0.6f, 1f), c1 = new Color(1f, 0.4f, 0.1f, 1f), a1 = 0f,
                gravity = 0.6f, radius = 0.02f, additive = true, stretch = true, growth = 0.4f, boost = 2.5f,
            }, parent);
        }

        public static void Sparkle(Vector3 pos, Color c)
        {
            if (!Net.FxAllowed) return;
            Net.EvPosColor(Net.Ev.Sparkle, pos, c);
            Make(pos, new PS
            {
                burst = 26, life = new Vector2(0.4f, 0.8f), speed = new Vector2(2f, 5f), size = new Vector2(0.08f, 0.18f),
                c0 = Color.Lerp(c, Color.white, 0.4f), c1 = c, a1 = 0f,
                gravity = -0.3f, radius = 0.25f, additive = true, growth = 0.3f, drag = 0.1f, boost = 2f,
            });
            var b = Blob(pos, PrimitiveType.Quad, new Color(c.r, c.g, c.b, 0.9f), new Color(c.r, c.g, c.b, 0f), true,
                Vector3.one * 0.4f, Vector3.one * 2.2f, 0.3f, 0f, Gfx.SoftCircle());
            b.GetComponent<FxAnim>().Billboard = true;
        }

        public static void Poof(Vector3 pos, Color c)
        {
            if (!Net.FxAllowed) return;
            Net.EvPosColor(Net.Ev.Poof, pos, c);
            Make(pos, new PS
            {
                burst = 16, life = new Vector2(0.5f, 0.9f), speed = new Vector2(1.5f, 3.5f), size = new Vector2(0.4f, 0.7f),
                c0 = new Color(0.95f, 0.95f, 0.95f, 0.8f), c1 = new Color(0.8f, 0.8f, 0.8f, 0f), a1 = 0f,
                gravity = -0.2f, radius = 0.35f, additive = false, growth = 1.6f, drag = 0.1f,
            });
            Make(pos, new PS
            {
                burst = 14, life = new Vector2(0.3f, 0.6f), speed = new Vector2(3f, 6f), size = new Vector2(0.08f, 0.14f),
                c0 = Color.Lerp(c, Color.white, 0.3f), c1 = c, a1 = 0f,
                gravity = 0.8f, radius = 0.2f, additive = true, growth = 0.5f,
            });
        }

        public static void SpawnFlash(Vector3 pos, Color c)
        {
            if (!Net.FxAllowed) return;
            Net.EvPosColor(Net.Ev.SpawnFlash, pos, c);
            var b = Blob(pos, PrimitiveType.Sphere, new Color(c.r, c.g, c.b, 0.7f), new Color(1f, 1f, 1f, 0f), true,
                Vector3.one * 2.2f, Vector3.one * 0.3f, 0.35f);
            b.GetComponent<FxAnim>().EasePow = 1f;
            Make(pos, new PS
            {
                burst = 18, life = new Vector2(0.3f, 0.6f), speed = new Vector2(1f, 3f), size = new Vector2(0.08f, 0.15f),
                c0 = Color.Lerp(c, Color.white, 0.5f), c1 = c, a1 = 0f,
                gravity = -0.5f, radius = 0.5f, additive = true, growth = 0.4f,
            });
        }

        public static void Shatter(Vector3 pos)
        {
            if (!Net.FxAllowed) return;
            Net.EvPos(Net.Ev.Shatter, pos);
            Make(pos, new PS
            {
                burst = 40, life = new Vector2(0.6f, 1.2f), speed = new Vector2(3f, 9f), size = new Vector2(0.1f, 0.25f),
                c0 = new Color(0.85f, 0.95f, 1f, 1f), c1 = new Color(0.5f, 0.8f, 1f, 1f), a1 = 0f,
                gravity = 2f, radius = 0.4f, additive = false, growth = 0.7f,
            });
            Make(pos, new PS
            {
                burst = 25, life = new Vector2(0.2f, 0.5f), speed = new Vector2(4f, 10f), size = new Vector2(0.05f, 0.1f),
                c0 = Color.white, c1 = new Color(0.6f, 0.85f, 1f, 1f), a1 = 0f,
                gravity = 1f, radius = 0.3f, additive = true, stretch = true, growth = 0.5f,
            });
        }

        /// <summary>
        /// Trozo que sale despedido (armadura rota, pedacito de piel o musculo): pieza fisica que rebota,
        /// gira y se encoge hasta desaparecer.
        /// </summary>
        public static void Debris(Vector3 pos, Vector3 normal, Material mat, float size, Collider ignore)
        {
            mat = new Material(mat); // copia propia: el personaje puede destruirse antes que el trozo
            var go = Gfx.Part("Debris", null, Gfx.Prim(Random.value < 0.5f ? PrimitiveType.Cube : PrimitiveType.Sphere), mat,
                pos + Random.insideUnitSphere * 0.08f, new Vector3(size, size * Random.Range(0.4f, 1f), size * Random.Range(0.6f, 1.2f)));
            go.transform.rotation = Random.rotation;
            var col = go.AddComponent<BoxCollider>();
            if (ignore != null) Physics.IgnoreCollision(col, ignore, true);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 v = (normal + Random.insideUnitSphere * 0.6f).normalized * Random.Range(3f, 6.5f) + Vector3.up * Random.Range(2f, 4f);
            rb.SetVel(v);
            rb.angularVelocity = Random.insideUnitSphere * 15f;
            var fade = go.AddComponent<DebrisFade>();
            fade.Life = Random.Range(1.8f, 2.6f);
            fade.Mat = mat;
        }

        /// <summary>
        /// Rompe una pieza del cuerpo en trozos reales de su malla y los lanza hacia fuera
        /// (desde el punto del impacto, en la direccion 'outward').
        /// </summary>
        public static void Fragments(Renderer r, Material mat, Vector3 origin, Vector3 outward, int count, Collider ignore)
        {
            if (r == null || mat == null) return;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var pieces = MeshFracture.Split(mf.sharedMesh, count, 0.2f);
            var tr = r.transform;
            foreach (var p in pieces)
            {
                var go = new GameObject("Fragment");
                go.transform.SetPositionAndRotation(tr.TransformPoint(p.center), tr.rotation);
                go.transform.localScale = tr.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                var copy = new Material(mat);
                go.AddComponent<MeshRenderer>().sharedMaterial = copy;

                var bc = go.AddComponent<BoxCollider>();
                Vector3 s = bc.size;
                bc.size = new Vector3(Mathf.Max(s.x, 0.02f), Mathf.Max(s.y, 0.02f), Mathf.Max(s.z, 0.02f));
                if (ignore != null) Physics.IgnoreCollision(bc, ignore, true);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.08f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                // Salen despedidos desde el impacto, un poco abiertos y hacia arriba.
                Vector3 away = go.transform.position - origin;
                Vector3 dir = (outward * 1.2f + (away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.zero) + Random.insideUnitSphere * 0.5f).normalized;
                rb.SetVel(dir * Random.Range(3f, 7f) + Vector3.up * Random.Range(2f, 4.5f));
                rb.angularVelocity = Random.insideUnitSphere * 14f;

                var fade = go.AddComponent<DebrisFade>();
                fade.Life = Random.Range(2.2f, 3.2f);
                fade.Mat = copy;
                fade.OwnedMesh = p.mesh;
            }
        }

        /// <summary>
        /// Lanza un trozo del cuerpo ya construido (malla exacta del trozo) desde su sitio:
        /// sale hacia fuera de la explosion, gira, rebota y se queda en el campo.
        /// </summary>
        public static void SpawnChunk(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material mat, Vector3 blast, Vector3 normal, Collider ignore)
        {
            var go = new GameObject("BodyChunk");
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var copy = new Material(mat);
            go.AddComponent<MeshRenderer>().sharedMaterial = copy;

            var bc = go.AddComponent<BoxCollider>();
            Vector3 s = bc.size;
            bc.size = new Vector3(Mathf.Max(s.x, 0.02f / Mathf.Max(0.01f, Mathf.Abs(scale.x))),
                                  Mathf.Max(s.y, 0.02f / Mathf.Max(0.01f, Mathf.Abs(scale.y))),
                                  Mathf.Max(s.z, 0.02f / Mathf.Max(0.01f, Mathf.Abs(scale.z))));
            if (ignore != null) Physics.IgnoreCollision(bc, ignore, true);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            Vector3 away = pos - blast;
            // Saltan hacia fuera de la superficie (normal) y se van alejando de la explosion.
            Vector3 dir = (normal * 0.9f + (away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.zero) * 0.6f + Random.insideUnitSphere * 0.5f).normalized;
            float force = Mathf.Lerp(7f, 3f, Mathf.Clamp01(away.magnitude / 3f));
            rb.SetVel(dir * force * Random.Range(0.8f, 1.25f) + Vector3.up * Random.Range(1.5f, 4f));
            rb.angularVelocity = Random.insideUnitSphere * 16f;

            var fade = go.AddComponent<DebrisFade>();
            fade.Mat = copy;
            fade.OwnedMesh = mesh;
        }

        public static void Confetti(Vector3 pos)
        {
            if (!Net.FxAllowed) return;
            Net.EvPos(Net.Ev.Confetti, pos);
            for (int i = 0; i < 4; i++)
            {
                Color c = PlayerSlot.Palette[Random.Range(0, PlayerSlot.Palette.Length)];
                Make(pos + Random.insideUnitSphere * 1.5f, new PS
                {
                    burst = 30, life = new Vector2(1.2f, 2.2f), speed = new Vector2(4f, 10f), size = new Vector2(0.1f, 0.2f),
                    c0 = c, c1 = Color.Lerp(c, Color.white, 0.5f), a1 = 0f,
                    gravity = 0.8f, radius = 0.3f, additive = true, growth = 0.6f, drag = 0.05f,
                });
            }
        }
    }
}
