using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Cuerpo visual del personaje: piloto de motocross estilo stickman (casco, peto, coderas,
    /// rodilleras, guantes y botas) con extremidades tipo palo.
    /// Cabeza, manos y pies son puntos con muelle que siguen una pose animada
    /// procedimentalmente; en K.O. los muelles se aflojan y el cuerpo queda de trapo.
    /// </summary>
    public partial class CharacterVisual : MonoBehaviour
    {
        LBCharacter ch;
        Transform body, head, eyeL, eyeR;
        readonly Transform[] hands = new Transform[2];
        readonly Transform[] feet = new Transform[2];
        readonly Transform[] upperArm = new Transform[2];
        readonly Transform[] foreArm = new Transform[2];
        readonly Transform[] thigh = new Transform[2];
        readonly Transform[] shin = new Transform[2];

        Vector3 headP, headV, prevHeadT;
        readonly Vector3[] prevHandT = new Vector3[2], prevFootT = new Vector3[2];
        readonly Vector3[] handP = new Vector3[2], handV = new Vector3[2];
        readonly Vector3[] footP = new Vector3[2], footV = new Vector3[2];
        readonly Vector3[] frozenHand = new Vector3[2], frozenFoot = new Vector3[2];
        Vector3 frozenHead;

        readonly List<Material> mats = new List<Material>();
        readonly List<Color> baseColors = new List<Color>();
        Material gloveMat;
        GameObject shield;
        Material shieldMat;
        float shieldPulse;

        bool floppy, frozen, cursed, gloves, hidden, initialized;
        float flash, walkPhase, stride = 0.26f, blinkTimer = 2f;
        Vector3 lastPos;
        Color lastEmission = new Color(-1, -1, -1, -1);

        public Vector3 HeadPosition => head != null ? head.position : transform.position + Vector3.up * 1.17f;

        Material NewMat(Color c, float spec = 0.45f, float gloss = 0.55f)
        {
            var m = Gfx.Toon(c, spec, gloss, 0.3f);
            if (m.HasProperty("_VertexDamage")) m.SetFloat("_VertexDamage", 1f); // hollin y grietas por vertice
            mats.Add(m);
            baseColors.Add(c);
            return m;
        }

        // Proporciones del cuerpo estilo stickman.
        const float ShoulderX = 0.21f, ShoulderY = 0.93f, HipX = 0.1f, HipY = 0.47f, NeckY = 1.0f;
        const float EyeOpen = 0.13f;

        static readonly Dictionary<string, Mesh> boxCache = new Dictionary<string, Mesh>();

        /// <summary>Caja redondeada cacheada por tamano (respaldo si faltan modelos de Blender).</summary>
        static Mesh RBox(float x, float y, float z, float r)
        {
            string k = x + "_" + y + "_" + z + "_" + r;
            if (!boxCache.TryGetValue(k, out var m))
            {
                m = MeshBuilder.RoundedBox(new Vector3(x, y, z), r);
                boxCache[k] = m;
            }
            return m;
        }

        Transform neck;
        readonly Transform[] kneePad = new Transform[2], elbowPad = new Transform[2], shoulderPad = new Transform[2];
        readonly GameObject[] boxingGlove = new GameObject[2];

        // ------------------------------------------------------------------ Capas del cuerpo (dano por trozos)
        //
        // Cada pieza de cada capa (armadura, ropa, piel, musculo, hueso) esta dividida en muchos trozos
        // organicos con su propia resistencia (ver BodyDamage). Todas las capas se dibujan a la vez: las
        // interiores son algo mas pequenas y solo se ven donde se han desprendido los trozos de encima.

        public enum BodyZone { HeadFront, HeadBack, ChestFront, ChestBack, ShoulderL, ShoulderR, ArmL, ArmR, LegL, LegR }

        BodyDamage damage;
        Material armorMat, accentMat, skinTone, muscleMat, boneMat, tendonMat;

        // Cuantos trozos tiene cada pieza (el casco, 100).
        static readonly Dictionary<string, int> ChunkCounts = new Dictionary<string, int>
        {
            { "helmet_shell", 100 }, { "helmet_visor", 14 }, { "helmet_chin", 24 }, { "helmet_goggles", 18 },
            { "Stripe", 20 }, { "Crown", 24 }, { "Band", 18 }, { "Nape", 24 },
            { "head_skin_front", 60 }, { "head_skin_back", 45 }, { "head_hair", 35 }, { "head_brows", 4 },
            { "head_muscle_front", 50 }, { "head_muscle_back", 40 },
            { "torso_cloth_front", 50 }, { "torso_cloth_back", 50 }, { "armor_chest_front", 70 }, { "armor_chest_back", 60 },
            { "armor_straps_front", 10 }, { "armor_straps_back", 10 },
            { "torso_skin_front", 55 }, { "torso_skin_back", 55 }, { "torso_muscle_front", 60 }, { "torso_muscle_back", 55 },
            { "armor_shoulder", 24 }, { "Skin", 18 }, { "Deltoid", 12 }, { "Cloth", 26 }, { "Muscle", 22 }, { "Tendon", 6 },
            { "glove", 16 }, { "HandSkin", 12 }, { "HandMuscle", 10 },
            { "boot", 26 }, { "boot_sole", 6 }, { "boot_buckles", 8 }, { "FootSkin", 12 }, { "FootMuscle", 10 },
            { "knee_guard", 18 }, { "knee_straps", 6 }, { "elbow_guard", 12 },
        };

        /// <summary>Contenedor de una pieza: cabeza, cuerpo, hombro, segmento de extremidad, mano, pie o almohadilla.</summary>
        Transform ContainerOf(Transform r)
        {
            var c = r.parent;
            while (c != null && c.parent != null && c.parent != transform && c.parent != body) c = c.parent;
            return c != null ? c : transform;
        }

        void Add(BodyZone z, int layer, params GameObject[] gos)
        {
            foreach (var go in gos) Add(z, layer, go);
        }

        /// <summary>Registra las piezas de una capa en el sistema de dano por trozos.</summary>
        void Add(BodyZone z, int layer, GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                bool cloth = n == "Cloth" || n.StartsWith("torso_cloth");
                float depth = layer == 0 ? (cloth ? 0.5f : 0f) : layer;
                bool destructible = layer < 3 && !n.Contains("teeth") && !n.Contains("holes");
                float hp = layer == 0 ? (cloth ? 0.55f : 1f) : (layer == 1 ? (n.Contains("hair") || n.Contains("brows") ? 0.45f : 0.8f) : 1f);
                int chunks = ChunkCounts.TryGetValue(n, out int cc) ? cc : 20;
                damage.AddPart(r, ContainerOf(r.transform), depth, destructible, hp, chunks);
            }
        }

        /// <summary>Pieza del cuerpo: modelo de Blender en su sitio, o primitiva de respaldo.</summary>
        GameObject Piece(string model, Transform parent, Material mat, Mesh fallback, Vector3 fbPos, Vector3 fbScale)
        {
            var mesh = Gfx.ModelRaw(model);
            if (mesh != null) return Gfx.Part(model, parent, mesh, mat, Vector3.zero, Vector3.one);
            return Gfx.Part(model, parent, fallback, mat, fbPos, fbScale);
        }

        /// <summary>Segmento de extremidad con sus 4 capas (ropa, piel, musculo, hueso) dentro de un contenedor que se estira.</summary>
        Transform LimbSegment(string name, Transform parent, Material cloth, BodyZone zone)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var cap = Gfx.Prim(PrimitiveType.Capsule);
            var cm = Gfx.ModelRaw("limb_cloth");
            Add(zone, 0, Gfx.Part("Cloth", root, cm != null ? cm : cap, cloth, Vector3.zero, Vector3.one));
            var sm = Gfx.ModelRaw("limb_skin");
            Add(zone, 1, Gfx.Part("Skin", root, sm != null ? sm : cap, skinTone, Vector3.zero, new Vector3(0.92f, 1f, 0.92f)));
            var mm = Gfx.ModelRaw("limb_muscle");
            Add(zone, 2, Gfx.Part("Muscle", root, mm != null ? mm : cap, muscleMat, Vector3.zero, new Vector3(0.9f, 0.98f, 0.9f)));
            var tm = Gfx.ModelRaw("limb_tendon");
            if (tm != null) Add(zone, 2, Gfx.Part("Tendon", root, tm, tendonMat, Vector3.zero, new Vector3(0.9f, 0.98f, 0.9f)));
            var bm = Gfx.ModelRaw("limb_bone");
            Add(zone, 3, Gfx.Part("Bone", root, bm != null ? bm : cap, boneMat, Vector3.zero, bm != null ? new Vector3(0.62f, 1f, 0.62f) : new Vector3(0.4f, 1f, 0.4f)));
            return root;
        }

        static readonly Color[] SkinTones =
        {
            new Color(1f, 0.8f, 0.64f), new Color(0.87f, 0.64f, 0.46f), new Color(0.62f, 0.42f, 0.28f),
            new Color(0.95f, 0.74f, 0.58f), new Color(0.45f, 0.3f, 0.2f),
        };

        static readonly Color[] HairColors =
        {
            new Color(0.25f, 0.15f, 0.08f), new Color(0.08f, 0.07f, 0.07f), new Color(0.95f, 0.78f, 0.35f),
            new Color(0.72f, 0.26f, 0.1f), new Color(0.35f, 0.2f, 0.12f),
        };

        /// <summary>
        /// Construye el piloto de motocross stickman con todas sus capas (armadura, piel, musculo, hueso)
        /// repartidas en zonas. Las piezas vienen de Blender/generate_body.py; si no existen se usan primitivas.
        /// </summary>
        public void Build(LBCharacter c, Color color, Color highlight)
        {
            ch = c;
            var t = transform;
            damage = new BodyDamage(c.Col);
            int idx = c.Slot != null ? Mathf.Abs(c.Slot.Index) : 0;
            var sphere = Gfx.Prim(PrimitiveType.Sphere);
            var capsule = Gfx.Prim(PrimitiveType.Capsule);

            armorMat = NewMat(color, 0.85f, 0.8f);
            var jersey = NewMat(Color.Lerp(color, Color.black, 0.15f), 0.35f, 0.4f);
            accentMat = NewMat(highlight, 0.7f, 0.7f);
            var pants = NewMat(Color.Lerp(new Color(0.14f, 0.14f, 0.17f), highlight, 0.2f), 0.35f, 0.4f);
            var dark = NewMat(new Color(0.09f, 0.09f, 0.11f), 0.6f, 0.7f);
            var bootMat = NewMat(new Color(0.93f, 0.93f, 0.95f), 0.7f, 0.7f);
            skinTone = NewMat(SkinTones[idx % SkinTones.Length], 0.3f, 0.4f);
            var hair = NewMat(HairColors[(idx * 3 + 1) % HairColors.Length], 0.5f, 0.6f);
            muscleMat = NewTexMat(Color.white, Gfx.MuscleTex(), 0.6f, 0.75f);
            boneMat = NewTexMat(Color.white, Gfx.BoneTex(), 0.45f, 0.5f);
            tendonMat = NewMat(new Color(0.95f, 0.84f, 0.8f), 0.8f, 0.8f);
            var cavity = NewMat(new Color(0.33f, 0.05f, 0.06f), 0.5f, 0.6f);
            var teeth = NewMat(new Color(1f, 1f, 0.96f), 0.9f, 0.9f);
            gloveMat = NewMat(new Color(0.85f, 0.08f, 0.06f), 0.9f, 0.8f);
            var eyeWhite = NewMat(Color.white, 0.8f, 0.9f);
            var eyeBlack = NewMat(new Color(0.05f, 0.05f, 0.08f), 1f, 0.95f);
            foreach (var m in new[] { eyeWhite, eyeBlack, gloveMat }) m.SetFloat("_VertexDamage", 0f);
            // Color de las grietas segun el material: armadura oscura, piel rasgunada, musculo desgarrado.
            foreach (var m in new[] { skinTone, hair }) m.SetColor("_DamageColor", new Color(0.62f, 0.12f, 0.16f));
            foreach (var m in new[] { muscleMat, tendonMat }) m.SetColor("_DamageColor", new Color(0.28f, 0.03f, 0.04f));
            foreach (var m in new[] { jersey, pants }) m.SetColor("_DamageColor", new Color(0.2f, 0.12f, 0.08f));

            // ================================================================ Torso
            body = new GameObject("Body").transform;
            body.SetParent(t, false);
            Gfx.Part("Hips", body, RBox(0.3f, 0.16f, 0.22f, 0.06f), pants, new Vector3(0f, HipY + 0.03f, 0f), Vector3.one);

            Add(BodyZone.ChestFront, 0,
                Piece("torso_cloth_front", body, jersey, sphere, new Vector3(0f, 0.77f, 0f), new Vector3(0.35f, 0.54f, 0.26f)),
                Piece("armor_chest_front", body, armorMat, RBox(0.4f, 0.3f, 0.28f, 0.09f), new Vector3(0f, 0.84f, 0f), Vector3.one));
            if (Gfx.ModelRaw("armor_straps_front") != null)
            {
                Add(BodyZone.ChestFront, 0, Piece("armor_straps_front", body, dark, null, Vector3.zero, Vector3.one));
                Add(BodyZone.ChestBack, 0, Piece("armor_straps_back", body, dark, null, Vector3.zero, Vector3.one));
            }
            Add(BodyZone.ChestBack, 0,
                Piece("torso_cloth_back", body, jersey, sphere, new Vector3(0f, 0.77f, 0f), new Vector3(0.35f, 0.54f, 0.26f)),
                Piece("armor_chest_back", body, armorMat, RBox(0.3f, 0.22f, 0.05f, 0.02f), new Vector3(0f, 0.85f, -0.14f), Vector3.one));
            Add(BodyZone.ChestFront, 1, Piece("torso_skin_front", body, skinTone, sphere, new Vector3(0f, 0.77f, 0f), new Vector3(0.34f, 0.53f, 0.25f)));
            Add(BodyZone.ChestBack, 1, Piece("torso_skin_back", body, skinTone, sphere, new Vector3(0f, 0.77f, 0f), new Vector3(0.34f, 0.53f, 0.25f)));
            Add(BodyZone.ChestFront, 2, Piece("torso_muscle_front", body, muscleMat, sphere, new Vector3(0f, 0.77f, 0f), new Vector3(0.32f, 0.52f, 0.24f)));
            Add(BodyZone.ChestBack, 2, Piece("torso_muscle_back", body, muscleMat, sphere, new Vector3(0f, 0.77f, 0f), new Vector3(0.32f, 0.52f, 0.24f)));
            Add(BodyZone.ChestFront, 3,
                Piece("torso_bone_front", body, boneMat, RBox(0.04f, 0.25f, 0.03f, 0.01f), new Vector3(0f, 0.83f, 0.12f), Vector3.one),
                Piece("torso_cavity_front", body, cavity, sphere, new Vector3(0f, 0.79f, 0f), new Vector3(0.28f, 0.47f, 0.2f)));
            Add(BodyZone.ChestBack, 3,
                Piece("torso_bone_back", body, boneMat, RBox(0.05f, 0.45f, 0.04f, 0.015f), new Vector3(0f, 0.78f, -0.1f), Vector3.one),
                Piece("torso_cavity_back", body, cavity, sphere, new Vector3(0f, 0.79f, 0f), new Vector3(0.28f, 0.47f, 0.2f)));

            // Hombros: hombrera (armadura) y debajo la articulacion (piel, deltoides, cabeza del humero).
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1f : 1f;
                var zone = s == 0 ? BodyZone.ShoulderL : BodyZone.ShoulderR;
                var sp = new GameObject("Shoulder").transform;
                sp.SetParent(body, false);
                sp.localPosition = new Vector3(sx * 0.22f, 0.96f, 0f);
                sp.localRotation = Quaternion.Euler(0f, 0f, -sx * 18f);
                shoulderPad[s] = sp;
                var pad = Piece("armor_shoulder", sp, armorMat, sphere, Vector3.zero, new Vector3(0.21f, 0.13f, 0.23f));
                pad.transform.localScale = new Vector3(pad.transform.localScale.x * -sx, pad.transform.localScale.y, pad.transform.localScale.z);
                Add(zone, 0, pad);
                Add(zone, 1, Gfx.Part("Skin", sp, sphere, skinTone, new Vector3(0f, -0.03f, 0f), new Vector3(0.15f, 0.13f, 0.15f)));
                Add(zone, 2, Gfx.Part("Deltoid", sp, sphere, muscleMat, new Vector3(0f, -0.03f, 0f), new Vector3(0.14f, 0.13f, 0.14f)));
                Add(zone, 3, Gfx.Part("Humerus", sp, sphere, boneMat, new Vector3(0f, -0.035f, 0f), new Vector3(0.1f, 0.1f, 0.1f)));
            }

            // ================================================================ Cuello (se estira cada frame)
            neck = LimbSegment("Neck", t, jersey, BodyZone.HeadBack);

            // ================================================================ Cabeza
            head = new GameObject("Head").transform;
            head.SetParent(t, false);
            int style = idx % 5;
            Material visorMat = style % 2 == 0 ? accentMat : armorMat;
            Material chinMat = style % 3 == 1 ? armorMat : accentMat;

            // Armadura: la carcasa y la visera son "detras/arriba"; mentonera y gafas son "delante".
            var shell = Piece("helmet_shell", head, armorMat, sphere, new Vector3(0f, 0.02f, -0.01f), new Vector3(0.56f, 0.55f, 0.57f));
            Add(BodyZone.HeadBack, 0, shell, Piece("helmet_visor", head, visorMat, RBox(0.42f, 0.035f, 0.26f, 0.015f), new Vector3(0f, 0.17f, 0.24f), Vector3.one));
            Add(BodyZone.HeadBack, 0, HelmetDecal(style, sphere));
            Add(BodyZone.HeadFront, 0,
                Piece("helmet_chin", head, chinMat, RBox(0.3f, 0.12f, 0.16f, 0.05f), new Vector3(0f, -0.15f, 0.2f), Vector3.one),
                Piece("helmet_goggles", head, dark, RBox(0.36f, 0.06f, 0.05f, 0.02f), new Vector3(0f, 0.09f, 0.25f), Vector3.one));
            // Piel
            Add(BodyZone.HeadFront, 1, Piece("head_skin_front", head, skinTone, sphere, Vector3.zero, Vector3.one * 0.52f));
            if (Gfx.ModelRaw("head_brows") != null) Add(BodyZone.HeadFront, 1, Piece("head_brows", head, hair, null, Vector3.zero, Vector3.one));
            Add(BodyZone.HeadBack, 1,
                Piece("head_skin_back", head, skinTone, sphere, Vector3.zero, Vector3.one * 0.52f),
                Piece("head_hair", head, hair, sphere, new Vector3(0f, 0.08f, -0.05f), new Vector3(0.54f, 0.4f, 0.5f)));
            // Musculo (con dientes al descubierto delante)
            var teethGo = Piece("head_teeth", head, teeth, RBox(0.2f, 0.06f, 0.03f, 0.01f), new Vector3(0f, -0.12f, 0.23f), Vector3.one);
            Add(BodyZone.HeadFront, 2, Piece("head_muscle_front", head, muscleMat, sphere, Vector3.zero, Vector3.one * 0.5f), teethGo);
            Add(BodyZone.HeadBack, 2, Piece("head_muscle_back", head, muscleMat, sphere, Vector3.zero, Vector3.one * 0.5f));
            // Hueso: craneo con cuencas y fosa nasal; los dientes siguen ahi
            Add(BodyZone.HeadFront, 3,
                Piece("skull_front", head, boneMat, sphere, Vector3.zero, Vector3.one * 0.47f),
                Piece("skull_holes", head, dark, sphere, new Vector3(0f, -0.05f, 0.22f), new Vector3(0.05f, 0.05f, 0.03f)),
                teethGo);
            Add(BodyZone.HeadBack, 3, Piece("skull_back", head, boneMat, sphere, Vector3.zero, Vector3.one * 0.47f));

            // Ojos: siempre visibles (dentro de las gafas, sobre la cara o en las cuencas del craneo).
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1f : 1f;
                var eye = Gfx.Part("Eye", head, sphere, eyeWhite, new Vector3(sx * 0.082f, 0.02f, 0.255f), new Vector3(0.1f, EyeOpen, 0.07f)).transform;
                Gfx.Part("Pupil", eye, sphere, eyeBlack, new Vector3(-sx * 0.08f, 0f, 0.42f), new Vector3(0.5f, 0.5f, 0.4f));
                if (s == 0) eyeL = eye; else eyeR = eye;
            }

            // ================================================================ Extremidades
            for (int s = 0; s < 2; s++)
            {
                var arm = s == 0 ? BodyZone.ArmL : BodyZone.ArmR;
                var leg = s == 0 ? BodyZone.LegL : BodyZone.LegR;

                // Mano: guante -> piel -> musculo -> hueso.
                hands[s] = new GameObject("Hand").transform;
                hands[s].SetParent(t, false);
                Add(arm, 0, Piece("glove", hands[s], accentMat, sphere, Vector3.zero, new Vector3(0.17f, 0.16f, 0.18f)));
                var hs = Gfx.ModelRaw("hand_skin");
                Add(arm, 1, hs != null ? Gfx.Part("HandSkin", hands[s], hs, skinTone, Vector3.zero, Vector3.one)
                                       : Gfx.Part("HandSkin", hands[s], sphere, skinTone, Vector3.zero, Vector3.one * 0.15f));
                Add(arm, 2, hs != null ? Gfx.Part("HandMuscle", hands[s], hs, muscleMat, Vector3.zero, Vector3.one * 0.96f)
                                       : Gfx.Part("HandMuscle", hands[s], sphere, muscleMat, Vector3.zero, Vector3.one * 0.14f));
                Add(arm, 3, Piece("hand_bone", hands[s], boneMat, sphere, Vector3.zero, Vector3.one * 0.09f));
                boxingGlove[s] = Gfx.Part("BoxingGlove", hands[s], sphere, gloveMat, Vector3.zero, Vector3.one * 0.32f);
                boxingGlove[s].SetActive(false);

                // Pie: bota -> pie descalzo -> musculo -> hueso.
                feet[s] = new GameObject("Foot").transform;
                feet[s].SetParent(t, false);
                Add(leg, 0,
                    Piece("boot", feet[s], bootMat, RBox(0.18f, 0.24f, 0.3f, 0.06f), new Vector3(0f, 0.07f, 0.02f), Vector3.one),
                    Piece("boot_sole", feet[s], dark, RBox(0.2f, 0.05f, 0.32f, 0.02f), new Vector3(0f, -0.045f, 0.02f), Vector3.one),
                    Piece("boot_buckles", feet[s], accentMat, RBox(0.19f, 0.025f, 0.13f, 0.01f), new Vector3(0f, 0.1f, 0f), Vector3.one));
                var fs = Gfx.ModelRaw("foot_skin");
                Add(leg, 1, fs != null ? Gfx.Part("FootSkin", feet[s], fs, skinTone, Vector3.zero, Vector3.one)
                                       : Gfx.Part("FootSkin", feet[s], RBox(0.11f, 0.08f, 0.24f, 0.03f), skinTone, new Vector3(0f, -0.03f, 0.04f), Vector3.one));
                Add(leg, 2, fs != null ? Gfx.Part("FootMuscle", feet[s], fs, muscleMat, Vector3.zero, Vector3.one * 0.96f)
                                       : Gfx.Part("FootMuscle", feet[s], RBox(0.1f, 0.07f, 0.22f, 0.03f), muscleMat, new Vector3(0f, -0.03f, 0.04f), Vector3.one));
                Add(leg, 3, Piece("foot_bone", feet[s], boneMat, RBox(0.06f, 0.04f, 0.2f, 0.015f), new Vector3(0f, -0.04f, 0.04f), Vector3.one));

                upperArm[s] = LimbSegment("UpperArm", t, jersey, arm);
                foreArm[s] = LimbSegment("ForeArm", t, jersey, arm);
                thigh[s] = LimbSegment("Thigh", t, pants, leg);
                shin[s] = LimbSegment("Shin", t, pants, leg);

                elbowPad[s] = new GameObject("ElbowPad").transform;
                elbowPad[s].SetParent(t, false);
                Add(arm, 0, Piece("elbow_guard", elbowPad[s], armorMat, sphere, Vector3.zero, new Vector3(0.14f, 0.14f, 0.14f)));
                kneePad[s] = new GameObject("KneePad").transform;
                kneePad[s].SetParent(t, false);
                Add(leg, 0, Piece("knee_guard", kneePad[s], armorMat, RBox(0.17f, 0.22f, 0.1f, 0.04f), Vector3.zero, Vector3.one));
                if (Gfx.ModelRaw("knee_straps") != null) Add(leg, 0, Piece("knee_straps", kneePad[s], dark, null, Vector3.zero, Vector3.one));
            }

            // Todo lo que use materiales del cuerpo necesita colores de vertice (hollin/grietas).
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (damage.IsRegistered(r) || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_VertexDamage")) continue;
                if (r.sharedMaterial.GetFloat("_VertexDamage") < 0.5f) continue;
                damage.AddPart(r, ContainerOf(r.transform), 0.5f, false, 1f, 4);
            }
            damage.Finish();

            shieldMat = Gfx.Shield(new Color(0.65f, 0.35f, 1f, 0.12f), new Color(0.85f, 0.65f, 1f, 0.9f));
            shield = Gfx.Part("Shield", t, Gfx.Prim(PrimitiveType.Sphere), shieldMat, Vector3.zero, Vector3.one * 1.8f);
            var sr = shield.GetComponent<MeshRenderer>();
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            shield.SetActive(false);

            lastPos = t.position;
        }

        Material NewTexMat(Color c, Texture tex, float spec, float gloss)
        {
            var m = Gfx.Toon(c, spec, gloss, 0.25f, tex);
            if (m.HasProperty("_VertexDamage")) m.SetFloat("_VertexDamage", 1f);
            mats.Add(m);
            baseColors.Add(c);
            return m;
        }

        /// <summary>Diseno del casco segun el jugador (franjas, corona, banda o nuca de color).</summary>
        GameObject HelmetDecal(int style, Mesh sphere)
        {
            var root = new GameObject("HelmetDecal");
            root.transform.SetParent(head, false);
            var cyl = Gfx.Prim(PrimitiveType.Cylinder);
            switch (style)
            {
                case 0:
                    Gfx.Part("Stripe", root.transform, sphere, accentMat, new Vector3(0f, 0.025f, -0.01f), new Vector3(0.13f, 0.6f, 0.6f));
                    break;
                case 1:
                    for (int s = -1; s <= 1; s += 2)
                        Gfx.Part("Stripe", root.transform, sphere, accentMat, new Vector3(s * 0.09f, 0.025f, -0.01f), new Vector3(0.06f, 0.6f, 0.6f))
                            .transform.localRotation = Quaternion.Euler(0f, 0f, s * 14f);
                    break;
                case 2:
                    Gfx.Part("Crown", root.transform, sphere, accentMat, new Vector3(0f, 0.15f, -0.03f), new Vector3(0.52f, 0.34f, 0.52f));
                    break;
                case 3:
                    Gfx.Part("Band", root.transform, cyl, accentMat, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.6f, 0.04f, 0.6f));
                    break;
                default:
                    Gfx.Part("Nape", root.transform, sphere, accentMat, new Vector3(0f, 0f, -0.12f), new Vector3(0.52f, 0.5f, 0.42f));
                    break;
            }
            return root;
        }

        /// <summary>Explosion: dana los trozos expuestos que miran hacia ella (ver BodyDamage).</summary>
        public void ApplyBlastLayers(Vector3 blastPos, float strength, float radius)
        {
            if (!hidden && damage != null) damage.ApplyBlast(blastPos, strength, radius);
        }

        /// <summary>Salud: regenera la capa mas profunda danada y limpia parte del hollin.</summary>
        public void HealLayers(int levels)
        {
            if (damage != null) damage.Heal();
        }


        // ------------------------------------------------------------------ API de estado

        public void SetFloppy(bool b) { floppy = b; }

        /// <summary>Muere cayendo al vacio: sigue pataleando en vez de quedar como trapo.</summary>
        public void SetFallingDeath() { fallingDeath = true; }

        float strain, wriggle;

        const float FlailDelay = 0.45f;
        float airTime, flail;
        bool fallingDeath;
        public void Flash() { flash = 1f; }
        public void ShieldHit() { shieldPulse = 1f; }

        public void SetShield(bool b)
        {
            if (shield != null) shield.SetActive(b && !hidden);
            shieldPulse = b ? 1f : 0f;
        }

        public void SetGloves(bool b)
        {
            // Los guantes de boxeo tapan la mano sea cual sea su capa (guante, piel, musculo o hueso).
            gloves = b;
            for (int s = 0; s < 2; s++)
            {
                if (hands[s] == null) continue;
                foreach (var r in hands[s].GetComponentsInChildren<Renderer>(true))
                {
                    if (r.gameObject == boxingGlove[s]) continue;
                    r.enabled = !b && !hidden;
                }
                boxingGlove[s].SetActive(b && !hidden);
            }
        }

        public void SetCursed(bool b) { cursed = b; }

        public void SetFrozen(bool b)
        {
            if (frozen == b) return;
            frozen = b;
            if (b)
            {
                var t = transform;
                frozenHead = t.InverseTransformPoint(headP);
                for (int s = 0; s < 2; s++)
                {
                    frozenHand[s] = t.InverseTransformPoint(handP[s]);
                    frozenFoot[s] = t.InverseTransformPoint(footP[s]);
                }
            }
            Color ice = new Color(0.7f, 0.9f, 1f);
            for (int i = 0; i < mats.Count; i++)
            {
                mats[i].color = b ? Color.Lerp(baseColors[i], ice, 0.7f) : baseColors[i];
                if (mats[i].HasProperty("_Spec")) mats[i].SetFloat("_Spec", b ? 1.2f : 0.45f);
            }
        }

        public void Hide()
        {
            hidden = true;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        void OnDestroy()
        {
            foreach (var m in mats) if (m != null) Destroy(m);
            if (damage != null) damage.Dispose();
            if (shieldMat != null) Destroy(shieldMat);
        }

        // ------------------------------------------------------------------ Animacion

        /// <summary>
        /// Muelle que sigue a un objetivo en movimiento. El amortiguamiento actua sobre la velocidad
        /// RELATIVA al objetivo (no la absoluta): asi, al caminar o correr, la pieza no se queda
        /// rezagada detras del cuerpo y solo conserva el rebote propio.
        /// </summary>
        static void Spring(ref Vector3 p, ref Vector3 v, ref Vector3 prevTarget, Vector3 target, float dt, float k, float d, float g, float maxDist)
        {
            if ((target - prevTarget).sqrMagnitude > 4f)
            {
                // Teletransporte (aparecer, recolocar): sin estela.
                p = target;
                v = Vector3.zero;
                prevTarget = target;
                return;
            }
            Vector3 tv = (target - prevTarget) / dt;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 tg = Vector3.Lerp(prevTarget, target, (i + 1f) / steps);
                Vector3 a = (tg - p) * k + (tv - v) * d;
                a.y -= g;
                v += a * h;
                p += v * h;
            }
            prevTarget = target;
            Vector3 off = p - target;
            float m = off.magnitude;
            if (m > maxDist) p = target + off * (maxDist / m);
        }

        static void PlaceSeg(Transform seg, Vector3 a, Vector3 b, float thick)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            seg.position = (a + b) * 0.5f;
            seg.rotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            seg.localScale = new Vector3(thick, Mathf.Max(len * 0.5f, thick * 0.5f), thick);
        }

        void LateUpdate()
        {
            if (ch == null || hidden) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;

            Transform t = transform;
            Vector3 vel = (t.position - lastPos) / dt;
            lastPos = t.position;
            Vector3 hv = new Vector3(vel.x, 0f, vel.z);
            float speed = Mathf.Min(hv.magnitude, 12f);
            bool air = !ch.Grounded && !floppy && !frozen;
            float amp = Mathf.Clamp01(speed / 3f);
            float run01 = Mathf.Clamp01((speed - Tuning.WalkSpeed * 0.8f) / (Tuning.RunSpeed - Tuning.WalkSpeed * 0.8f));
            float time = Time.time;

            // Zancada sincronizada con la velocidad real: el pie apoyado se mueve hacia atras
            // a la misma velocidad que el cuerpo avanza, asi no "patina" sobre el suelo.
            stride = Mathf.Lerp(0.24f, 0.34f, run01);
            if (!air && !frozen && !floppy) walkPhase += speed / stride * dt;

            // Inclinacion y rebote del cuerpo
            float bob = 0f, lean = 0f, side = 0f;
            if (!floppy && !frozen)
            {
                bob = air ? 0f : Mathf.Abs(Mathf.Sin(walkPhase)) * 0.05f * amp + Mathf.Sin(time * 2.2f) * 0.008f;
                Vector3 lv = t.InverseTransformDirection(hv);
                lean = Mathf.Clamp(lv.z * 1.6f, -5f, 11f);
                side = Mathf.Clamp(-lv.x * 1.5f, -8f, 8f);
            }
            // --- Agarres: quien carga se esfuerza, quien es cargado se retuerce.
            bool carrying = ch.Holding != null && ch.Holding.AsCharacter != null && !floppy && !frozen;
            bool beingHeld = ch.HeldBy != null && !floppy && !frozen;
            strain = Mathf.MoveTowards(strain, carrying ? 1f : 0f, dt * 4f);
            wriggle = Mathf.MoveTowards(wriggle, beingHeld ? 1f : 0f, dt * 5f);
            float tug = carrying ? ch.Holding.AsCharacter.StruggleTug : 0f;
            float twist = 0f;
            if (strain > 0.001f)
            {
                // Peso: se echa hacia atras como contrapeso, baja el cuerpo y tiembla; los tirones lo sacuden.
                float shake = (Mathf.PerlinNoise(time * 18f, 0.3f) - 0.5f) * (3f + tug * 14f);
                lean = Mathf.Lerp(lean, -13f + Mathf.Sin(time * 3.1f) * 2f, strain) + shake * strain;
                side += (Mathf.Sin(time * 6.5f) * 3f + (Mathf.PerlinNoise(0.7f, time * 22f) - 0.5f) * tug * 18f) * strain;
                bob -= 0.08f * strain;
            }
            if (wriggle > 0.001f)
            {
                // Resistencia: gira el torso, se ladea y da sacudidas con cada forcejeo.
                float myTug = ch.StruggleTug;
                twist = (Mathf.Sin(time * 9f) * 22f + Mathf.Sin(time * 31f) * myTug * 18f) * wriggle;
                side += (Mathf.Sin(time * 7.3f) * 12f + myTug * Mathf.Sin(time * 40f) * 10f) * wriggle;
                lean += Mathf.Sin(time * 5.2f) * 8f * wriggle;
            }
            // Sin pie / sin pierna: cojear, gatear o arrastrarse.
            int moveMode = ch.MoveMode;
            if (!floppy && !frozen && moveMode > 0) ApplyLocomotionPose(moveMode, ref bob, ref lean, ref side, time, amp);
            body.localPosition = new Vector3(0f, bob, 0f);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(lean, twist, side), 1f - Mathf.Exp(-14f * dt));
            Quaternion bl = body.localRotation;
            Vector3 bp = body.localPosition;

            // Objetivos locales
            Vector3 headT, handT0, handT1, footT0, footT1;
            if (frozen)
            {
                headT = frozenHead;
                handT0 = frozenHand[0]; handT1 = frozenHand[1];
                footT0 = frozenFoot[0]; footT1 = frozenFoot[1];
            }
            else
            {
                headT = bp + bl * new Vector3(0f, 1.17f, 0.02f);
                // Cargando: cabeza hacia delante y abajo, temblando por el esfuerzo.
                if (strain > 0.001f)
                    headT += new Vector3((Mathf.PerlinNoise(time * 20f, 5f) - 0.5f) * 0.04f, -0.05f, 0.07f) * strain;
                // Cargado: sacude la cabeza de lado a lado.
                if (wriggle > 0.001f)
                    headT += new Vector3(Mathf.Sin(time * 11f) * 0.07f, 0f, 0f) * wriggle;
                handT0 = HandTarget(0, air, amp, run01, bp, bl);
                handT1 = HandTarget(1, air, amp, run01, bp, bl);
                footT0 = FootTarget(0, air, amp);
                footT1 = FootTarget(1, air, amp);
                // A gatas o arrastrandose: las manos apoyan en el suelo por delante (salvo si sostiene algo).
                if (!floppy && moveMode >= 2 && ch.Holding == null && !air)
                    CrawlTargets(moveMode, amp, ref handT0, ref handT1, ref footT0, ref footT1);
                else if (!floppy && moveMode == 1 && !air)
                    LimpTargets(amp, ref footT0, ref footT1);
            }

            // Pataleo en el aire: si lleva un rato lejos del suelo (saltando alto, saliendo volando
            // por una bomba o cayendo al vacio) agita los brazos como molino y pedalea.
            bool farFromGround = !Physics.Raycast(ch.Center, Vector3.down, 1.4f, ~0, QueryTriggerInteraction.Ignore);
            bool canFlail = !frozen && ch.HeldBy == null && (!ch.Dead || fallingDeath);
            airTime = farFromGround && canFlail ? airTime + dt : 0f;
            flail = Mathf.MoveTowards(flail, airTime > FlailDelay ? 1f : 0f, dt * 4f);
            if (flail > 0.001f)
            {
                float fa = time * 15f;
                Vector3 FlailHand(int s)
                {
                    float sx = s == 0 ? -1f : 1f, a = fa + s * Mathf.PI;
                    return new Vector3(sx * 0.48f, 0.95f + Mathf.Sin(a) * 0.28f, Mathf.Cos(a) * 0.32f);
                }
                Vector3 FlailFoot(int s)
                {
                    float sx = s == 0 ? -1f : 1f, b = fa * 1.15f + s * Mathf.PI;
                    return new Vector3(sx * 0.18f, 0.22f + Mathf.Sin(b) * 0.16f, Mathf.Cos(b) * 0.22f);
                }
                handT0 = Vector3.Lerp(handT0, FlailHand(0), flail);
                handT1 = Vector3.Lerp(handT1, FlailHand(1), flail);
                footT0 = Vector3.Lerp(footT0, FlailFoot(0), flail);
                footT1 = Vector3.Lerp(footT1, FlailFoot(1), flail);
                headT = Vector3.Lerp(headT, new Vector3(Mathf.Sin(time * 9f) * 0.05f, 1.17f, 0.02f), flail);
            }

            Vector3 wHead = t.TransformPoint(headT);
            Vector3 wHand0 = t.TransformPoint(handT0), wHand1 = t.TransformPoint(handT1);
            Vector3 wFoot0 = t.TransformPoint(footT0), wFoot1 = t.TransformPoint(footT1);
            if (!initialized)
            {
                initialized = true;
                headP = prevHeadT = wHead;
                handP[0] = prevHandT[0] = wHand0;
                handP[1] = prevHandT[1] = wHand1;
                footP[0] = prevFootT[0] = wFoot0;
                footP[1] = prevFootT[1] = wFoot1;
            }

            // Simulacion de muelles
            float k = floppy ? 70f : 900f;
            float d = floppy ? 7f : 52f;
            float g = floppy ? 18f : 0f;
            float maxD = floppy ? 0.45f : 0.22f;
            if (frozen) { k = 4000f; d = 120f; g = 0f; maxD = 0.02f; }
            // Mientras patalea, los miembros tienen fuerza aunque el cuerpo vaya dando vueltas.
            k = Mathf.Lerp(k, 900f, flail);
            d = Mathf.Lerp(d, 45f, flail);
            g = Mathf.Lerp(g, 0f, flail);
            maxD = Mathf.Lerp(maxD, 0.3f, flail);
            Spring(ref headP, ref headV, ref prevHeadT, wHead, dt, k * 1.4f, d, g * 0.5f, maxD * 0.6f);
            Spring(ref handP[0], ref handV[0], ref prevHandT[0], wHand0, dt, k, d, g, maxD);
            Spring(ref handP[1], ref handV[1], ref prevHandT[1], wHand1, dt, k, d, g, maxD);
            // De pie, los pies van casi rigidos (siguen la pisada con precision); en K.O. cuelgan sueltos.
            float fk = floppy ? k : 4000f, fd = floppy ? d : 120f;
            float fMax = floppy ? maxD : 0.06f;
            fk = Mathf.Lerp(fk, 1500f, flail);
            fd = Mathf.Lerp(fd, 70f, flail);
            fMax = Mathf.Lerp(fMax, 0.3f, flail);
            Spring(ref footP[0], ref footV[0], ref prevFootT[0], wFoot0, dt, fk, fd, g, fMax);
            Spring(ref footP[1], ref footV[1], ref prevFootT[1], wFoot1, dt, fk, fd, g, fMax);

            if (floppy && Physics.Raycast(ch.Center, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                float gy = hit.point.y + 0.07f;
                ClampY(ref headP, ref headV, gy + 0.2f);
                for (int s = 0; s < 2; s++)
                {
                    ClampY(ref handP[s], ref handV[s], gy);
                    ClampY(ref footP[s], ref footV[s], gy);
                }
            }

            // Colocar piezas
            head.position = headP;
            head.rotation = t.rotation * Quaternion.Euler(floppy ? Mathf.Sin(time * 7f) * 10f : lean * 0.3f, 0f, floppy ? Mathf.Cos(time * 5f) * 12f : 0f);
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1f : 1f;
                int armLoss = ch.ArmLoss[s], legLoss = ch.LegLoss[s];
                // Lo que se ha cortado ya no es del personaje: no se mueve.
                if (armLoss == 0)
                {
                    hands[s].position = handP[s];
                    hands[s].rotation = t.rotation;
                }
                if (legLoss == 0)
                {
                    feet[s].position = footP[s];
                    feet[s].rotation = t.rotation;
                }

                // Brazos tipo palo (grosor medio) con codera en la articulacion.
                Vector3 shoulder = t.TransformPoint(bp + bl * new Vector3(sx * ShoulderX, ShoulderY, 0f));
                Vector3 elbow = (shoulder + handP[s]) * 0.5f + t.right * sx * 0.07f - t.up * 0.05f - t.forward * 0.03f;
                if (armLoss < 2)
                {
                    PlaceSeg(upperArm[s], shoulder, elbow, 0.12f);
                    elbowPad[s].position = elbow;
                    elbowPad[s].rotation = t.rotation;
                }
                if (armLoss == 0) PlaceSeg(foreArm[s], elbow, handP[s], 0.11f);

                // Piernas: el espinillero entra en la bota; rodillera mirando al frente.
                Vector3 hip = t.TransformPoint(bp + bl * new Vector3(sx * HipX, HipY, 0f));
                Vector3 footTop = footP[s] + (legLoss == 0 ? feet[s].up : t.up) * 0.14f;
                Vector3 knee = (hip + footTop) * 0.5f + t.forward * 0.07f + t.right * sx * 0.02f;
                if (legLoss < 2) PlaceSeg(thigh[s], hip, knee, 0.14f);
                if (legLoss == 0)
                {
                    PlaceSeg(shin[s], knee, footTop, 0.12f);
                    Vector3 shinUp = knee - footTop;
                    if (shinUp.sqrMagnitude < 1e-6f) shinUp = t.up;
                    kneePad[s].position = knee + t.forward * 0.06f;
                    kneePad[s].rotation = Quaternion.LookRotation(t.forward, shinUp.normalized);
                }
            }

            // Cuello entre el torso y el casco.
            Vector3 neckBase = t.TransformPoint(bp + bl * new Vector3(0f, NeckY, 0f));
            Vector3 neckTop = headP - head.up * 0.2f;
            PlaceSeg(neck, neckBase, neckTop, 0.12f);

            // Parpadeo
            blinkTimer -= dt;
            float eyeY = (blinkTimer < 0.12f && !ch.Dead) ? 0.02f : Mathf.Lerp(EyeOpen, EyeOpen * 0.45f, strain); // ojos entrecerrados por el esfuerzo
            if (blinkTimer < 0f) blinkTimer = Random.Range(2f, 5f);
            if (ch.Dead) eyeY = 0.035f;
            eyeL.localScale = new Vector3(0.11f, eyeY, 0.05f);
            eyeR.localScale = new Vector3(0.11f, eyeY, 0.05f);

            // Escudo
            if (shield.activeSelf)
            {
                shieldPulse = Mathf.Max(0f, shieldPulse - dt * 3f);
                shield.transform.position = ch.Center;
                shield.transform.rotation = Quaternion.Euler(0f, time * 60f, 0f);
                float sc = 1.8f + Mathf.Sin(time * 6f) * 0.03f + shieldPulse * 0.25f;
                shield.transform.localScale = new Vector3(sc, sc, sc);
            }

            // Brillo de golpe / maldicion / hielo
            flash = Mathf.Max(0f, flash - dt * 5f);
            Color em = Color.white * (flash * 0.7f);
            if (cursed) em += new Color(0.6f, 0.1f, 0.7f) * (0.25f + 0.25f * Mathf.Sin(time * 14f));
            if (frozen) em += new Color(0.15f, 0.25f, 0.35f);
            em.a = 1f;
            if (em != lastEmission)
            {
                lastEmission = em;
                foreach (var m in mats) Gfx.SetEmission(m, em);
            }
        }

        static void ClampY(ref Vector3 p, ref Vector3 v, float minY)
        {
            if (p.y < minY)
            {
                p.y = minY;
                if (v.y < 0f) v.y = 0f;
                v.x *= 0.9f;
                v.z *= 0.9f;
            }
        }

        Vector3 HandTarget(int s, bool air, float amp, float run01, Vector3 bp, Quaternion bl)
        {
            float sx = s == 0 ? -1f : 1f;
            Vector3 h;
            if (ch.Holding != null && ch.Holding.AsCharacter != null)
            {
                // Sujeta al otro por los hombros (las manos siguen al cuerpo agarrado), con los brazos
                // temblando por el peso y mas aun cuando el otro forcejea.
                var victim = ch.Holding.AsCharacter;
                float c = Mathf.Max(0f, ch.ThrowCharge);
                float tr = 0.02f + victim.StruggleTug * 0.06f;
                float tt = Time.time * 24f + s * 3.7f;
                Vector3 grip = transform.InverseTransformPoint(victim.Center)
                               + new Vector3(sx * 0.27f, 0.14f + 0.15f * c, -0.12f - 0.25f * c)
                               + new Vector3(Mathf.PerlinNoise(tt, 1f) - 0.5f, Mathf.PerlinNoise(2f, tt) - 0.5f, 0f) * tr * 2f;
                Vector3 shoulder = new Vector3(sx * 0.27f, 0.93f, 0f);
                Vector3 reach = grip - shoulder;
                if (reach.magnitude > 0.8f) grip = shoulder + reach.normalized * 0.8f;
                return grip; // en espacio local del personaje, sin la inclinacion del torso
            }
            if (ch.Holding != null)
            {
                float c = Mathf.Max(0f, ch.ThrowCharge);
                h = new Vector3(sx * 0.2f, 1.02f + 0.2f * c, 0.36f - 0.6f * c);
            }
            else if (air)
            {
                h = new Vector3(sx * 0.46f, 0.98f, 0.05f);
            }
            else
            {
                float swing = -Mathf.Sin(walkPhase + (s == 0 ? 0f : Mathf.PI)) * 0.28f * amp;
                h = new Vector3(sx * (0.38f - run01 * 0.06f), 0.6f + Mathf.Abs(swing) * 0.3f * run01 + run01 * 0.1f, 0.06f + swing + run01 * 0.05f);
            }

            bool punching = ch.PunchTimer > 0f && ((ch.PunchRight && s == 1) || (!ch.PunchRight && s == 0));
            if (punching)
            {
                float k = 1f - ch.PunchTimer / Tuning.PunchDuration;
                float e = Mathf.Sin(Mathf.Min(1f, k * 1.6f) * Mathf.PI * 0.5f) * (k < 0.62f ? 1f : Mathf.Clamp01((1f - k) / 0.38f));
                h = Vector3.Lerp(h, new Vector3(sx * 0.08f, 0.88f, 0.82f), e);
            }

            if (ch.HeldBy != null)
            {
                float time = Time.time;
                float k = 1f + ch.StruggleTug * 0.8f; // cada forcejeo hace el movimiento mas violento
                if (ch.Clinching)
                    // Aferrado al agarrador: brazos alrededor de el, apretando.
                    h = new Vector3(sx * 0.22f, 0.95f + Mathf.Sin(time * 9f + s * 2f) * 0.03f, 0.45f);
                else if (ch.GrabbedFromBehind)
                {
                    // Por la espalda: manotazos hacia atras intentando golpear.
                    float a = time * 13f + s * Mathf.PI;
                    h = new Vector3(sx * 0.42f, 1.0f + Mathf.Sin(a) * 0.16f * k, -0.1f + Mathf.Cos(a) * 0.24f * k);
                }
                else
                    // De frente: empuja al agarrador con las manos para separarse.
                    h = new Vector3(sx * 0.24f, 0.98f + Mathf.Sin(time * 15f + s * 2f) * 0.05f * k, 0.38f + Mathf.Sin(time * 12f + s * Mathf.PI) * 0.12f * k);
            }

            return bp + bl * h;
        }

        Vector3 FootTarget(int s, bool air, float amp)
        {
            float sx = s == 0 ? -1f : 1f;
            if (air) return new Vector3(sx * 0.17f, 0.25f, s == 0 ? 0.1f : -0.06f);
            if (ch.HeldBy != null)
            {
                // Pataleo de resistencia (mas fuerte con cada forcejeo).
                float k = 1f + ch.StruggleTug * 0.7f;
                float b = Time.time * 16f + s * Mathf.PI;
                return new Vector3(sx * 0.19f, 0.13f + Mathf.Sin(b) * 0.12f * k, 0.04f + Mathf.Cos(b) * 0.18f * k);
            }
            float ph = walkPhase + (s == 0 ? 0f : Mathf.PI);
            // El pie se levanta solo mientras avanza (cos > 0) y queda plano mientras apoya.
            float lift = Mathf.Max(0f, Mathf.Cos(ph));
            Vector3 f = new Vector3(sx * 0.15f, 0.07f + lift * lift * 0.16f * amp, Mathf.Sin(ph) * stride * amp);
            // Cargando peso: piernas abiertas y una adelantada para afianzarse.
            if (strain > 0.001f)
                f += new Vector3(sx * 0.07f, 0f, (s == 0 ? 0.1f : -0.14f) * (1f - amp)) * strain;
            return f;
        }
    }
}
