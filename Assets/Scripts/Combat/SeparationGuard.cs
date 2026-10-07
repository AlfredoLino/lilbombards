using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Evita los "lanzamientos fantasma" al soltar algo: si al soltar un objeto (bomba, personaje...)
    /// esta tocando a alguien, se ignoran los choques entre ambos hasta que se separen de verdad.
    /// Si se reactivaran mientras se solapan, la fisica los despegaria de golpe y saldrian volando.
    /// </summary>
    public class SeparationGuard : MonoBehaviour
    {
        Collider[] mine;
        Pickupable pick;
        readonly List<Collider> others = new List<Collider>();
        float t;

        /// <summary>Empieza a vigilar: 'extra' (quien lo soltaba) y todo personaje que este tocando ahora.</summary>
        public static void Begin(Pickupable p, Collider extra)
        {
            if (p == null || p.Cols == null) return;
            var g = p.GetComponent<SeparationGuard>();
            if (g == null) g = p.gameObject.AddComponent<SeparationGuard>();
            g.mine = p.Cols;
            g.pick = p;
            g.t = 0f;
            if (extra != null) g.Track(extra);
            foreach (var c in LBCharacter.All)
            {
                if (c == null || c.Col == null || c.Grab == p) continue;
                if (g.Overlaps(c.Col)) g.Track(c.Col);
            }
        }

        void Track(Collider other)
        {
            if (other == null || others.Contains(other)) return;
            foreach (var m in mine)
                if (m != null) Physics.IgnoreCollision(m, other, true);
            others.Add(other);
        }

        bool Overlaps(Collider other)
        {
            var ob = other.bounds;
            ob.Expand(0.08f);
            foreach (var m in mine)
                if (m != null && m.bounds.Intersects(ob)) return true;
            return false;
        }

        void FixedUpdate()
        {
            // Mientras alguien lo tenga en la mano, no se toca nada.
            if (pick != null && (pick.Holder != null || pick.CoHolder != null)) return;
            t += Time.fixedDeltaTime;
            for (int i = others.Count - 1; i >= 0; i--)
            {
                var o = others[i];
                if (o == null)
                {
                    others.RemoveAt(i);
                    continue;
                }
                if (Overlaps(o)) continue;
                foreach (var m in mine)
                    if (m != null) Physics.IgnoreCollision(m, o, false);
                others.RemoveAt(i);
            }
            if (others.Count == 0 || t > 10f) Destroy(this);
        }
    }
}
