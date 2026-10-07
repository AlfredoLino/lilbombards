using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>Caja de powerup: aparece con un "pop", se recoge al tocarla y caduca parpadeando.</summary>
    public class PowerupBox : MonoBehaviour
    {
        public static readonly List<PowerupBox> All = new List<PowerupBox>();

        // Pesos de aparicion (mismas proporciones que BombSquad).
        static readonly int[] weights = { 3, 3, 3, 3, 2, 3, 2, 1, 1 };

        public PowerupType Type;
        public int NetId;
        public bool IsProxy;

        public void MakeProxy(int id)
        {
            NetId = id;
            IsProxy = true;
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
            }
        }
        float age;
        bool taken;
        Transform vis;
        Renderer rend;
        Material mat;

        public static PowerupType RandomType()
        {
            int total = 0;
            foreach (var w in weights) total += w;
            int r = Random.Range(0, total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (r < weights[i]) return (PowerupType)i;
                r -= weights[i];
            }
            return PowerupType.TripleBombs;
        }

        public static PowerupBox Create(PowerupType type, Vector3 pos)
        {
            var go = new GameObject("Powerup_" + type);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.SetDamping(0.1f, 1f);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.62f, 0.62f, 0.62f);
            Compat.SetPhysMat(col, 0.6f, 0.15f, false);

            var p = go.AddComponent<PowerupBox>();
            p.NetId = Net.NewId();
            p.Type = type;
            p.mat = Gfx.Toon(Color.white, 0.6f, 0.7f, 0.3f, Gfx.PowerupIcon(type));
            Gfx.SetEmission(p.mat, Gfx.PowerupColor(type) * 0.55f); // brillo tipo cristal
            var v = Gfx.PartAuto("Box", go.transform, "powerup", PrimitiveType.Cube, p.mat, Vector3.zero, new Vector3(0.62f, 0.62f, 0.62f));
            p.vis = v.transform;
            p.rend = v.GetComponent<Renderer>();
            p.vis.localScale = Vector3.zero;
            BlobShadow.Attach(go, 0.95f, 0.45f);
            Sfx.PlayAt(Sfx.Pop, pos, 0.5f);
            return p;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        void OnDestroy() { if (mat != null) Destroy(mat); }

        void Update()
        {
            age += Time.deltaTime;
            float s = 0.62f;
            if (age < 0.3f)
            {
                float k = age / 0.3f;
                s *= 1f + 2.7f * Mathf.Pow(k - 1f, 3f) + 1.7f * Mathf.Pow(k - 1f, 2f); // ease-out-back
            }
            vis.localScale = new Vector3(s, s, s);

            if (age > Tuning.PowerupLife - 3f) rend.enabled = Mathf.Repeat(age, 0.2f) < 0.12f;
            if (!IsProxy && (age > Tuning.PowerupLife || transform.position.y < Tuning.KillY))
            {
                FX.Poof(transform.position, Gfx.PowerupColor(Type));
                Destroy(gameObject);
            }
        }

        void OnCollisionEnter(Collision c) { TryCollect(c.rigidbody); }
        void OnCollisionStay(Collision c) { TryCollect(c.rigidbody); }

        void TryCollect(Rigidbody rb)
        {
            if (IsProxy || taken || rb == null || age < 0.15f) return;
            var ch = rb.GetComponent<LBCharacter>();
            if (ch == null || ch.Dead) return;
            taken = true;
            ch.ApplyPowerup(Type);
            FX.Sparkle(transform.position, Gfx.PowerupColor(Type));
            Destroy(gameObject);
        }
    }
}
