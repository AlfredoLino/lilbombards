using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>Resolucion de explosiones: dano con caida por distancia, empuje, K.O., congelacion y cadenas.</summary>
    public static class Blast
    {
        static readonly Collider[] buf = new Collider[128];
        static readonly HashSet<Rigidbody> seen = new HashSet<Rigidbody>();

        public static void Explode(Vector3 pos, BombType type, LBCharacter owner, float radius, float dmgMul)
        {
            bool ice = type == BombType.Ice;
            FX.Explosion(pos, radius, ice);
            Sfx.PlayAt(ice ? Sfx.IceBlast : Sfx.Explosion, pos, Mathf.Clamp(radius / Tuning.BlastRadius, 0.6f, 1.2f));
            CameraRig.Shake(0.45f * radius / Tuning.BlastRadius);

            seen.Clear();
            int n = Physics.OverlapSphereNonAlloc(pos, radius + 0.4f, buf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var rb = buf[i].attachedRigidbody;
                if (rb == null || !seen.Add(rb)) continue;

                var ch = rb.GetComponent<LBCharacter>();
                if (ch != null)
                {
                    HitCharacter(ch, pos, type, owner, radius, dmgMul);
                    continue;
                }

                Vector3 dir = rb.worldCenterOfMass - pos;
                float dist = dir.magnitude;
                float f = Mathf.Clamp01(1f - dist / (radius + 0.4f));
                dir = dist > 0.01f ? dir / dist : Vector3.up;

                var bomb = rb.GetComponent<Bomb>();
                if (bomb != null)
                {
                    if (!bomb.Exploded) bomb.TriggerChain(Random.Range(0.06f, 0.16f));
                    if (!rb.isKinematic) rb.AddForce(dir * 6f * f + Vector3.up * 3f * f, ForceMode.VelocityChange);
                    continue;
                }

                var tnt = rb.GetComponent<TntBox>();
                if (tnt != null)
                {
                    tnt.TriggerChain(0.12f, owner);
                    continue;
                }

                if (!rb.isKinematic)
                    rb.AddForce(dir * 10f * f + Vector3.up * 5f * f, ForceMode.VelocityChange);
            }

            if (Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, out var hit, 1.6f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.attachedRigidbody == null)
            {
                // Marca en el suelo: mas grande cuanto mas cerca del suelo exploto.
                float h = Mathf.Clamp01(1f - (pos.y - hit.point.y) / 1.6f);
                FX.Scorch(hit.point, hit.normal, radius * Mathf.Lerp(0.55f, 1f, h), ice);
            }

            // Tambien tizna las barandas cercanas (paredes verticales).
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (Physics.Raycast(pos, dir, out var wh, radius * 0.7f, ~0, QueryTriggerInteraction.Ignore) &&
                    wh.collider.attachedRigidbody == null && Mathf.Abs(wh.normal.y) < 0.5f)
                {
                    float near = 1f - wh.distance / (radius * 0.7f);
                    FX.Scorch(wh.point, wh.normal, radius * Mathf.Lerp(0.35f, 0.75f, near), ice);
                }
            }
        }

        static void HitCharacter(LBCharacter ch, Vector3 pos, BombType type, LBCharacter owner, float radius, float dmgMul)
        {
            if (ch.Dead)
            {
                if (!ch.Body.isKinematic)
                    ch.Body.AddForce((ch.Center - pos).normalized * 8f + Vector3.up * 6f, ForceMode.VelocityChange);
                return;
            }
            Vector3 c = ch.Center;
            float d = Mathf.Max(0f, Vector3.Distance(pos, c) - 0.35f);
            float f = 1f - d / radius;
            if (f <= 0f) return;

            Vector3 dir = c - pos;
            if (dir.y < 0f) dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;

            var h = new HitInfo { kind = HitKind.Blast, source = owner, point = pos };
            if (type == BombType.Ice)
            {
                h.freeze = true;
                h.damage = 150f * f;
                h.impulse = dir * (5f * f) + Vector3.up * (3f * f);
            }
            else
            {
                // Desgaste de capas del cuerpo segun el angulo de cada zona (el escudo lo evita).
                if (ch.ShieldHp <= 0f && ch.Visual != null)
                {
                    // Semilla compartida: los clientes online rompen los mismos trozos.
                    int seed = Net.NextSeed();
                    var rs = Random.state;
                    Random.InitState(seed);
                    ch.Visual.ApplyBlastLayers(pos, dmgMul, radius);
                    Random.state = rs;
                    Net.EvBlastLayers(ch.NetId, pos, dmgMul, radius, seed);
                    ch.Visual.ApplyLimbDamage(pos, dmgMul, radius); // desmembramiento (solo bombas)
                }
                h.damage = Tuning.BlastDamage * dmgMul * Mathf.Pow(f, 0.8f);
                h.impulse = dir * (Tuning.BlastKnock * f + 3f) + Vector3.up * (Tuning.BlastUp * f + 2f);
                h.knockout = 0.8f + 1.6f * f;
            }
            ch.TakeHit(h);
        }
    }
}
