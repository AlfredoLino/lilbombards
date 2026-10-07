using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Fabrica de materiales, texturas procedurales y piezas visuales.
    /// Si existen modelos exportados desde Blender en Resources/Models se usan;
    /// si no, se cae a primitivas de Unity para que el juego funcione igual.
    /// </summary>
    public static class Gfx
    {
        static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        static readonly Dictionary<PrimitiveType, Mesh> prims = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<string, Mesh> models = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static Shader GetShader(string name, string fallback)
        {
            if (shaders.TryGetValue(name, out var s)) return s;
            s = Shader.Find(name);
            if (s == null)
            {
                Debug.LogWarning("[LB] Shader no encontrado: " + name + ", usando " + fallback);
                s = Shader.Find(fallback);
            }
            shaders[name] = s;
            return s;
        }

        // ---------------------------------------------------------------- Materiales

        public static Material Toon(Color c, float spec = 0.45f, float gloss = 0.55f, float rim = 0.3f, Texture tex = null)
        {
            var m = new Material(GetShader("LB/Toon", "Standard"));
            m.color = c;
            if (m.HasProperty("_Spec")) m.SetFloat("_Spec", spec);
            if (m.HasProperty("_Gloss")) m.SetFloat("_Gloss", gloss);
            if (m.HasProperty("_Rim")) m.SetFloat("_Rim", rim);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (tex != null) m.mainTexture = tex;
            if (m.HasProperty("_CrackTex")) m.SetTexture("_CrackTex", CrackTex());
            if (m.HasProperty("_SootTex")) m.SetTexture("_SootTex", SootTex());
            return m;
        }

        /// <summary>Manchas de hollin (ruido fractal): los valores altos se tiznan primero.</summary>
        public static Texture2D SootTex()
        {
            return GetTex("soot", 256, p =>
            {
                float v = Mathf.PerlinNoise(p.x * 4f, p.y * 4f) * 0.55f + Mathf.PerlinNoise(p.x * 11f + 3f, p.y * 11f) * 0.3f +
                          Mathf.PerlinNoise(p.x * 30f, p.y * 30f + 7f) * 0.15f;
                return new Color(v, v, v, 1f);
            }, true);
        }

        /// <summary>
        /// Marca de explosion en el suelo: centro negro, rayos de hollin hacia fuera y borde difuso.
        /// </summary>
        public static Texture2D ScorchTex()
        {
            return GetTex("scorch", 256, p =>
            {
                Vector2 d = p - new Vector2(0.5f, 0.5f);
                float r = d.magnitude * 2f;
                float ang = Mathf.Atan2(d.y, d.x);
                float rays = Mathf.PerlinNoise(ang * 3.2f + 10f, 0.5f) * 0.6f + Mathf.PerlinNoise(ang * 9f + 3f, 1.7f) * 0.4f;
                float reach = 0.55f + rays * 0.5f;
                float a = Mathf.Clamp01(1f - r / reach);
                a = Mathf.Pow(a, 0.7f);
                float grain = 0.75f + 0.25f * Mathf.PerlinNoise(p.x * 40f, p.y * 40f);
                float core = Mathf.Clamp01(1f - r / 0.35f);
                float shade = Mathf.Lerp(0.16f, 0.02f, core);
                return new Color(shade, shade * 0.9f, shade * 0.8f, Mathf.Clamp01(a * grain));
            });
        }

        /// <summary>
        /// Mapa de grietas: lineas finas ramificadas (ruido "ridged"). Cada linea tiene una intensidad
        /// distinta, asi que con poco desgaste solo aparecen las principales y luego las secundarias.
        /// </summary>
        public static Texture2D CrackTex()
        {
            return GetTex("cracks", 256, p =>
            {
                float n1 = Mathf.PerlinNoise(p.x * 6f, p.y * 6f);
                float n2 = Mathf.PerlinNoise(p.x * 13f + 5f, p.y * 13f + 3f);
                float ridge1 = 1f - Mathf.Abs(n1 - 0.5f) * 2f;
                float ridge2 = 1f - Mathf.Abs(n2 - 0.5f) * 2f;
                float lines = Mathf.Max(Mathf.Pow(ridge1, 14f), Mathf.Pow(ridge2, 18f) * 0.85f);
                float strength = 0.55f + 0.45f * Mathf.PerlinNoise(p.x * 2.3f + 9f, p.y * 2.3f);
                float scratch = Mathf.Pow(Mathf.Abs(Mathf.Sin((p.x * 0.8f + p.y) * 70f + n1 * 8f)), 60f) * 0.6f;
                float v = Mathf.Clamp01(Mathf.Max(lines * strength, scratch * strength));
                return new Color(v, v, v, 1f);
            }, true);
        }

        public static void SetEmission(Material m, Color c)
        {
            if (m.HasProperty("_Emission")) m.SetColor("_Emission", c);
            else if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c);
            }
        }

        public static Material Unlit(Color c, bool additive, Texture tex = null, bool cullOff = false)
        {
            var m = new Material(GetShader("LB/Unlit", "Sprites/Default"));
            m.color = c;
            if (tex != null) m.mainTexture = tex;
            if (m.HasProperty("_SrcBlend"))
            {
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_Cull", cullOff ? 0f : 2f);
            }
            return m;
        }

        static readonly Dictionary<int, Material> particleMats = new Dictionary<int, Material>();

        /// <summary>Material de particulas. boost &gt; 1 empuja el color por encima de 1 (HDR) para que el bloom lo haga brillar.</summary>
        public static Material Particle(bool additive, float boost = 1f)
        {
            int k = (additive ? 1 : 0) + Mathf.RoundToInt(boost * 10f) * 2;
            if (particleMats.TryGetValue(k, out var m)) return m;
            m = new Material(GetShader("LB/Particle", "Sprites/Default"));
            m.mainTexture = SoftCircle();
            if (m.HasProperty("_Boost")) m.SetFloat("_Boost", boost);
            if (m.HasProperty("_SrcBlend"))
            {
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            }
            particleMats[k] = m;
            return m;
        }

        public static Material Shield(Color body, Color edge)
        {
            var m = new Material(GetShader("LB/Shield", "Sprites/Default"));
            if (m.HasProperty("_Color")) m.SetColor("_Color", body);
            if (m.HasProperty("_EdgeColor")) m.SetColor("_EdgeColor", edge);
            return m;
        }

        // ---------------------------------------------------------------- Mallas

        public static Mesh Prim(PrimitiveType t)
        {
            if (prims.TryGetValue(t, out var m)) return m;
            var go = GameObject.CreatePrimitive(t);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            prims[t] = m;
            return m;
        }

        /// <summary>
        /// Malla de Resources/Models/{name} (FBX de Blender), centrada y normalizada
        /// a extension maxima 1 (igual que las primitivas). Null si no existe.
        /// </summary>
        public static Mesh Model(string name)
        {
            if (models.TryGetValue(name, out var m)) return m;
            m = null;
            var go = Resources.Load<GameObject>("Models/" + name);
            if (go == null)
            {
                Debug.Log("[LB] Modelo '" + name + "' no encontrado en Resources/Models (se usa primitiva).");
            }
            else
            {
                var mf = go.GetComponentInChildren<MeshFilter>(true);
                if (mf == null || mf.sharedMesh == null)
                    Debug.LogWarning("[LB] Modelo '" + name + "' no tiene MeshFilter/malla.");
                else if (!mf.sharedMesh.isReadable)
                    Debug.LogWarning("[LB] Modelo '" + name + "' no es legible (Read/Write desactivado).");
                else
                {
                    m = Normalize(mf.sharedMesh);
                    Debug.Log("[LB] Modelo '" + name + "' cargado: " + m.vertexCount + " vertices, tamano original " + mf.sharedMesh.bounds.size);
                }
            }
            models[name] = m;
            return m;
        }

        static readonly Dictionary<string, Mesh> rawModels = new Dictionary<string, Mesh>();
        static int axisFix = -1; // -1 sin comprobar, 0 correcto, 1 girar 180 grados en Y

        /// <summary>
        /// Malla de Resources/Models/body/{name} SIN normalizar: las piezas del cuerpo vienen ya en metros
        /// y en su posicion final (ver Blender/generate_body.py). Comprueba una vez la orientacion de los
        /// ejes con "axis_probe" (un cubo delante del personaje) y la corrige si hiciera falta.
        /// </summary>
        public static Mesh ModelRaw(string name)
        {
            if (rawModels.TryGetValue(name, out var m)) return m;
            m = null;
            var src = LoadMesh("Models/body/" + name);
            if (src != null && src.isReadable)
            {
                if (axisFix < 0)
                {
                    var probe = LoadMesh("Models/body/axis_probe");
                    axisFix = probe != null && probe.isReadable && probe.bounds.center.z < 0f ? 1 : 0;
                    if (axisFix == 1) Debug.Log("[LB] Modelos del cuerpo: corrigiendo orientacion (frente invertido).");
                }
                m = Object.Instantiate(src);
                if (axisFix == 1)
                {
                    var v = m.vertices;
                    var n = m.normals;
                    for (int i = 0; i < v.Length; i++) v[i] = new Vector3(-v[i].x, v[i].y, -v[i].z);
                    for (int i = 0; i < n.Length; i++) n[i] = new Vector3(-n[i].x, n[i].y, -n[i].z);
                    m.vertices = v;
                    if (n.Length == v.Length) m.normals = n;
                    m.RecalculateBounds();
                }
            }
            rawModels[name] = m;
            return m;
        }

        static Mesh LoadMesh(string path)
        {
            var go = Resources.Load<GameObject>(path);
            if (go == null) return null;
            var mf = go.GetComponentInChildren<MeshFilter>(true);
            return mf != null ? mf.sharedMesh : null;
        }

        /// <summary>Fibras musculares: rojo con estrias a lo largo de la pieza.</summary>
        public static Texture2D MuscleTex()
        {
            return GetTex("muscle", 256, p =>
            {
                float n = Mathf.PerlinNoise(p.x * 30f, p.y * 3f);
                float fib = 0.5f + 0.5f * Mathf.Sin((p.x * 56f + n * 2.5f) * Mathf.PI);
                fib = Mathf.Pow(fib, 3f);
                Color dark = new Color(0.5f, 0.07f, 0.07f), mid = new Color(0.82f, 0.18f, 0.16f), hi = new Color(1f, 0.45f, 0.4f);
                Color c = Color.Lerp(dark, mid, 0.35f + n * 0.4f);
                return Color.Lerp(c, hi, fib * 0.45f);
            }, true);
        }

        /// <summary>Hueso: marfil con motas suaves.</summary>
        public static Texture2D BoneTex()
        {
            return GetTex("bone", 128, p =>
            {
                float n = Mathf.PerlinNoise(p.x * 18f, p.y * 18f) * 0.6f + Mathf.PerlinNoise(p.x * 60f, p.y * 60f) * 0.4f;
                return Color.Lerp(new Color(0.86f, 0.82f, 0.7f), new Color(0.98f, 0.96f, 0.9f), n);
            }, true);
        }

        static Mesh Normalize(Mesh src)
        {
            var m = Object.Instantiate(src);
            var b = m.bounds;
            float s = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (s < 1e-5f) return m;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = (v[i] - b.center) / s;
            m.vertices = v;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Crea una pieza visual sin collider.</summary>
        public static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go;
        }

        /// <summary>Usa el modelo de Blender si existe (escala uniforme), si no la primitiva con escala dada.</summary>
        public static GameObject PartAuto(string name, Transform parent, string model, PrimitiveType fallback, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var mesh = Model(model);
            if (mesh != null)
            {
                float s = Mathf.Max(localScale.x, Mathf.Max(localScale.y, localScale.z));
                return Part(name, parent, mesh, mat, localPos, new Vector3(s, s, s));
            }
            return Part(name, parent, Prim(fallback), mat, localPos, localScale);
        }

        // ---------------------------------------------------------------- Texturas

        static Sprite circleSprite, roundSprite;

        /// <summary>Circulo nitido (UI).</summary>
        public static Sprite CircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            var t = GetTex("ui_circle", 64, p =>
            {
                float d = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f;
                return new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 32f));
            });
            circleSprite = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
            return circleSprite;
        }

        /// <summary>Rectangulo redondeado estirable (9-slice) para la UI.</summary>
        public static Sprite RoundSprite()
        {
            if (roundSprite != null) return roundSprite;
            const int n = 32;
            var t = GetTex("ui_round", n, p =>
            {
                float r = 0.35f;
                float qx = Mathf.Abs(p.x - 0.5f) - 0.5f + r, qy = Mathf.Abs(p.y - 0.5f) - 0.5f + r;
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                return new Color(1f, 1f, 1f, Mathf.Clamp01(-d * n));
            });
            roundSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
            return roundSprite;
        }

        public static Texture2D SoftCircle()
        {
            return GetTex("softcircle", 64, p =>
            {
                float d = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                return new Color(1, 1, 1, a);
            });
        }

        public static Texture2D Ring()
        {
            return GetTex("ring", 128, p =>
            {
                float d = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f;
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.15f);
                return new Color(1, 1, 1, a * a);
            });
        }

        public static Texture2D Tiles()
        {
            return GetTex("tiles", 256, p =>
            {
                Vector2 q = p * 4f;
                Vector2 f = new Vector2(q.x - Mathf.Floor(q.x), q.y - Mathf.Floor(q.y));
                float edge = Mathf.Min(Mathf.Min(f.x, 1f - f.x), Mathf.Min(f.y, 1f - f.y));
                float grout = Mathf.SmoothStep(0.02f, 0.06f, edge);
                float n = Mathf.PerlinNoise(Mathf.Floor(q.x) * 3.1f + 0.5f, Mathf.Floor(q.y) * 2.7f + 0.5f);
                float detail = Mathf.PerlinNoise(p.x * 40f, p.y * 40f) * 0.06f;
                float bevel = Mathf.SmoothStep(0.06f, 0.14f, edge) * 0.06f;
                float v = 0.74f + n * 0.1f - detail + bevel;
                Color tile = new Color(v, v * 0.97f, v * 0.9f);
                return Color.Lerp(new Color(0.48f, 0.44f, 0.4f), tile, grout);
            }, true);
        }

        /// <summary>Veta de madera clara (bloques de juguete sin pintar).</summary>
        public static Texture2D Wood()
        {
            return GetTex("wood", 512, p =>
            {
                float warp = Mathf.PerlinNoise(p.x * 3f, p.y * 0.8f) * 2.2f + Mathf.PerlinNoise(p.x * 9f + 7f, p.y * 2f) * 0.5f;
                float rings = Mathf.Sin((p.x * 26f + warp * 3.5f) * Mathf.PI);
                float grain = Mathf.SmoothStep(0.55f, 1f, rings) * 0.55f + Mathf.SmoothStep(0.2f, 1f, rings) * 0.12f;
                float fine = (Mathf.PerlinNoise(p.x * 160f, p.y * 6f) - 0.5f) * 0.12f;
                float tone = Mathf.PerlinNoise(p.x * 1.5f + 3f, p.y * 1.5f) * 0.12f;
                Color light = new Color(0.93f, 0.76f, 0.52f), dark = new Color(0.68f, 0.47f, 0.27f);
                Color c = Color.Lerp(light, dark, Mathf.Clamp01(grain + tone));
                return c * (1f + fine);
            }, true);
        }

        /// <summary>Veta muy sutil en gris claro: se multiplica por el color de la pintura.</summary>
        public static Texture2D PaintedWood()
        {
            return GetTex("paintedwood", 256, p =>
            {
                float warp = Mathf.PerlinNoise(p.x * 3f, p.y * 0.8f) * 2.2f;
                float rings = Mathf.Sin((p.x * 26f + warp * 3.5f) * Mathf.PI);
                float v = 1f - Mathf.SmoothStep(0.6f, 1f, rings) * 0.1f - Mathf.PerlinNoise(p.x * 120f, p.y * 5f) * 0.05f;
                return new Color(v, v, v, 1f);
            }, true);
        }

        public static Texture2D Rock()
        {
            return GetTex("rock", 256, p =>
            {
                float band = Mathf.PerlinNoise(p.x * 3f, p.y * 14f);
                float n = Mathf.PerlinNoise(p.x * 24f, p.y * 24f);
                float v = 0.55f + band * 0.35f + n * 0.12f;
                return new Color(0.62f * v, 0.46f * v, 0.33f * v);
            }, true);
        }

        public static Texture2D Grass()
        {
            return GetTex("grass", 128, p =>
            {
                float n = Mathf.PerlinNoise(p.x * 18f, p.y * 18f) * 0.5f + Mathf.PerlinNoise(p.x * 60f, p.y * 60f) * 0.25f;
                return new Color(0.32f + n * 0.15f, 0.62f + n * 0.2f, 0.25f + n * 0.08f);
            }, true);
        }

        public static Texture2D Stripe(Color a, Color b)
        {
            return GetTex("stripe" + a + b, 64, p =>
            {
                float s = Mathf.Abs(p.y - 0.5f);
                return s < 0.09f ? b : a;
            });
        }

        static Texture2D GetTex(string key, int size, System.Func<Vector2, Color> fn, bool repeat = false)
        {
            if (textures.TryGetValue(key, out var t)) return t;
            t = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = fn(new Vector2((x + 0.5f) / size, (y + 0.5f) / size));
            t.SetPixels(px);
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.Apply(true);
            textures[key] = t;
            return t;
        }

        // ---------------------------------------------------------------- Iconos de powerups

        public static Color PowerupColor(PowerupType t)
        {
            switch (t)
            {
                case PowerupType.TripleBombs: return new Color(1f, 0.78f, 0.2f);
                case PowerupType.IceBombs: return new Color(0.55f, 0.85f, 1f);
                case PowerupType.StickyBombs: return new Color(0.45f, 0.88f, 0.3f);
                case PowerupType.ImpactBombs: return new Color(1f, 0.5f, 0.15f);
                case PowerupType.LandMines: return new Color(0.62f, 0.64f, 0.7f);
                case PowerupType.Gloves: return new Color(1f, 0.32f, 0.28f);
                case PowerupType.Shield: return new Color(0.62f, 0.38f, 0.95f);
                case PowerupType.Health: return new Color(0.96f, 0.96f, 0.96f);
                default: return new Color(0.3f, 0.18f, 0.35f);
            }
        }

        public static Texture2D PowerupIcon(PowerupType t)
        {
            string key = "pw_" + t;
            if (textures.TryGetValue(key, out var tex)) return tex;
            var p = new Painter(128);
            Color bg = PowerupColor(t);
            p.Fill(new Color(bg.r * 0.55f, bg.g * 0.55f, bg.b * 0.55f));
            p.RoundRect(0.5f, 0.5f, 0.44f, 0.44f, 0.08f, bg);
            p.RoundRect(0.5f, 0.56f, 0.4f, 0.32f, 0.06f, Color.Lerp(bg, Color.white, 0.25f));
            Color black = new Color(0.08f, 0.08f, 0.1f);
            Color hi = new Color(1f, 1f, 1f, 0.6f);
            switch (t)
            {
                case PowerupType.TripleBombs:
                    for (int i = 0; i < 3; i++)
                    {
                        float cx = 0.27f + i * 0.23f;
                        p.Circle(cx, 0.45f, 0.11f, black);
                        p.Rect(cx - 0.025f, 0.55f, cx + 0.025f, 0.62f, new Color(0.5f, 0.5f, 0.5f));
                        p.Circle(cx - 0.04f, 0.49f, 0.03f, hi);
                    }
                    break;
                case PowerupType.IceBombs:
                    p.Circle(0.5f, 0.48f, 0.22f, new Color(0.2f, 0.55f, 0.95f));
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        p.Line(0.5f - Mathf.Cos(a) * 0.17f, 0.48f - Mathf.Sin(a) * 0.17f, 0.5f + Mathf.Cos(a) * 0.17f, 0.48f + Mathf.Sin(a) * 0.17f, 0.025f, Color.white);
                    }
                    break;
                case PowerupType.StickyBombs:
                    p.Circle(0.5f, 0.5f, 0.2f, new Color(0.15f, 0.6f, 0.15f));
                    p.RoundRect(0.42f, 0.3f, 0.035f, 0.08f, 0.03f, new Color(0.15f, 0.6f, 0.15f));
                    p.RoundRect(0.6f, 0.28f, 0.03f, 0.1f, 0.03f, new Color(0.15f, 0.6f, 0.15f));
                    p.Circle(0.44f, 0.56f, 0.05f, hi);
                    break;
                case PowerupType.ImpactBombs:
                    p.Circle(0.5f, 0.48f, 0.22f, black);
                    p.Ring(0.5f, 0.48f, 0.15f, 0.03f, new Color(1f, 0.85f, 0.1f));
                    p.Circle(0.5f, 0.48f, 0.05f, new Color(1f, 0.85f, 0.1f));
                    break;
                case PowerupType.LandMines:
                    p.RoundRect(0.5f, 0.42f, 0.28f, 0.08f, 0.06f, new Color(0.25f, 0.27f, 0.3f));
                    p.RoundRect(0.5f, 0.5f, 0.12f, 0.05f, 0.04f, new Color(0.35f, 0.37f, 0.4f));
                    p.Circle(0.5f, 0.57f, 0.05f, new Color(1f, 0.15f, 0.1f));
                    break;
                case PowerupType.Gloves:
                    p.Circle(0.5f, 0.55f, 0.2f, new Color(0.8f, 0.05f, 0.05f));
                    p.Circle(0.33f, 0.5f, 0.09f, new Color(0.8f, 0.05f, 0.05f));
                    p.RoundRect(0.52f, 0.3f, 0.14f, 0.07f, 0.03f, new Color(0.95f, 0.95f, 0.95f));
                    p.Circle(0.45f, 0.62f, 0.05f, hi);
                    break;
                case PowerupType.Shield:
                    p.Ring(0.5f, 0.5f, 0.2f, 0.05f, Color.white);
                    p.Circle(0.5f, 0.5f, 0.12f, new Color(1f, 1f, 1f, 0.35f));
                    break;
                case PowerupType.Health:
                    p.Rect(0.42f, 0.25f, 0.58f, 0.75f, new Color(0.9f, 0.1f, 0.1f));
                    p.Rect(0.25f, 0.42f, 0.75f, 0.58f, new Color(0.9f, 0.1f, 0.1f));
                    break;
                case PowerupType.Curse:
                    p.Circle(0.5f, 0.55f, 0.2f, new Color(0.92f, 0.9f, 0.85f));
                    p.RoundRect(0.5f, 0.33f, 0.11f, 0.07f, 0.02f, new Color(0.92f, 0.9f, 0.85f));
                    p.Circle(0.42f, 0.56f, 0.055f, black);
                    p.Circle(0.58f, 0.56f, 0.055f, black);
                    p.Rect(0.47f, 0.3f, 0.49f, 0.38f, black);
                    p.Rect(0.51f, 0.3f, 0.53f, 0.38f, black);
                    break;
            }
            tex = p.ToTexture();
            textures[key] = tex;
            return tex;
        }

        public static Texture2D TntTexture()
        {
            if (textures.TryGetValue("tnt", out var tex)) return tex;
            var p = new Painter(128);
            p.Fill(new Color(0.85f, 0.15f, 0.1f));
            p.Rect(0f, 0.32f, 1f, 0.68f, new Color(0.95f, 0.88f, 0.7f));
            p.Rect(0f, 0f, 1f, 0.06f, new Color(0.4f, 0.08f, 0.05f));
            p.Rect(0f, 0.94f, 1f, 1f, new Color(0.4f, 0.08f, 0.05f));
            p.Glyphs("TNT", 0.5f, 0.5f, 0.045f, new Color(0.1f, 0.08f, 0.08f));
            tex = p.ToTexture();
            textures["tnt"] = tex;
            return tex;
        }

        /// <summary>Pintor de texturas con primitivas antialias en coordenadas normalizadas (0..1).</summary>
        public class Painter
        {
            readonly int n;
            readonly Color[] px;

            public Painter(int size)
            {
                n = size;
                px = new Color[size * size];
            }

            public void Fill(Color c)
            {
                for (int i = 0; i < px.Length; i++) px[i] = c;
            }

            void Shade(System.Func<float, float, float> sdf, Color c)
            {
                float aa = 1.5f / n;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                        float d = sdf(u, v);
                        float a = Mathf.Clamp01(0.5f - d / aa) * c.a;
                        if (a <= 0f) continue;
                        int i = y * n + x;
                        Color o = Color.Lerp(px[i], c, a);
                        o.a = Mathf.Max(px[i].a, a);
                        px[i] = o;
                    }
            }

            public void Circle(float cx, float cy, float r, Color c) =>
                Shade((u, v) => Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - r, c);

            public void Ring(float cx, float cy, float r, float w, Color c) =>
                Shade((u, v) => Mathf.Abs(Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - r) - w, c);

            public void Rect(float x0, float y0, float x1, float y1, Color c) =>
                Shade((u, v) => Mathf.Max(Mathf.Max(x0 - u, u - x1), Mathf.Max(y0 - v, v - y1)), c);

            public void RoundRect(float cx, float cy, float hw, float hh, float r, Color c) =>
                Shade((u, v) =>
                {
                    float qx = Mathf.Abs(u - cx) - hw + r, qy = Mathf.Abs(v - cy) - hh + r;
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                }, c);

            public void Line(float x0, float y0, float x1, float y1, float w, Color c) =>
                Shade((u, v) =>
                {
                    Vector2 pa = new Vector2(u - x0, v - y0), ba = new Vector2(x1 - x0, y1 - y0);
                    float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
                    return (pa - ba * h).magnitude - w;
                }, c);

            static readonly Dictionary<char, string[]> font = new Dictionary<char, string[]>
            {
                { 'T', new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" } },
                { 'N', new[] { "10001", "11001", "11001", "10101", "10011", "10011", "10001" } },
            };

            /// <summary>Texto con una fuente de mapa de bits 5x7 (solo los glifos que necesitamos).</summary>
            public void Glyphs(string s, float cx, float cy, float cell, Color c)
            {
                float w = s.Length * 6f * cell - cell;
                float x0 = cx - w * 0.5f, y0 = cy + 3.5f * cell;
                for (int k = 0; k < s.Length; k++)
                {
                    if (!font.TryGetValue(s[k], out var g)) continue;
                    for (int row = 0; row < 7; row++)
                        for (int col = 0; col < 5; col++)
                            if (g[row][col] == '1')
                            {
                                float gx = x0 + (k * 6 + col) * cell, gy = y0 - (row + 1) * cell;
                                Rect(gx, gy, gx + cell, gy + cell, c);
                            }
                }
            }

            public Texture2D ToTexture()
            {
                var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
                t.SetPixels(px);
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Trilinear;
                t.Apply(true);
                return t;
            }
        }
    }
}
