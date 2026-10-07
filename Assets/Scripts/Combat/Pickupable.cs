using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>Algo que un personaje puede agarrar y lanzar (bombas, minas, otros personajes).</summary>
    public class Pickupable : MonoBehaviour
    {
        public static readonly List<Pickupable> All = new List<Pickupable>();

        public Rigidbody Body;
        public Collider[] Cols;
        public LBCharacter Holder;
        /// <summary>Segundo agarrador (solo para personajes): lo cargan entre dos.</summary>
        public LBCharacter CoHolder;
        public bool CanBePicked = true;
        public LBCharacter AsCharacter;
        public Bomb AsBomb;
        public LBCharacter LastThrower;
        public float LastThrownTime = -10f;

        public event System.Action<bool> Released; // true = lanzado, false = soltado

        public void Init(Rigidbody body)
        {
            Body = body;
            Cols = GetComponentsInChildren<Collider>();
            AsCharacter = GetComponent<LBCharacter>();
            AsBomb = GetComponent<Bomb>();
        }

        public void NotifyReleased(bool thrown)
        {
            Released?.Invoke(thrown);
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void OnDestroy()
        {
            if (Holder != null) Holder.OnHeldDestroyed(this);
            if (CoHolder != null) CoHolder.OnHeldDestroyed(this);
        }
    }
}
