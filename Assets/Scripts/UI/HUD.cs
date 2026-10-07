using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LB
{
    /// <summary>
    /// Interfaz construida por codigo (uGUI): marcador de jugadores arriba, nombres flotantes
    /// con barra de vida, mensajes centrales, registro de eliminaciones y textos emergentes.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        public static HUD I;

        Font font;
        RectTransform root, scoreRow, tagLayer, popLayer;
        Text centerText, subText, feedText, lobbyText, footerText;
        float centerTimer, centerFade;

        class Tag
        {
            public RectTransform rt;
            public Text name, extra;
            public Image chargeBg, chargeFill, stamBg, stamFill;
            public float shownHp = 1f;
        }

        class PopupItem
        {
            public Text text;
            public Vector3 world;
            public float t;
        }

        readonly Dictionary<LBCharacter, Tag> tags = new Dictionary<LBCharacter, Tag>();
        readonly List<PopupItem> popups = new List<PopupItem>();
        readonly List<(string text, float time)> feed = new List<(string, float)>();
        readonly List<Text> scoreTexts = new List<Text>();
        readonly List<Image> scoreIcons = new List<Image>();

        // Paneles de porcentaje en la parte inferior (estilo Smash).
        class DamagePanel
        {
            public Image bg, stamBg, stamFill;
            public Image[] sil; // silueta: cabeza, torso y 8 secciones de extremidades
            public Text pct, name;
            public float shown, pop;
            public PlayerSlot slot;
        }
        RectTransform bottomRow;
        readonly List<DamagePanel> damagePanels = new List<DamagePanel>();

        public static HUD Create()
        {
            var go = new GameObject("HUD", typeof(RectTransform));
            var h = go.AddComponent<HUD>();
            I = h;
            h.Build();
            return h;
        }

        Font LoadFont()
        {
            var f = Resources.Load<Font>("Fonts/LuckiestGuy-Regular");
            if (f != null) return f;
#if UNITY_2022_2_OR_NEWER
            f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            f = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            return f;
        }

        void Build()
        {
            font = LoadFont();
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var cs = gameObject.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.matchWidthOrHeight = 0.5f;
            root = (RectTransform)transform;

            tagLayer = Layer("Tags");
            popLayer = Layer("Popups");

            bottomRow = Layer("DamageRow");
            bottomRow.anchorMin = new Vector2(0f, 0f);
            bottomRow.anchorMax = new Vector2(1f, 0f);
            bottomRow.pivot = new Vector2(0.5f, 0f);
            bottomRow.sizeDelta = new Vector2(0f, 130f);
            bottomRow.anchoredPosition = new Vector2(0f, 18f);

            scoreRow = Layer("Scores");
            scoreRow.anchorMin = new Vector2(0f, 1f);
            scoreRow.anchorMax = new Vector2(1f, 1f);
            scoreRow.pivot = new Vector2(0f, 1f);
            scoreRow.sizeDelta = new Vector2(0f, 90f);
            scoreRow.anchoredPosition = new Vector2(24f, -16f);

            centerText = MakeText(root, "Center", 96, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.62f), new Vector2(1600f, 160f));
            subText = MakeText(root, "Sub", 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.52f), new Vector2(1600f, 80f));
            feedText = MakeText(root, "Feed", 30, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(900f, 400f));
            feedText.rectTransform.pivot = new Vector2(1f, 1f);
            feedText.rectTransform.anchoredPosition = new Vector2(-24f, -110f);
            lobbyText = MakeText(root, "Lobby", 40, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.45f), new Vector2(1700f, 760f));
            footerText = MakeText(root, "Footer", 26, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(1800f, 120f));
            footerText.rectTransform.pivot = new Vector2(0.5f, 0f);
            footerText.rectTransform.anchoredPosition = new Vector2(0f, 16f);
            centerText.text = subText.text = feedText.text = lobbyText.text = footerText.text = "";
        }

        RectTransform Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        Text MakeText(Transform parent, string name, int size, TextAnchor anchor, Vector2 anchorPos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchorPos;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = box;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.85f);
            o.effectDistance = new Vector2(2.5f, -2.5f);
            var s = go.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.4f);
            s.effectDistance = new Vector2(3f, -5f);
            return t;
        }

        Image MakeImage(Transform parent, Color c, Vector2 size)
        {
            var go = new GameObject("Img", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        // ------------------------------------------------------------------ API

        public static void Center(string big, string sub, float seconds)
        {
            if (!Net.FxAllowed) return;
            Net.EvCenter(big, sub, seconds);
            if (I == null) return;
            I.centerText.text = big;
            I.subText.text = sub;
            I.centerTimer = seconds;
            I.centerFade = 1f;
        }

        public static void Feed(string line)
        {
            if (!Net.FxAllowed) return;
            Net.EvFeed(line);
            if (I == null) return;
            I.feed.Add((line, Time.unscaledTime));
            if (I.feed.Count > 6) I.feed.RemoveAt(0);
        }

        public static void Popup(Vector3 world, string text, Color c, int size = 40)
        {
            if (!Net.FxAllowed) return;
            Net.EvPopup(world, text, c, size);
            if (I == null) return;
            var t = I.MakeText(I.popLayer, "Popup", size, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(600f, 80f));
            t.text = text;
            t.color = c;
            I.popups.Add(new PopupItem { text = t, world = world, t = 0f });
        }

        public static void Lobby(string text, string footer)
        {
            if (I == null) return;
            I.lobbyText.text = text;
            I.footerText.text = footer;
        }

        public static void ClearFeed()
        {
            if (I != null) I.feed.Clear();
        }

        // ------------------------------------------------------------------ Marcador

        void UpdateScores()
        {
            var gm = GameManager.I;
            bool show = gm != null && gm.Phase != GamePhase.Lobby;
            int n = show ? gm.Players.Count : 0;
            // Recuadros de color con borde claro, apilados arriba a la izquierda (como en BombSquad).
            while (scoreTexts.Count < n)
            {
                var panel = MakeImage(scoreRow, Color.white, new Vector2(330f, 48f));
                panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0f, 1f);
                panel.rectTransform.pivot = new Vector2(0f, 1f);
                var border = panel.gameObject.AddComponent<Outline>();
                border.effectDistance = new Vector2(3f, -3f);
                var txt = MakeText(panel.rectTransform, "Score", 32, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(310f, 48f));
                txt.rectTransform.pivot = new Vector2(0f, 0.5f);
                txt.rectTransform.anchoredPosition = new Vector2(14f, 0f);
                scoreIcons.Add(panel);
                scoreTexts.Add(txt);
            }
            for (int i = 0; i < scoreTexts.Count; i++)
            {
                bool on = i < n;
                scoreIcons[i].gameObject.SetActive(on);
                if (!on) continue;
                var p = gm.Players[i];
                var panel = scoreIcons[i];
                panel.rectTransform.anchoredPosition = new Vector2(0f, -i * 58f);
                Color bg = Color.Lerp(p.Color, Color.black, 0.45f);
                bg.a = p.Alive ? 0.85f : 0.45f;
                panel.color = bg;
                panel.GetComponent<Outline>().effectColor = new Color(Color.Lerp(p.Color, Color.white, 0.55f).r, Color.Lerp(p.Color, Color.white, 0.55f).g, Color.Lerp(p.Color, Color.white, 0.55f).b, 0.9f);
                scoreTexts[i].text = p.Name + "  <color=#FFFFFF>" + p.Kills + "</color><size=20>/" + gm.KillsToWin + "</size>";
                scoreTexts[i].color = Color.Lerp(p.Color, Color.white, 0.6f);
            }
        }

        // ------------------------------------------------------------------ Nombres flotantes

        Tag MakeTag(LBCharacter c)
        {
            var go = new GameObject("Tag", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(tagLayer, false);
            rt.sizeDelta = new Vector2(200f, 60f);
            var tag = new Tag { rt = rt };
            tag.name = MakeText(rt, "Name", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(300f, 40f));
            tag.name.text = c.Slot != null ? c.Slot.Name : "?";
            tag.name.color = c.Slot != null ? Color.Lerp(c.Slot.Color, Color.white, 0.3f) : Color.white;
            tag.extra = MakeText(rt, "Extra", 40, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(300f, 50f));
            tag.extra.rectTransform.anchoredPosition = new Vector2(0f, 48f);

            tag.chargeBg = MakeImage(rt, new Color(0f, 0f, 0f, 0.6f), new Vector2(84f, 14f));
            tag.chargeBg.rectTransform.anchoredPosition = new Vector2(0f, -36f);
            tag.chargeFill = MakeImage(tag.chargeBg.rectTransform, Color.yellow, new Vector2(80f, 10f));
            var cr = tag.chargeFill.rectTransform;
            cr.anchorMin = new Vector2(0f, 0.5f);
            cr.anchorMax = new Vector2(0f, 0.5f);
            cr.pivot = new Vector2(0f, 0.5f);
            cr.anchoredPosition = new Vector2(2f, 0f);
            tag.chargeBg.gameObject.SetActive(false);
            MakeBar(rt, new Vector2(0.5f, 0.5f), new Vector2(74f, 9f), out tag.stamBg, out tag.stamFill);
            tag.stamBg.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            tag.stamBg.gameObject.SetActive(false);
            return tag;
        }

        /// <summary>Barra horizontal (fondo oscuro + relleno anclado a la izquierda).</summary>
        void MakeBar(RectTransform parent, Vector2 anchor, Vector2 size, out Image bg, out Image fill)
        {
            bg = MakeImage(parent, new Color(0f, 0f, 0f, 0.65f), size);
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = anchor;
            fill = MakeImage(bg.rectTransform, Color.green, new Vector2(size.x - 4f, size.y - 4f));
            var fr = fill.rectTransform;
            fr.anchorMin = fr.anchorMax = new Vector2(0f, 0.5f);
            fr.pivot = new Vector2(0f, 0.5f);
            fr.anchoredPosition = new Vector2(2f, 0f);
        }

        /// <summary>Relleno de estamina: verde, amarillo y rojo cuando queda poca.</summary>
        static void SetBar(Image fill, float width, float height, float frac)
        {
            frac = Mathf.Clamp01(frac);
            fill.rectTransform.sizeDelta = new Vector2(width * frac, height);
            fill.color = frac > 0.5f
                ? Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.35f, 0.95f, 0.4f), (frac - 0.5f) * 2f)
                : Color.Lerp(new Color(0.95f, 0.2f, 0.15f), new Color(1f, 0.85f, 0.2f), frac * 2f);
        }

        // Posicion y tamano de cada seccion de la silueta (en el orden de CharacterVisual.Section).
        static readonly Vector4[] SilLayout =
        {
            new Vector4(0f, 38f, 24f, 24f),    // cabeza
            new Vector4(0f, 9f, 22f, 34f),     // torso
            new Vector4(-17f, 15f, 9f, 18f),   // brazo izq
            new Vector4(-19f, -5f, 9f, 20f),   // antebrazo + mano izq
            new Vector4(17f, 15f, 9f, 18f),    // brazo der
            new Vector4(19f, -5f, 9f, 20f),    // antebrazo + mano der
            new Vector4(-6f, -20f, 10f, 18f),  // muslo izq
            new Vector4(-6f, -40f, 10f, 20f),  // espinilla + pie izq
            new Vector4(6f, -20f, 10f, 18f),   // muslo der
            new Vector4(6f, -40f, 10f, 20f),   // espinilla + pie der
        };

        /// <summary>Silueta del personaje con 10 secciones coloreables (vista como el propio jugador).</summary>
        Image[] MakeSilhouette(RectTransform parent)
        {
            var box = new GameObject("Silhouette", typeof(RectTransform));
            var rt = (RectTransform)box.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(64f, 104f);
            rt.anchoredPosition = new Vector2(48f, 2f);
            var imgs = new Image[SilLayout.Length];
            for (int i = 0; i < SilLayout.Length; i++)
            {
                var l = SilLayout[i];
                var img = MakeImage(rt, Color.green, new Vector2(l.z, l.w));
                img.rectTransform.anchoredPosition = new Vector2(l.x, l.y);
                img.sprite = i == 0 ? Gfx.CircleSprite() : Gfx.RoundSprite();
                img.type = Image.Type.Simple;
                var o = img.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.6f);
                o.effectDistance = new Vector2(1.5f, -1.5f);
                imgs[i] = img;
            }
            return imgs;
        }

        static Color DangerColor(float d)
        {
            if (d < 0f) return new Color(0.12f, 0.12f, 0.12f, 0.45f); // perdida
            Color green = new Color(0.3f, 0.9f, 0.35f), yellow = new Color(1f, 0.85f, 0.2f), red = new Color(1f, 0.15f, 0.1f);
            return d < 0.5f ? Color.Lerp(green, yellow, d * 2f) : Color.Lerp(yellow, red, (d - 0.5f) * 2f);
        }

        DamagePanel MakeDamagePanel()
        {
            var dp = new DamagePanel();
            dp.bg = MakeImage(bottomRow, Color.white, new Vector2(280f, 118f));
            dp.bg.rectTransform.anchorMin = dp.bg.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            dp.bg.rectTransform.pivot = new Vector2(0.5f, 0f);
            var border = dp.bg.gameObject.AddComponent<Outline>();
            border.effectDistance = new Vector2(3f, -3f);
            dp.pct = MakeText(dp.bg.rectTransform, "Pct", 64, TextAnchor.MiddleCenter, new Vector2(0.63f, 0.66f), new Vector2(190f, 72f));
            dp.name = MakeText(dp.bg.rectTransform, "Name", 24, TextAnchor.MiddleCenter, new Vector2(0.63f, 0.27f), new Vector2(190f, 30f));
            MakeBar(dp.bg.rectTransform, new Vector2(0.63f, 0.09f), new Vector2(160f, 12f), out dp.stamBg, out dp.stamFill);
            dp.sil = MakeSilhouette(dp.bg.rectTransform);
            return dp;
        }

        void UpdateDamagePanels()
        {
            var gm = GameManager.I;
            bool show = gm != null && gm.Phase != GamePhase.Lobby;
            int n = show ? gm.Players.Count : 0;
            while (damagePanels.Count < n) damagePanels.Add(MakeDamagePanel());

            const float spacing = 300f;
            float x0 = -(n - 1) * spacing * 0.5f;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < damagePanels.Count; i++)
            {
                var dp = damagePanels[i];
                bool on = i < n;
                dp.bg.gameObject.SetActive(on);
                if (!on) continue;

                var p = gm.Players[i];
                if (dp.slot != p)
                {
                    dp.slot = p;
                    dp.shown = 0f;
                }
                bool alive = p.Alive;
                float target = alive ? p.Character.Damage : 0f;
                if (target > dp.shown + 0.5f) dp.pop = 1f;
                dp.shown = target < dp.shown ? target : Mathf.MoveTowards(dp.shown, target, dt * 140f);
                dp.pop = Mathf.Max(0f, dp.pop - dt * 4f);

                // Al recibir un golpe el numero salta y tiembla.
                Vector2 shake = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * dp.pop * 7f;
                dp.bg.rectTransform.anchoredPosition = new Vector2(x0 + i * spacing, 0f) + shake;
                Color bg = Color.Lerp(p.Color, Color.black, 0.5f);
                bg.a = alive ? 0.85f : 0.4f;
                dp.bg.color = bg;
                Color edge = Color.Lerp(p.Color, Color.white, 0.5f);
                dp.bg.GetComponent<Outline>().effectColor = new Color(edge.r, edge.g, edge.b, 0.9f);

                dp.pct.text = alive ? Mathf.RoundToInt(dp.shown) + "<size=36>%</size>" : "-";
                dp.pct.color = PercentColor(dp.shown);
                float s = 1f + dp.pop * 0.35f;
                dp.pct.rectTransform.localScale = new Vector3(s, s, 1f);
                dp.name.text = p.Name;
                dp.stamBg.gameObject.SetActive(alive);
                if (alive) SetBar(dp.stamFill, 156f, 8f, p.Character.Stamina / Tuning.StaminaMax);

                // Silueta: verde sano -> amarillo -> rojo a punto de perderse; gris si ya se perdio.
                var vis = alive ? p.Character.Visual : null;
                for (int si = 0; si < dp.sil.Length; si++)
                {
                    float d = vis != null ? vis.SectionDanger((CharacterVisual.Section)si) : -1f;
                    dp.sil[si].color = alive ? DangerColor(d) : new Color(0.3f, 0.3f, 0.3f, 0.35f);
                }
                dp.name.color = Color.Lerp(p.Color, Color.white, 0.6f);
            }
        }

        /// <summary>Blanco a 0%, amarillo, naranja, rojo y rojo oscuro a partir de ~200%.</summary>
        public static Color PercentColor(float pct)
        {
            if (pct < 50f) return Color.Lerp(Color.white, new Color(1f, 0.92f, 0.3f), pct / 50f);
            if (pct < 100f) return Color.Lerp(new Color(1f, 0.92f, 0.3f), new Color(1f, 0.5f, 0.1f), (pct - 50f) / 50f);
            if (pct < 160f) return Color.Lerp(new Color(1f, 0.5f, 0.1f), new Color(0.95f, 0.12f, 0.08f), (pct - 100f) / 60f);
            return Color.Lerp(new Color(0.95f, 0.12f, 0.08f), new Color(0.55f, 0.02f, 0.02f), Mathf.Clamp01((pct - 160f) / 100f));
        }

        void UpdateTags(Camera cam)
        {
            foreach (var c in LBCharacter.All)
                if (!tags.ContainsKey(c)) tags[c] = MakeTag(c);

            var dead = new List<LBCharacter>();
            foreach (var kv in tags)
            {
                var c = kv.Key;
                var tag = kv.Value;
                if (c == null)
                {
                    if (tag.rt != null) Destroy(tag.rt.gameObject);
                    dead.Add(c);
                    continue;
                }
                Vector3 sp = cam.WorldToScreenPoint(c.HeadPos + Vector3.up * 0.5f);
                bool vis = sp.z > 0f && !c.Dead;
                tag.rt.gameObject.SetActive(vis);
                if (!vis) continue;
                tag.rt.position = sp;


                // Barra de carga del lanzamiento (solo jugadores humanos).
                float charge = c.ThrowCharge;
                bool showCharge = charge >= 0f && c.Slot != null && !c.Slot.IsBot;
                tag.chargeBg.gameObject.SetActive(showCharge);

                // Estamina bajo el nombre (solo humanos, y solo si no esta llena).
                bool showStam = c.Slot != null && !c.Slot.IsBot && c.Stamina < Tuning.StaminaMax - 0.5f;
                tag.stamBg.gameObject.SetActive(showStam);
                if (showStam) SetBar(tag.stamFill, 70f, 5f, c.Stamina / Tuning.StaminaMax);
                if (showCharge)
                {
                    tag.chargeFill.rectTransform.sizeDelta = new Vector2(80f * charge, 10f);
                    tag.chargeFill.color = Color.Lerp(new Color(1f, 0.95f, 0.3f), new Color(1f, 0.35f, 0.1f), charge);
                }

                if (c.Cursed)
                {
                    tag.extra.text = Mathf.CeilToInt(c.CurseTimer).ToString();
                    tag.extra.color = new Color(0.85f, 0.4f, 1f);
                }
                else if (c.IsFrozen)
                {
                    tag.extra.text = "*";
                    tag.extra.color = new Color(0.6f, 0.9f, 1f);
                }
                else tag.extra.text = "";
            }
            foreach (var d in dead) tags.Remove(d);
        }

        void UpdatePopups(Camera cam)
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                p.t += Time.unscaledDeltaTime;
                float k = p.t / 1.4f;
                if (k >= 1f || p.text == null)
                {
                    if (p.text != null) Destroy(p.text.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                Vector3 sp = cam.WorldToScreenPoint(p.world + Vector3.up * k * 1.2f);
                p.text.rectTransform.position = sp;
                float s = k < 0.12f ? Mathf.Lerp(0.3f, 1.15f, k / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((k - 0.12f) / 0.1f));
                p.text.rectTransform.localScale = new Vector3(s, s, 1f);
                var col = p.text.color;
                col.a = k > 0.7f ? 1f - (k - 0.7f) / 0.3f : 1f;
                p.text.color = col;
            }
        }

        void LateUpdate()
        {
            var cam = CameraRig.I != null ? CameraRig.I.Cam : Camera.main;
            if (cam == null) return;

            UpdateScores();
            UpdateDamagePanels();
            UpdateTags(cam);
            UpdatePopups(cam);

            if (centerTimer > 0f)
            {
                centerTimer -= Time.unscaledDeltaTime;
                if (centerTimer <= 0f) centerFade = 1f;
            }
            else if (centerFade > 0f)
            {
                centerFade -= Time.unscaledDeltaTime * 3f;
                if (centerFade <= 0f)
                {
                    centerText.text = "";
                    subText.text = "";
                }
            }
            float a = centerTimer > 0f ? 1f : Mathf.Clamp01(centerFade);
            centerText.color = new Color(1f, 1f, 1f, a);
            subText.color = new Color(1f, 0.92f, 0.6f, a);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < feed.Count; i++)
            {
                float age = Time.unscaledTime - feed[i].time;
                if (age > 7f) continue;
                sb.AppendLine(feed[i].text);
            }
            feedText.text = sb.ToString();
        }
    }
}
