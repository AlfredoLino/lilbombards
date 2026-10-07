using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>Aisla los cambios de API entre Unity 2021/2022 y Unity 6.</summary>
    public static class Compat
    {
        public static Vector3 Vel(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVel(this Rigidbody rb, Vector3 v)
        {
            if (rb.isKinematic) return;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static void SetDamping(this Rigidbody rb, float linear, float angular)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = linear;
            rb.angularDamping = angular;
#else
            rb.drag = linear;
            rb.angularDrag = angular;
#endif
        }

#if UNITY_6000_0_OR_NEWER
        static readonly Dictionary<int, PhysicsMaterial> mats = new Dictionary<int, PhysicsMaterial>();
#else
        static readonly Dictionary<int, PhysicMaterial> mats = new Dictionary<int, PhysicMaterial>();
#endif

        /// <summary>Asigna un material fisico cacheado. minFriction=true usa combinacion Minimum.</summary>
        public static void SetPhysMat(Collider c, float friction, float bounce, bool minFriction)
        {
            int key = Mathf.RoundToInt(friction * 100f) * 1000 + Mathf.RoundToInt(bounce * 100f) * 2 + (minFriction ? 1 : 0);
            if (!mats.TryGetValue(key, out var m))
            {
#if UNITY_6000_0_OR_NEWER
                m = new PhysicsMaterial("lb_" + key);
                m.frictionCombine = minFriction ? PhysicsMaterialCombine.Minimum : PhysicsMaterialCombine.Average;
                m.bounceCombine = PhysicsMaterialCombine.Maximum;
#else
                m = new PhysicMaterial("lb_" + key);
                m.frictionCombine = minFriction ? PhysicMaterialCombine.Minimum : PhysicMaterialCombine.Average;
                m.bounceCombine = PhysicMaterialCombine.Maximum;
#endif
                m.dynamicFriction = friction;
                m.staticFriction = friction;
                m.bounciness = bounce;
                mats[key] = m;
            }
            c.sharedMaterial = m;
        }
    }
}
