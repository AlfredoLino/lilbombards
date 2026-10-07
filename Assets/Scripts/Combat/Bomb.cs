using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Bombas al estilo BombSquad:
    ///  Normal   - mecha de ~2.6 s.
    ///  Hielo    - congela a quien alcanza (los congelados se rompen al recibir otro golpe).
    ///  Pegajosa - se pega a lo primero que toca tras lanzarla.
    ///  Impacto  - explota al chocar despues de lanzarla.
    ///  Mina     - se arma 1 s despues de tocar el suelo y explota al contacto.
    /// Cualquier bomba atrapada en una explosion detona en cadena.
    /// </summary>
    public class Bomb : MonoBehaviour
    {
        public static readonly List<Bomb> All = new List<Bomb>();

        public BombType Type;
        public int NetId;
        public bool IsProxy;
        public bool Armed => armed;
        public float FuseFraction => Lit && fuseTotal > 0f ? Mathf.Clamp01(fuse / fuseTotal) : 1f;

        public void MakeProxy(int id)
        {
            NetId = id;
            IsProxy = true;
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;
            if (Grab != null) Grab.CanBePicked = false;
        }

        /// <summary>Estado recibido del anfitrion (bomba espejo en un cliente).</summary>
        public void ApplyNetState(Vector3 pos, Quaternion rot, bool lit, float fuse01, bool isArmed)
        {
            NetSmooth.Set(gameObject, pos, rot);
            if (lit && !Lit) Light();
            if (Lit) fuse = fuse01 * fuseTotal;
            armed = isArmed;
        }
        public LBCharacter Owner;
        public Rigidbody Body;
        public Pickupable Grab;
        public bool Lit;
        public bool Exploded;

        float fuse, fuseTotal, age;
        bool thrown, armed, stuck, chainQueued;
        float thrownTime = -10f;
        float armTimer = -1f;

        Transform fuseT, sparkT;
        Material mat, lightMat;

        /// <summary>Para la IA: bomba que puede estallar pronto.</summary>
        public bool IsDangerous => !Exploded && (Lit || armed || (Type == BombType.Impact && thrown));
        public float FuseLeft => Lit ? fuse : 99f;

        public static int ActiveCount(LBCharacter owner)
        {
            int n = 0;
            foreach (var b in All)
                if (b.Owner == owner && !b.Exploded && b.Type != BombType.LandMine) n++;
            return n;
        }

        public static Bomb Create(BombType type, Vector3 pos, LBCharacter owner)
        {
            var go = new GameObject(type + "Bomb");
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.SetDamping(0.05f, 1.5f);

            Collider col;
            if (type == BombType.LandMine)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.56f, 0.16f, 0.56f);
                col = bc;
            }
            else
            {
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = 0.26f;
                col = sc;
            }
            Compat.SetPhysMat(col, type == BombType.Sticky ? 1f : 0.5f, type == BombType.Sticky ? 0f : 0.35f, false);

            var b = go.AddComponent<Bomb>();
            b.NetId = Net.NewId();
            b.Type = type;
            b.Owner = owner;
            b.Body = rb;
            b.BuildVisual();
            BlobShadow.Attach(go, type == BombType.LandMine ? 0.8f : 0.65f, 0.45f);

            b.Grab = go.AddComponent<Pickupable>();
            b.Grab.Init(rb);
            b.Grab.Released += b.OnReleased;
            return b;
        }

        void BuildVisual()
        {
            Transform t = transform;
            if (Type == BombType.LandMine)
            {
                mat = Gfx.Toon(new Color(0.42f, 0.44f, 0.48f), 0.7f, 0.7f);
                Gfx.PartAuto("Mine", t, "landmine", PrimitiveType.Cylinder, mat, Vector3.zero, new Vector3(0.56f, 0.08f, 0.56f));
                lightMat = Gfx.Toon(new Color(0.6f, 0.05f, 0.05f), 1f, 0.9f);
                Gfx.Part("Light", t, Gfx.Prim(PrimitiveType.Sphere), lightMat, new Vector3(0f, 0.08f, 0f), new Vector3(0.16f, 0.1f, 0.16f));
                return;
            }

            Color c;
            Texture tex = null;
            switch (Type)
            {
                case BombType.Ice: c = new Color(0.62f, 0.86f, 1f); break;
                case BombType.Sticky: c = new Color(0.32f, 0.86f, 0.26f); break;
                case BombType.Impact:
                    c = Color.white;
                    tex = Gfx.Stripe(new Color(0.14f, 0.14f, 0.16f), new Color(1f, 0.82f, 0.1f));
                    break;
                default: c = new Color(0.11f, 0.11f, 0.13f); break;
            }
            mat = Gfx.Toon(c, 1.0f, 0.85f, 0.35f, tex);
            if (Type == BombType.Ice) Gfx.SetEmission(mat, new Color(0.1f, 0.2f, 0.3f));
            if (Type == BombType.Sticky) Gfx.SetEmission(mat, new Color(0.05f, 0.18f, 0.03f));

            Gfx.PartAuto("Ball", t, "bomb", PrimitiveType.Sphere, mat, Vector3.zero, Vector3.one * 0.52f);
            var capMat = Gfx.Toon(new Color(0.55f, 0.55f, 0.58f), 0.9f, 0.8f);
            Gfx.Part("Cap", t, Gfx.Prim(PrimitiveType.Cylinder), capMat, new Vector3(0f, 0.25f, 0f), new Vector3(0.17f, 0.035f, 0.17f));
            var fuseMat = Gfx.Toon(new Color(0.75f, 0.62f, 0.4f), 0.2f, 0.3f);
            fuseT = Gfx.Part("Fuse", t, Gfx.Prim(PrimitiveType.Cylinder), fuseMat, new Vector3(0f, 0.36f, 0f), new Vector3(0.035f, 0.09f, 0.035f)).transform;
            sparkT = new GameObject("Spark").transform;
            sparkT.SetParent(t, false);
            sparkT.localPosition = new Vector3(0f, 0.45f, 0f);
        }

        public void Light()
        {
            if (Lit) return;
            Lit = true;
            fuseTotal = Type == BombType.Impact ? 6f : Tuning.FuseTime;
            fuse = fuseTotal;
            if (sparkT != null) FX.FuseSparks(sparkT);
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (lightMat != null) Destroy(lightMat);
        }

        void OnReleased(bool wasThrown)
        {
            thrown = true;
            thrownTime = Time.time;
        }

        void Update()
        {
            if (Exploded) return;
            float dt = Time.deltaTime;
            age += dt;

            if (Lit)
            {
                if (!IsProxy) fuse -= dt; // el espejo recibe la mecha del anfitrion
                float k = Mathf.Clamp01(fuse / fuseTotal);
                if (fuseT != null)
                {
                    fuseT.localScale = new Vector3(0.035f, 0.09f * k + 0.005f, 0.035f);
                    fuseT.localPosition = new Vector3(0f, 0.27f + 0.09f * k, 0f);
                    sparkT.localPosition = new Vector3(0f, 0.28f + 0.18f * k, 0f);
                }
                if (fuse < 0.7f)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(age * 45f);
                    Gfx.SetEmission(mat, new Color(1f, 0.6f, 0.4f) * (pulse * 0.6f));
                }
                if (fuse <= 0f && !IsProxy)
                {
                    Explode();
                    return;
                }
            }

            if (armTimer > 0f && !IsProxy)
            {
                armTimer -= dt;
                if (armTimer <= 0f)
                {
                    armed = true;
                    Grab.CanBePicked = false;
                    Sfx.PlayAt(Sfx.Beep, transform.position, 0.6f);
                }
            }
            if (lightMat != null)
            {
                bool on = armed ? Mathf.Repeat(age, 0.8f) < 0.15f : false;
                Gfx.SetEmission(lightMat, on ? new Color(1f, 0.1f, 0.05f) : (armed ? new Color(0.3f, 0f, 0f) : Color.black));
            }

            if (!IsProxy && transform.position.y < Tuning.KillY) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (IsProxy) return;
            if (Exploded || Grab == null || Grab.Holder != null) return;
            float rel = c.relativeVelocity.magnitude;
            var orb = c.rigidbody;
            var other = orb != null ? orb.GetComponent<LBCharacter>() : null;

            // Una bomba lanzada a la cara duele un poco.
            if (other != null && !other.Dead && rel > 3.5f && Time.time - Grab.LastThrownTime < 1.5f && other != Grab.LastThrower)
            {
                other.TakeHit(new HitInfo
                {
                    damage = rel * 12f,
                    impulse = Body.Vel() * 0.25f + Vector3.up * 1.5f,
                    source = Grab.LastThrower,
                    kind = HitKind.Impact,
                    knockout = 0.3f, // una bomba lanzada a la cara tambien tumba
                    stagger = 0.3f,
                    point = transform.position,
                });
                Sfx.PlayAt(Sfx.Punch, transform.position, 0.6f);
            }

            switch (Type)
            {
                case BombType.Impact:
                    if (thrown && Time.time - thrownTime > 0.05f && rel > 1.5f)
                    {
                        Explode();
                        return;
                    }
                    break;
                case BombType.Sticky:
                    if (thrown && !stuck) StickTo(c);
                    break;
                case BombType.LandMine:
                    if (!armed && armTimer < 0f && thrown) armTimer = Tuning.LandMineArmTime;
                    else if (armed && (other != null || rel > 3f))
                    {
                        // Quien pisa una mina pierde si o si un pie.
                        if (other != null && !other.Dead && other.Visual != null) other.Visual.ForceLoseFoot(transform.position);
                        Explode();
                        return;
                    }
                    break;
            }

            if (rel > 3f) Sfx.PlayAt(Sfx.Bounce, transform.position, Mathf.Clamp01(rel / 12f) * 0.5f);
        }

        void StickTo(Collision c)
        {
            stuck = true;
            Grab.CanBePicked = false;
            var orb = c.rigidbody;
            if (orb != null)
            {
                Body.mass = 0.25f;
                var j = gameObject.AddComponent<FixedJoint>();
                j.connectedBody = orb;
            }
            else
            {
                Body.isKinematic = true;
            }
            Sfx.PlayAt(Sfx.Sticky, transform.position, 0.8f);
        }

        /// <summary>Detonacion retardada (cadena de explosiones).</summary>
        public void TriggerChain(float delay)
        {
            if (Exploded || chainQueued) return;
            chainQueued = true;
            Invoke(nameof(Explode), delay);
        }

        public void Explode()
        {
            if (Exploded) return;
            Exploded = true;
            LBCharacter holder = Grab != null ? Grab.Holder : null;
            if (holder != null)
            {
                holder.DropHeld();
                // Explotar en la mano (sin lanzarla) arranca si o si las dos manos.
                if (!holder.Dead && holder.Visual != null) holder.Visual.LoseBothHands(transform.position);
            }
            float r = Tuning.BlastRadius * Tuning.RadiusMul(Type);
            float dmgMul = (Type == BombType.Impact || Type == BombType.LandMine) ? 0.85f : 1f;
            Blast.Explode(transform.position, Type, Owner, r, dmgMul);
            Destroy(gameObject);
        }
    }
}
