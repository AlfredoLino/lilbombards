using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Desmembramiento: brazos y piernas divididos en dos partes (baja: antebrazo+mano / espinilla+pie;
    /// alta: brazo / muslo). Solo las bombas cortan, por el lado que miran a la explosion y con mas
    /// facilidad cuanto mas destrozadas esten sus capas. La parte cortada queda fisicamente en el campo
    /// tal cual estaba (capas rotas, hollin...), con munon de musculo y hueso en el corte.
    /// </summary>
    public partial class CharacterVisual
    {
        // 0 brazo izq, 1 brazo der, 2 pierna izq, 3 pierna der
        readonly float[] lowerHp = { 1f, 1f, 1f, 1f };
        readonly float[] upperHp = { 1f, 1f, 1f, 1f };
        bool gibbed;

        /// <summary>Dano de una explosion a las extremidades (solo bombas).</summary>
        public void ApplyLimbDamage(Vector3 blast, float strength, float radius)
        {
            if (ch == null || ch.Dead || hidden || gibbed) return;
            Vector3 chest = body.TransformPoint(new Vector3(0f, 0.8f, 0f));
            Vector3 hips = body.TransformPoint(new Vector3(0f, HipY, 0f));
            for (int limb = 0; limb < 4; limb++)
            {
                bool arm = limb < 2;
                int s = limb % 2;
                int loss = arm ? ch.ArmLoss[s] : ch.LegLoss[s];
                if (loss >= 2) continue;
                bool lower = loss == 0;
                Transform seg = arm ? (lower ? foreArm[s] : upperArm[s]) : (lower ? shin[s] : thigh[s]);
                Transform end = arm ? (lower ? hands[s] : null) : (lower ? feet[s] : null);
                Vector3 pos = end != null ? (seg.position + end.position) * 0.5f : seg.position;

                // Normal "hacia fuera" de la extremidad; las piernas reciben sobre todo desde abajo.
                Vector3 n = pos - (arm ? chest : hips);
                if (!arm)
                {
                    n.y = 0f;
                    n = (n.sqrMagnitude > 1e-6f ? n.normalized : transform.forward) + Vector3.down * 0.7f;
                }
                n = n.sqrMagnitude > 1e-6f ? n.normalized : transform.right * (s == 0 ? -1f : 1f);

                Vector3 to = blast - pos;
                float dist = to.magnitude;
                Vector3 dir = dist > 1e-4f ? to / dist : n;
                float facing = Vector3.Dot(n, dir);
                float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.3f, 0.6f, facing));
                float prox = Mathf.Clamp01(1f - dist / (radius + 0.4f));
                if (prox <= 0f || w <= 0.01f) continue;

                // Cuanto mas destrozadas esten sus capas, mas facil es arrancarla.
                float strip = damage != null ? damage.StrippedFraction(seg, end) : 0.5f;
                float dmg = strength * prox * w * Tuning.LimbDamagePerBlast * (0.35f + 1.65f * strip) * Random.Range(0.8f, 1.2f);
                if (lower)
                {
                    lowerHp[limb] -= dmg;
                    if (lowerHp[limb] <= 0f) Sever(arm, s, true, blast);
                }
                else
                {
                    upperHp[limb] -= dmg;
                    if (upperHp[limb] <= 0f) Sever(arm, s, false, blast);
                }
                if (gibbed) return;
            }
        }

        /// <summary>Secciones de la silueta del HUD.</summary>
        public enum Section { Head, Torso, UpperArmL, LowerArmL, UpperArmR, LowerArmR, ThighL, ShinL, ThighR, ShinR }
        public const int SectionCount = 10;

        /// <summary>
        /// Peligro de una seccion para el HUD: 0 sana, 1 a punto de perderse; -1 si ya se perdio.
        /// Extremidades: lo cerca que esta de cortarse (y lo destrozadas que estan sus capas).
        /// Cabeza y torso: cuanto se han destruido sus capas.
        /// </summary>
        public float SectionDanger(Section sec)
        {
            if (ch == null || damage == null || gibbed) return -1f;
            if (ch.IsProxy) return ch.NetDanger((int)sec);
            switch (sec)
            {
                case Section.Head: return damage.LayerDamage(head, neck);
                case Section.Torso: return damage.LayerDamage(body, shoulderPad[0], shoulderPad[1]);
            }
            int idx = (int)sec - 2;          // 0..7
            bool arm = idx < 4;
            int s = (idx % 4) / 2;           // 0 izq, 1 der
            bool lower = idx % 2 == 1;
            int limb = arm ? s : 2 + s;
            int loss = arm ? ch.ArmLoss[s] : ch.LegLoss[s];
            if (lower && loss >= 1) return -1f;
            if (!lower && loss >= 2) return -1f;
            Transform seg = arm ? (lower ? foreArm[s] : upperArm[s]) : (lower ? shin[s] : thigh[s]);
            Transform end = arm ? (lower ? hands[s] : null) : (lower ? feet[s] : null);
            float hp = lower ? lowerHp[limb] : upperHp[limb];
            float strip = damage.StrippedFraction(seg, end);
            return Mathf.Clamp01(Mathf.Max(1f - hp, strip * 0.5f));
        }

        /// <summary>Una bomba explota en la mano: se pierden las dos manos (las que queden).</summary>
        public void LoseBothHands(Vector3 blast)
        {
            if (ch == null || ch.Dead || gibbed) return;
            for (int s = 0; s < 2 && !gibbed; s++)
                if (ch.ArmLoss[s] == 0) Sever(true, s, true, blast);
        }

        /// <summary>Pisar una mina: se pierde si o si un pie (el mas cercano que quede).</summary>
        public void ForceLoseFoot(Vector3 minePos)
        {
            if (ch == null || ch.Dead || gibbed) return;
            int best = -1;
            float bd = float.MaxValue;
            for (int s = 0; s < 2; s++)
            {
                if (ch.LegLoss[s] != 0) continue;
                float d = (feet[s].position - minePos).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            if (best >= 0)
            {
                Sever(false, best, true, minePos);
                return;
            }
            for (int s = 0; s < 2; s++)
            {
                if (ch.LegLoss[s] >= 2) continue;
                float d = (thigh[s].position - minePos).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            if (best >= 0) Sever(false, best, false, minePos);
        }

        /// <summary>Corte recibido del anfitrion (en un cliente online).</summary>
        public void SeverNet(bool arm, int s, bool lower, Vector3 blast)
        {
            if (gibbed || ch == null) return;
            if (lower ? severedLower[(arm ? 0 : 2) + s] : severedUpper[(arm ? 0 : 2) + s]) return;
            Sever(arm, s, lower, blast);
        }

        readonly bool[] severedLower = new bool[4], severedUpper = new bool[4];

        void Sever(bool arm, int s, bool lower, Vector3 blast)
        {
            float sx = s == 0 ? -1f : 1f;
            if (lower) severedLower[(arm ? 0 : 2) + s] = true; else severedUpper[(arm ? 0 : 2) + s] = true;
            if (Net.IsHost && ch != null) Net.EvLimb(ch.NetId, arm, s, lower, blast);
            var parts = new List<Transform>();
            if (arm)
            {
                if (lower) { parts.Add(foreArm[s]); parts.Add(hands[s]); }
                else { parts.Add(upperArm[s]); parts.Add(elbowPad[s]); }
            }
            else
            {
                if (lower) { parts.Add(shin[s]); parts.Add(feet[s]); parts.Add(kneePad[s]); }
                else parts.Add(thigh[s]);
            }

            // Munon en lo que queda unido al cuerpo.
            if (arm && lower) SegStump(upperArm[s], true);
            else if (arm) Stump(shoulderPad[s], new Vector3(0f, -0.04f, 0f), 0.13f);
            else if (lower) SegStump(thigh[s], true);
            else Stump(body, new Vector3(sx * HipX, HipY - 0.03f, 0f), 0.14f);

            // Y en la parte que sale volando (en el lado del corte).
            SegStump(parts[0], false);

            BuildSevered(parts, blast);
            Sfx.PlayAt(Sfx.Sticky, parts[0].position, 0.9f);
            Sfx.PlayAt(Sfx.Punch, parts[0].position, 0.7f);
            FX.Poof(parts[0].position, new Color(0.7f, 0.15f, 0.12f));
            ch.OnLimbLost(arm, s, lower ? 1 : 2);
        }

        /// <summary>Munon al final (+1) o al principio (-1) de un segmento de extremidad.</summary>
        void SegStump(Transform seg, bool atEnd)
        {
            float e = atEnd ? 1f : -1f;
            RegisterStatic(Gfx.Part("StumpMuscle", seg, Gfx.Prim(PrimitiveType.Sphere), muscleMat, new Vector3(0f, 0.97f * e, 0f), new Vector3(0.95f, 0.14f, 0.95f)));
            RegisterStatic(Gfx.Part("StumpBone", seg, Gfx.Prim(PrimitiveType.Sphere), boneMat, new Vector3(0f, 1.02f * e, 0f), new Vector3(0.42f, 0.16f, 0.42f)));
        }

        void Stump(Transform parent, Vector3 localPos, float size)
        {
            RegisterStatic(Gfx.Part("StumpMuscle", parent, Gfx.Prim(PrimitiveType.Sphere), muscleMat, localPos, Vector3.one * size));
            RegisterStatic(Gfx.Part("StumpBone", parent, Gfx.Prim(PrimitiveType.Sphere), boneMat, localPos + Vector3.down * size * 0.15f, Vector3.one * size * 0.5f));
        }

        /// <summary>Pieza nueva que no se rompe (munones): necesita colores de vertice para el hollin.</summary>
        void RegisterStatic(GameObject go)
        {
            if (damage == null) return;
            var r = go.GetComponent<Renderer>();
            if (r != null) damage.AddPart(r, ContainerOf(r.transform), 3f, false, 1f, 4);
        }

        /// <summary>
        /// Separa las piezas del cuerpo y las convierte en un objeto fisico que se queda en el campo,
        /// con sus propios materiales y mallas (el personaje puede morir y desaparecer sin afectarlas).
        /// </summary>
        GameObject BuildSevered(List<Transform> pieces, Vector3 blast)
        {
            var root = new GameObject("Severed_" + pieces[0].name).transform;
            root.SetPositionAndRotation(pieces[0].position, pieces[0].rotation);
            foreach (var p in pieces)
                if (p != null) p.SetParent(root, true);

            var copies = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                if (!copies.TryGetValue(m, out var c))
                {
                    c = new Material(m);
                    copies[m] = c;
                }
                r.sharedMaterial = c;
            }
            var meshes = damage != null ? damage.Detach(root) : new List<Mesh>();

            // Collider ajustado a las piezas (en el espacio del objeto cortado).
            bool any = false;
            var b = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 lp = root.InverseTransformPoint(corner);
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
                    else b.Encapsulate(lp);
                }
            }
            var bc = root.gameObject.AddComponent<BoxCollider>();
            bc.center = b.center;
            bc.size = Vector3.Max(b.size * 0.85f, Vector3.one * 0.06f);
            if (ch != null && ch.Col != null) Physics.IgnoreCollision(bc, ch.Col, true);

            var rb = root.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.SetDamping(0.2f, 0.8f);
            Vector3 away = root.position - blast;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Random.onUnitSphere;
            rb.SetVel(away * Random.Range(4f, 7f) + Vector3.up * Random.Range(3f, 5.5f));
            rb.angularVelocity = Random.insideUnitSphere * 12f;

            var sp = root.gameObject.AddComponent<SeveredPart>();
            sp.Mats.AddRange(copies.Values);
            sp.Meshes.AddRange(meshes);
            BlobShadow.Attach(root.gameObject, 0.55f, 0.35f, bc);
            return root.gameObject;
        }

        /// <summary>Sin ninguna extremidad: el personaje se despedaza (cabeza y torso tambien quedan en el campo).</summary>
        public void Gib(Vector3 blast)
        {
            if (gibbed) return;
            gibbed = true;
            BuildSevered(new List<Transform> { head }, blast);
            BuildSevered(new List<Transform> { body, neck }, blast);
            FX.Poof(ch.Center, new Color(0.7f, 0.15f, 0.12f));
            FX.Poof(ch.Center, new Color(0.9f, 0.9f, 0.9f));
            Sfx.PlayAt(Sfx.Shatter, ch.Center, 1f);
            CameraRig.Shake(0.35f);
            Hide();
        }

        // ------------------------------------------------------------------ Locomocion sin extremidades

        /// <summary>Postura segun lo que falte: cojear, gatear o arrastrarse con las manos.</summary>
        void ApplyLocomotionPose(int mode, ref float bob, ref float lean, ref float side, float time, float amp)
        {
            int lostSide = ch.LegLoss[0] >= ch.LegLoss[1] ? 0 : 1;
            float sx = lostSide == 0 ? -1f : 1f;
            switch (mode)
            {
                case 1: // cojera: salta sobre la pierna buena y el cuerpo cae hacia el lado sin pie
                {
                    float hop = Mathf.Pow(Mathf.Abs(Mathf.Sin(walkPhase)), 0.6f);
                    bob = -0.05f + hop * 0.1f * amp + Mathf.Sin(time * 2.2f) * 0.006f;
                    side += -sx * (8f + 9f * amp * (1f - hop));
                    lean += 5f + 4f * amp;
                    break;
                }
                case 2: // a gatas
                    bob = -0.36f + Mathf.Abs(Mathf.Sin(walkPhase)) * 0.04f * amp;
                    lean = 55f;
                    side += Mathf.Sin(walkPhase) * 5f * amp;
                    break;
                default: // arrastrandose con las manos
                    bob = -0.5f + Mathf.Abs(Mathf.Sin(walkPhase * 0.5f)) * 0.05f * amp;
                    lean = 76f;
                    side += Mathf.Sin(walkPhase * 0.5f) * 4f * amp;
                    break;
            }
        }

        /// <summary>Cojera: el munon queda levantado y no pisa; la pierna buena da pasos mas cortos y apoyados.</summary>
        void LimpTargets(float amp, ref Vector3 foot0, ref Vector3 foot1)
        {
            for (int s = 0; s < 2; s++)
            {
                if (ch.LegLoss[s] < 1) continue;
                float sx = s == 0 ? -1f : 1f;
                Vector3 f = new Vector3(sx * 0.16f, 0.24f + Mathf.Sin(walkPhase) * 0.04f * amp, 0.06f + Mathf.Cos(walkPhase) * 0.05f * amp);
                if (s == 0) foot0 = f; else foot1 = f;
            }
        }

        /// <summary>Manos y pies al gatear/arrastrarse (en espacio local del personaje).</summary>
        void CrawlTargets(int mode, float amp, ref Vector3 hand0, ref Vector3 hand1, ref Vector3 foot0, ref Vector3 foot1)
        {
            float rate = mode == 2 ? 1f : 0.5f;
            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? -1f : 1f;
                float ph = walkPhase * rate + s * Mathf.PI;
                float swing = Mathf.Sin(ph) * amp;
                float lift = Mathf.Max(0f, Mathf.Cos(ph)) * 0.12f * amp;
                Vector3 h = mode == 2
                    ? new Vector3(sx * 0.3f, 0.07f + lift, 0.78f + swing * 0.22f)
                    : new Vector3(sx * 0.4f, 0.07f + lift, 0.95f + swing * 0.3f);
                Vector3 f = mode == 2
                    ? new Vector3(sx * 0.16f, 0.07f + lift * 0.4f, -0.3f - swing * 0.12f)
                    : new Vector3(sx * 0.14f, 0.06f, -0.35f);
                if (s == 0) { hand0 = h; foot0 = f; } else { hand1 = h; foot1 = f; }
            }
        }
    }

    /// <summary>Extremidad (o cabeza/torso) cortada que se queda en el campo toda la partida.</summary>
    public class SeveredPart : MonoBehaviour
    {
        public static readonly List<SeveredPart> All = new List<SeveredPart>();
        public readonly List<Material> Mats = new List<Material>();
        public readonly List<Mesh> Meshes = new List<Mesh>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Update()
        {
            if (transform.position.y < Tuning.KillY) Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var m in Mats) if (m != null) Destroy(m);
            foreach (var m in Meshes) if (m != null) Destroy(m);
        }

        public static void ClearAll()
        {
            foreach (var p in new List<SeveredPart>(All))
                if (p != null) Destroy(p.gameObject);
            All.Clear();
        }
    }
}
