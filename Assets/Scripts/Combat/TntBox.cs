using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>Caja de TNT: estalla (radio x1.45) cuando la alcanza otra explosion y reaparece mas tarde.</summary>
    public class TntBox : MonoBehaviour
    {
        public static readonly List<TntBox> All = new List<TntBox>();

        public Vector3 Home;
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
        bool exploded, queued;
        LBCharacter credit;
        Material mat;

        public static TntBox Create(Vector3 pos)
        {
            var go = new GameObject("TNT");
            go.transform.position = pos;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.SetDamping(0.2f, 1f);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.9f, 0.9f, 0.9f);
            col.center = new Vector3(0f, 0.45f, 0f);
            Compat.SetPhysMat(col, 0.8f, 0.05f, false);

            var t = go.AddComponent<TntBox>();
            t.NetId = Net.NewId();
            t.Home = pos;
            BlobShadow.Attach(go, 1.3f, 0.5f, col);
            t.mat = Gfx.Toon(Color.white, 0.35f, 0.4f, 0.25f, Gfx.TntTexture());
            Gfx.PartAuto("Crate", go.transform, "tnt", PrimitiveType.Cube, t.mat, new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.9f));
            FX.SpawnFlash(pos + Vector3.up * 0.45f, new Color(1f, 0.4f, 0.2f));
            return t;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        void OnDestroy() { if (mat != null) Destroy(mat); }

        public void TriggerChain(float delay, LBCharacter owner)
        {
            if (exploded || queued) return;
            queued = true;
            credit = owner;
            Invoke(nameof(Explode), delay);
        }

        void Explode()
        {
            if (exploded) return;
            exploded = true;
            Vector3 p = transform.position + transform.up * 0.45f;
            Blast.Explode(p, BombType.Normal, credit, Tuning.BlastRadius * Tuning.TntRadiusMul, 1.25f);
            if (GameManager.I != null) GameManager.I.OnTntGone(Home);
            Destroy(gameObject);
        }

        void Update()
        {
            if (IsProxy) return;
            if (!exploded && transform.position.y < Tuning.KillY)
            {
                exploded = true;
                if (GameManager.I != null) GameManager.I.OnTntGone(Home);
                Destroy(gameObject);
            }
        }
    }
}
