using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    public enum PowerupType { TripleBombs, IceBombs, StickyBombs, ImpactBombs, LandMines, Gloves, Shield, Health, Curse }
    public enum BombType { Normal, Ice, Sticky, Impact, LandMine }
    public enum HitKind { Punch, Blast, Impact, Fall, Curse, Shatter }

    public struct HitInfo
    {
        public float damage;
        public Vector3 impulse;     // cambio de velocidad aplicado
        public Vector3 point;
        public LBCharacter source;
        public HitKind kind;
        public float knockout;      // segundos de K.O. (0 = ninguno)
        public float stagger;       // segundos sin control (golpes leves)
        public bool freeze;
    }

    /// <summary>
    /// Personaje jugable estilo BombSquad: cuerpo fisico (capsula) que corre, salta, golpea,
    /// lanza bombas, agarra cosas y cae en K.O. como un muneco de trapo.
    /// La entrada viene de un IInputProvider (teclado, mando o bot).
    /// </summary>
    public class LBCharacter : MonoBehaviour
    {
        public static readonly List<LBCharacter> All = new List<LBCharacter>();

        /// <summary>(victima, slot del asesino o null, causa)</summary>
        public static event System.Action<LBCharacter, PlayerSlot, HitKind> Died;

        public PlayerSlot Slot;
        public IInputProvider Input;
        public Rigidbody Body;
        public CapsuleCollider Col;
        public CharacterVisual Visual;
        public Pickupable Grab;

        /// <summary>Porcentaje de dano acumulado (0% al aparecer).</summary>
        public float Damage;
        public float ShieldHp;
        public bool Dead;
        public float KoTimer, StaggerTimer, FrozenTimer;
        public float CurseTimer = -1f;
        public int LandMines;
        float tripleTimer, bombTypeTimer, glovesTimer;
        BombType bombType = BombType.Normal;

        public Pickupable Holding;
        public LBCharacter HeldBy;
        int struggle;
        float heldTime;
        enum ChargeButton { None, Bomb, Pickup }
        ChargeButton chargeButton;
        float chargeTime;

        PlayerSlot lastAttacker;
        float lastAttackTime = -100f;

        public Vector3 Facing = Vector3.forward;
        public bool Grounded;
        float lastGroundTime = -10f;
        float jumpQueued;

        public float PunchTimer;
        public bool PunchRight;
        float punchCooldown;
        bool punchHitDone;

        public float LastHitTime = -10f;
        bool screamed;
        AudioSource screamSrc;
        AudioClip screamClip;

        /// <summary>Toca suelo (de pie o tumbado) tras un vuelo: para el "oof" de aterrizaje.</summary>
        bool OnGroundForLanding()
        {
            if (Body.isKinematic || HeldBy != null) return false;
            if (Grounded) return true;
            if (Body.Vel().y > 0.5f) return false;
            return Physics.Raycast(Center, Vector3.down, out var hit, 0.75f, ~0, QueryTriggerInteraction.Ignore) &&
                   hit.collider.attachedRigidbody == null;
        }
        /// <summary>Tono de voz propio de cada personaje (para el grito).</summary>
        public float voicePitch = 1f;

        // ------------------------------------------------------------------ Extremidades
        // 0 = entera, 1 = sin la parte baja (mano / pie), 2 = sin la extremidad entera.
        public readonly int[] ArmLoss = new int[2];
        public readonly int[] LegLoss = new int[2];
        float drawTimer;
        bool drawThenCharge;

        public bool BothArmsGone => ArmLoss[0] >= 2 && ArmLoss[1] >= 2;
        /// <summary>Para agarrar (o aferrarse a) jugadores hacen falta los dos brazos.</summary>
        public bool CanGrabPlayers => ArmLoss[0] < 2 && ArmLoss[1] < 2;
        /// <summary>Mano perdida = 1, brazo perdido = 2 (el doble), sumado entre los dos lados.</summary>
        int ArmPenalty => ArmLoss[0] + ArmLoss[1];
        public float ThrowMul => Mathf.Max(0.2f, 1f - Tuning.ThrowPenaltyPerUnit * ArmPenalty);
        public float DrawTime => Tuning.DrawTimePerUnit * ArmPenalty;
        public float ThrowDistanceFor(float charge) => ThrowDistance(charge) * ThrowMul;

        /// <summary>0 normal, 1 cojeando, 2 a gatas, 3 arrastrandose con las manos, 4 inmovil.</summary>
        public int MoveMode
        {
            get
            {
                int a = LegLoss[0], b = LegLoss[1];
                if (a >= 2 && b >= 2) return BothArmsGone ? 4 : 3;      // sin piernas: con las manos
                if (a >= 2 || b >= 2 || (a >= 1 && b >= 1)) return 2;   // sin una pierna o sin los dos pies: arrastrandose
                if (a >= 1 || b >= 1) return 1;                         // sin un pie: cojea
                return 0;
            }
        }

        public float LimbSpeedMul
        {
            get
            {
                switch (MoveMode)
                {
                    case 1: return Tuning.LimpMul;
                    case 2: return Tuning.CrawlMul;
                    case 3: return Tuning.DragMul;
                    case 4: return 0f;
                    default: return 1f;
                }
            }
        }

        public bool CanJump => MoveMode < 2;
        public bool CanRun => MoveMode == 0;

        static readonly string[] LimbNames = { "brazo izquierdo", "brazo derecho", "pierna izquierda", "pierna derecha" };

        /// <summary>Se ha cortado una parte de una extremidad (lo llama CharacterVisual).</summary>
        public void OnLimbLost(bool arm, int s, int level)
        {
            if (arm) ArmLoss[s] = Mathf.Max(ArmLoss[s], level);
            else LegLoss[s] = Mathf.Max(LegLoss[s], level);

            if (IsProxy)
            {
                // Espejo online: solo lo visual (la logica la lleva el anfitrion).
                AdjustColliderForLegs();
                if (ArmLoss[0] >= 2 && ArmLoss[1] >= 2 && LegLoss[0] >= 2 && LegLoss[1] >= 2 && !Dead)
                {
                    Dead = true;
                    Visual.Gib(Center);
                }
                return;
            }

            if (arm)
            {
                if (BothArmsGone) DropHeld();
                else if (!CanGrabPlayers)
                {
                    if (HoldingCharacter) DropHeld();
                    Clinching = false;
                }
                drawTimer = 0f;
            }
            else AdjustColliderForLegs();

            string what = level == 1
                ? (arm ? "¡Sin mano!" : "¡Sin pie!")
                : (arm ? "¡Sin brazo!" : "¡Sin pierna!");
            HUD.Popup(HeadPos + Vector3.up * 0.4f, what, new Color(1f, 0.45f, 0.35f), 32);
            if (Slot != null) HUD.Feed(Slot.Tag + " perdió " + (level == 1 ? (arm ? "una mano" : "un pie") : "el " + LimbNames[(arm ? 0 : 2) + s]));

            // Sin ninguna extremidad: muere despedazado.
            if (ArmLoss[0] >= 2 && ArmLoss[1] >= 2 && LegLoss[0] >= 2 && LegLoss[1] >= 2 && !Dead)
            {
                Visual.Gib(Center);
                Die(HitKind.Blast);
            }
        }
        public Sfx.Voice voice = Sfx.Voice.Neutral;
        public InputState Cur;
        InputState prev;
        bool ragdoll;

        // ------------------------------------------------------------------ Online
        /// <summary>Identificador de red (lo asigna el anfitrion).</summary>
        public int NetId;
        /// <summary>Personaje espejo en un cliente online: sin logica, solo muestra lo que envia el anfitrion.</summary>
        public bool IsProxy;
        /// <summary>Murio cayendo al vacio (para que el espejo siga pataleando).</summary>
        public bool DiedFalling;
        float netCharge = -1f;
        readonly float[] netDanger = new float[CharacterVisual.SectionCount];

        public float NetDanger(int section) => netDanger[section];

        /// <summary>A gatas o sin piernas el cuerpo queda mas bajo.</summary>
        void AdjustColliderForLegs()
        {
            int mode = MoveMode;
            if (mode >= 3) { Col.height = 0.66f; Col.center = new Vector3(0f, 0.34f, 0f); }
            else if (mode == 2) { Col.height = 0.86f; Col.center = new Vector3(0f, 0.44f, 0f); }
        }

        public void MakeProxy(int id)
        {
            NetId = id;
            IsProxy = true;
            Input = null;
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>Aplica el estado recibido del anfitrion a este personaje espejo.</summary>
        public void ApplyNetState(Vector3 pos, Quaternion rot, bool grounded, bool ko, bool dead, bool fallDeath,
            bool frozen, bool cursed, bool shield, bool gloves, bool punchRight, bool clinching, bool behind,
            float punch, float charge, float dmg, float stamina, float curse, float tug, byte loss, float[] danger)
        {
            NetSmooth.Set(gameObject, pos, rot);
            Grounded = grounded;
            if (ko != ragdoll && !Dead)
            {
                ragdoll = ko;
                Visual.SetFloppy(ko);
            }
            if (dead && !Dead)
            {
                Dead = true;
                DiedFalling = fallDeath;
                if (fallDeath) Visual.SetFallingDeath();
                else Visual.SetFloppy(true);
            }
            if (frozen != IsFrozen)
            {
                FrozenTimer = frozen ? 999f : 0f;
                Visual.SetFrozen(frozen);
            }
            if (cursed != Cursed) Visual.SetCursed(cursed);
            CurseTimer = cursed ? curse : -1f;
            if (shield != (ShieldHp > 0f))
            {
                ShieldHp = shield ? 1f : 0f;
                Visual.SetShield(shield);
            }
            if (gloves != HasGloves)
            {
                glovesTimer = gloves ? 999f : 0f;
                Visual.SetGloves(gloves);
            }
            PunchRight = punchRight;
            PunchTimer = punch;
            Clinching = clinching;
            GrabbedFromBehind = behind;
            netCharge = charge;
            Damage = dmg;
            Stamina = stamina;
            StruggleTug = tug;
            ArmLoss[0] = Mathf.Max(ArmLoss[0], loss & 3);
            ArmLoss[1] = Mathf.Max(ArmLoss[1], (loss >> 2) & 3);
            LegLoss[0] = Mathf.Max(LegLoss[0], (loss >> 4) & 3);
            LegLoss[1] = Mathf.Max(LegLoss[1], (loss >> 6) & 3);
            for (int i = 0; i < netDanger.Length && i < danger.Length; i++) netDanger[i] = danger[i];
        }

        public void ApplyNetHolding(Pickupable holding, LBCharacter heldBy)
        {
            Holding = holding;
            HeldBy = heldBy;
        }

        static readonly RaycastHit[] castBuf = new RaycastHit[16];
        static readonly Collider[] overlapBuf = new Collider[32];

        public int BombCount => tripleTimer > 0f ? 3 : 1;
        public bool HasGloves => glovesTimer > 0f;
        public bool IsFrozen => FrozenTimer > 0f;
        public bool IsKO => KoTimer > 0f || Dead;
        public bool Cursed => CurseTimer >= 0f;
        public BombType CurrentBombType => bombTypeTimer > 0f ? bombType : BombType.Normal;
        public Vector3 Center => Col != null ? Col.bounds.center : transform.position + Vector3.up * Tuning.CenterHeight;
        public Vector3 HeadPos => Visual != null ? Visual.HeadPosition : Center + Vector3.up * 0.5f;
        public bool CanAct => !Dead && !IsKO && !IsFrozen && HeldBy == null && recoverTimer <= 0.1f && GameManager.AllowControl;

        /// <summary>Esta haciendo el brinquito para levantarse tras un K.O.</summary>
        public bool Recovering => recoverTimer > 0f;
        float recoverTimer;

        // ------------------------------------------------------------------ Creacion

        public static LBCharacter Spawn(PlayerSlot slot, Vector3 pos, float yaw)
        {
            var go = new GameObject("Char_" + slot.Name);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Tuning.CharMass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.freezeRotation = true;
            rb.SetDamping(0f, 0.8f);

            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, Tuning.CenterHeight, 0f);
            col.height = Tuning.CapsuleHeight;
            col.radius = Tuning.CapsuleRadius;
            Compat.SetPhysMat(col, 0f, 0f, true);

            var ch = go.AddComponent<LBCharacter>();
            ch.Slot = slot;
            ch.Input = slot.Input;
            ch.Body = rb;
            ch.Col = col;
            ch.Facing = go.transform.forward;
            ch.NetId = Net.NewId();
            // Voz para el grito: neutra, aguda o grave (fija por jugador) con un pequeno ajuste de tono.
            ch.voice = (Sfx.Voice)(Mathf.Abs(slot.Index * 7 + 3) % 3);
            ch.voicePitch = Random.Range(0.94f, 1.08f);

            ch.Grab = go.AddComponent<Pickupable>();
            ch.Grab.Init(rb);

            ch.Visual = go.AddComponent<CharacterVisual>();
            ch.Visual.Build(ch, slot.Color, slot.Highlight);
            BlobShadow.Attach(go, 1.05f, 0.55f, col);
            if (slot.Input is KeyboardInput kb && kb.Scheme == 0) AimMarker.Attach(ch);

            slot.Character = ch;
            if (slot.Input is BotBrain bot) bot.Attach(ch);

            FX.SpawnFlash(pos + Vector3.up * 0.7f, slot.Color);
            Sfx.PlayAt(Sfx.Spawn, pos, 0.5f);
            return ch;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        static bool Pressed(bool now, bool before) => now && !before;

        // ------------------------------------------------------------------ Bucle

        void Update()
        {
            if (Dead || IsProxy) return;
            float dt = Time.deltaTime;

            Cur = Input != null ? Input.Read() : default;
            if (!GameManager.AllowControl) Cur = default;

            TickTimers(dt);
            if (Dead) return;

            if (HeldBy != null)
            {
                UpdateBeingHeld(dt);
            }
            else if (CanAct)
            {
                HandleActions();
            }

            if (PunchTimer > 0f)
            {
                PunchTimer -= dt;
                if (!punchHitDone && PunchTimer <= Tuning.PunchDuration - Tuning.PunchHitDelay)
                {
                    punchHitDone = true;
                    DoPunchHit();
                }
            }
            punchCooldown -= dt;

            // Sacando la bomba con una sola mano / con el munon.
            if (drawTimer > 0f)
            {
                drawTimer -= dt;
                if (drawTimer <= 0f && CanAct && Holding == null && !BothArmsGone)
                {
                    SpawnBombInHand();
                    if (Holding != null && drawThenCharge && Cur.bomb) BeginCharge(ChargeButton.Bomb);
                }
            }

            // Grito: al salir disparado muy alto o al caer al vacio (una vez por vuelo).
            bool flyingHigh = transform.position.y > Tuning.ScreamHighY && HeldBy == null;
            bool fallingOff = transform.position.y < Tuning.ScreamY && !Body.isKinematic && Body.Vel().y < -1f;
            if (!screamed && (flyingHigh || fallingOff))
            {
                screamed = true;
                screamClip = Sfx.GetScream(voice);
                screamSrc = Sfx.PlayVoice(screamClip, transform.position, 0.85f, voicePitch);
            }
            // Si tras el grito vuelve a caer sobre el terreno: se corta el grito y suena "¡oof!".
            else if (screamed && transform.position.y > Tuning.ScreamY && OnGroundForLanding())
            {
                screamed = false;
                if (screamSrc != null) StartCoroutine(Sfx.FadeOut(screamSrc, screamClip, 0.08f));
                screamSrc = null;
                Sfx.PlayVoice(Sfx.GetOof(voice), transform.position, 0.9f, voicePitch);
                FX.Poof(transform.position + Vector3.up * 0.2f, new Color(0.8f, 0.75f, 0.65f));
                CameraRig.Shake(0.12f);
            }
            if (transform.position.y < Tuning.KillY) Die(HitKind.Fall);
            prev = Cur;
        }

        void TickTimers(float dt)
        {
            RegenStamina(dt);
            if (StruggleTug > 0f) StruggleTug = Mathf.Max(0f, StruggleTug - dt * 3f);
            if (tripleTimer > 0f) tripleTimer -= dt;
            if (bombTypeTimer > 0f) bombTypeTimer -= dt;
            if (glovesTimer > 0f)
            {
                glovesTimer -= dt;
                if (glovesTimer <= 0f) Visual.SetGloves(false);
            }
            if (StaggerTimer > 0f) StaggerTimer -= dt;
            if (FrozenTimer > 0f)
            {
                FrozenTimer -= dt;
                if (FrozenTimer <= 0f) Visual.SetFrozen(false);
            }
            if (KoTimer > 0f)
            {
                KoTimer -= dt;
                if (KoTimer <= 0f) TryRecover();
            }
            if (CurseTimer >= 0f)
            {
                CurseTimer -= dt;
                if (CurseTimer < 0f)
                {
                    CurseTimer = -1f;
                    Visual.SetCursed(false);
                    // La maldicion estalla sobre ti: mucho % y un gran empujon.
                    Blast.Explode(Center, BombType.Normal, this, Tuning.BlastRadius, 1.6f);
                }
            }
        }

        void HandleActions()
        {
            // Carga del lanzamiento: mientras se mantiene el boton la fuerza sube; al soltarlo se lanza.
            if (Holding != null && chargeButton != ChargeButton.None)
            {
                chargeTime += Time.deltaTime;
                bool held = chargeButton == ChargeButton.Bomb ? Cur.bomb : Cur.pickup;
                if (!held)
                {
                    ThrowHeld(ThrowCharge);
                    return;
                }
            }

            // Sin ningun brazo: no puede golpear, lanzar ni agarrar.
            bool armless = BothArmsGone;

            if (!armless && Pressed(Cur.punch, prev.punch))
            {
                if (Holding != null) ThrowHeld(chargeButton != ChargeButton.None ? ThrowCharge : 0.5f);
                else if (punchCooldown <= 0f) StartPunch();
            }

            if (!armless && Pressed(Cur.bomb, prev.bomb) && chargeButton == ChargeButton.None && drawTimer <= 0f)
            {
                if (Holding == null)
                {
                    // Modo raton: el 1er clic solo saca la bomba (la mecha ya corre).
                    // Mando/teclado: sacar y empezar a cargar en la misma pulsacion.
                    // Sin mano/brazo se tarda mas en sacarla.
                    if (DrawTime > 0f)
                    {
                        drawTimer = DrawTime;
                        drawThenCharge = !Cur.clickBomb;
                    }
                    else
                    {
                        SpawnBombInHand();
                        if (Holding != null && !Cur.clickBomb) BeginCharge(ChargeButton.Bomb);
                    }
                }
                else BeginCharge(ChargeButton.Bomb);
            }

            if (!armless && Pressed(Cur.pickup, prev.pickup) && chargeButton == ChargeButton.None)
            {
                if (Holding != null) BeginCharge(ChargeButton.Pickup);
                else TryPickup();
            }

            if (CanJump && Pressed(Cur.jump, prev.jump)) jumpQueued = 0.15f;
        }

        /// <summary>Direccion horizontal hacia el puntero del raton (si este jugador apunta con raton).</summary>
        public bool AimDir(out Vector3 dir)
        {
            dir = Facing;
            if (!Cur.hasAim) return false;
            Vector3 d = Cur.aimPoint - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.09f) return false;
            dir = d.normalized;
            return true;
        }

        void BeginCharge(ChargeButton b)
        {
            chargeButton = b;
            chargeTime = 0f;
        }

        /// <summary>0..1 mientras se carga un lanzamiento; -1 si no.</summary>
        public float ThrowCharge => IsProxy ? netCharge : chargeButton != ChargeButton.None && Holding != null
            ? Mathf.Min(Mathf.Clamp01(chargeTime / Tuning.ThrowChargeTime), MaxThrowCharge)
            : -1f;

        /// <summary>
        /// Carga maxima que permite la estamina actual: con 50% o mas se llega al maximo;
        /// con menos, la distancia maxima baja en proporcion (25% de estamina = mitad de distancia).
        /// </summary>
        public float MaxThrowCharge
        {
            get
            {
                float frac = Mathf.Clamp01(Stamina / Tuning.ThrowCostMax);
                if (frac >= 0.999f) return 1f;
                return ChargeForDistance(ThrowDistance(1f) * frac);
            }
        }

        // ------------------------------------------------------------------ Estamina

        /// <summary>Estamina 0..100.</summary>
        public float Stamina = Tuning.StaminaMax;
        float staminaDelay;
        bool sprinting;

        public bool HoldingCharacter => Holding != null && Holding.AsCharacter != null;

        /// <summary>Gasta estamina; si llega a 0 el personaje cae de cansancio.</summary>
        void SpendStamina(float amount)
        {
            if (Dead || amount <= 0f) return;
            Stamina -= amount;
            staminaDelay = Tuning.StaminaRegenDelay;
            if (Stamina <= 0f)
            {
                Stamina = 0f;
                Exhaust();
            }
        }

        void Exhaust()
        {
            if (Dead) return;
            DropHeld();
            float t = Mathf.Lerp(Tuning.ExhaustMin, Tuning.ExhaustMax, Mathf.Clamp01(Damage / Tuning.ExhaustFullPercent));
            KnockOut(t);
            HUD.Popup(HeadPos + Vector3.up * 0.4f, "¡Agotado!", new Color(0.75f, 0.85f, 1f), 34);
            Sfx.PlayAt(Sfx.Pop, transform.position, 0.6f);
        }

        void RegenStamina(float dt)
        {
            if (sprinting || HoldingCharacter) return; // agarrar a alguien impide recuperar
            if (staminaDelay > 0f)
            {
                staminaDelay -= dt;
                return;
            }
            Stamina = Mathf.Min(Tuning.StaminaMax, Stamina + Tuning.StaminaRegenPerSec * dt);
        }

        /// <summary>Velocidad de salida (horizontal, vertical) para una carga dada.</summary>
        public static Vector2 ThrowVelocity(float charge)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(charge));
            return new Vector2(Mathf.Lerp(Tuning.ThrowMinSpeed, Tuning.ThrowMaxSpeed, k),
                               Mathf.Lerp(Tuning.ThrowUpMin, Tuning.ThrowUpMax, k));
        }

        /// <summary>Distancia horizontal aproximada que recorre algo lanzado con esa carga (estando quieto).</summary>
        public static float ThrowDistance(float charge)
        {
            Vector2 v = ThrowVelocity(charge);
            float g = -Tuning.Gravity.y;
            float t = (v.y + Mathf.Sqrt(v.y * v.y + 2f * g * Tuning.ThrowHeight)) / g;
            return v.x * t;
        }

        /// <summary>Carga necesaria para alcanzar una distancia (para la IA).</summary>
        public static float ChargeForDistance(float dist)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (ThrowDistance(mid) < dist) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        void FixedUpdate()
        {
            if (Dead || IsProxy) return;
            float dt = Time.fixedDeltaTime;
            jumpQueued -= dt;
            sprinting = false;
            GroundCheck();
            if (Holding != null) CarryHeld();
            if (HeldBy != null || Body.isKinematic) return;
            if (recoverTimer > 0f)
            {
                recoverTimer -= dt;
                RecoverStep(dt);
            }

            Vector3 v = Body.Vel();
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            bool control = CanAct && StaggerTimer <= 0f;

            if (control)
            {
                Vector3 move = new Vector3(Cur.move.x, 0f, Cur.move.y);
                if (move.sqrMagnitude > 1f) move.Normalize();
                bool moving = move.sqrMagnitude > 0.04f;
                // Correr (solo con el boton de correr) gasta estamina mientras te mueves.
                // Cargando a alguien no se puede correr; si lo cargas solo vas a la mitad de velocidad.
                bool holdingChar = HoldingCharacter;
                bool run = Cur.run && moving && Stamina > 0f && !holdingChar && CanRun;
                sprinting = run;
                float speed = run ? Tuning.RunSpeed : Tuning.WalkSpeed;
                if (holdingChar && Holding.CoHolder == null) speed *= Tuning.HoldSpeedMul;
                speed *= LimbSpeedMul; // cojeando, a gatas o arrastrandose
                // A gatas o sin piernas no se puede mover mientras lanza o sostiene algo.
                if (MoveMode >= 2 && Holding != null) move = Vector3.zero;
                if (run) SpendStamina(Tuning.RunCostPerSec * dt);
                if (HoldingCharacter && moving) SpendStamina(Tuning.HoldMoveCostPerSec * dt);
                if (Dead || ragdoll) return; // se agoto justo ahora

                Vector3 desired = move * speed;
                float accel = Grounded ? Tuning.GroundAccel : Tuning.AirAccel;
                Vector3 dv = Vector3.ClampMagnitude(desired - horiz, accel * dt);
                Body.AddForce(dv, ForceMode.VelocityChange);

                if (move.sqrMagnitude > 0.01f) Facing = move.normalized;
                // Con raton: mientras tiene algo en la mano mira hacia el puntero.
                if (Holding != null && AimDir(out Vector3 aim)) Facing = aim;
                Quaternion target = Quaternion.LookRotation(Facing);
                Body.MoveRotation(Quaternion.Slerp(Body.rotation, target, 1f - Mathf.Exp(-Tuning.TurnRate * dt)));

                if (jumpQueued > 0f && CanJump && (Grounded || Time.time - lastGroundTime < 0.1f))
                {
                    jumpQueued = 0f;
                    v = Body.Vel();
                    v.y = Tuning.JumpSpeed;
                    Body.SetVel(v);
                    Grounded = false;
                    lastGroundTime = -10f;
                    Sfx.PlayAt(Sfx.Jump, transform.position, 0.45f);
                    SpendStamina(Tuning.JumpCost);
                }
            }
            else if (!IsKO && Grounded)
            {
                // Congelado o aturdido: se desliza y frena poco a poco.
                float k = IsFrozen ? 1.5f : 6f;
                Body.AddForce(-horiz * Mathf.Clamp01(k * dt), ForceMode.VelocityChange);
            }
        }

        void GroundCheck()
        {
            Grounded = false;
            if (Body.isKinematic || ragdoll) return;
            if (Body.Vel().y > 1.5f) return;
            Vector3 origin = transform.position + Vector3.up * 0.45f;
            int n = Physics.SphereCastNonAlloc(origin, 0.26f, Vector3.down, castBuf, 0.32f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = castBuf[i];
                if (h.collider == Col) continue;
                if (Holding != null && h.rigidbody != null && h.rigidbody == Holding.Body) continue;
                if (h.distance <= 0f)
                {
                    if (h.collider.attachedRigidbody != null) continue;
                }
                else if (h.normal.y < 0.5f) continue;
                Grounded = true;
                lastGroundTime = Time.time;
                break;
            }
        }

        bool GroundBelow(Vector3 from, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.5f, Vector3.down, castBuf, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var h = castBuf[i];
                if (h.collider == Col || h.collider.attachedRigidbody != null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (best == float.MinValue) return false;
            y = best;
            return true;
        }

        // ------------------------------------------------------------------ Golpes

        void StartPunch()
        {
            PunchTimer = Tuning.PunchDuration;
            punchCooldown = Tuning.PunchCooldown;
            punchHitDone = false;
            PunchRight = !PunchRight;
            if (PunchRight && ArmLoss[1] >= 2) PunchRight = false;
            if (!PunchRight && ArmLoss[0] >= 2) PunchRight = true;
            Sfx.PlayAt(Sfx.Swish, transform.position, 0.35f);
            SpendStamina(Tuning.PunchCost);
        }

        void DoPunchHit()
        {
            Vector3 v = Body.isKinematic ? Vector3.zero : Body.Vel();
            float spd = new Vector3(v.x, 0f, v.z).magnitude;
            Vector3 p = transform.position + Vector3.up * 0.85f + Facing * 0.6f;
            float dmg = (Tuning.PunchBase + spd * Tuning.PunchSpeedBonus) * (HasGloves ? Tuning.GlovesMul : 1f);

            int n = Physics.OverlapSphereNonAlloc(p, 0.55f, overlapBuf, ~0, QueryTriggerInteraction.Collide);
            bool hit = false;
            for (int i = 0; i < n; i++)
            {
                var c = overlapBuf[i];
                if (c == Col) continue;
                var rb = c.attachedRigidbody;
                if (rb == null) continue;
                if (Holding != null && rb == Holding.Body) continue;

                var other = rb.GetComponent<LBCharacter>();
                if (other != null)
                {
                    if (other == this) continue;
                    if (other.Dead)
                    {
                        if (!rb.isKinematic) rb.AddForce(Facing * 5f + Vector3.up * 3f, ForceMode.VelocityChange);
                        hit = true;
                        continue;
                    }
                    Vector3 dir = other.Center - Center;
                    dir.y = 0f;
                    dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Facing;
                    float knock = (Tuning.PunchKnock + spd * 0.6f) * (HasGloves ? 1.6f : 1f);
                    other.TakeHit(new HitInfo
                    {
                        damage = dmg,
                        impulse = dir * knock + Vector3.up * (2.5f + (HasGloves ? 2f : 0f)),
                        point = p,
                        source = this,
                        kind = HitKind.Punch,
                        knockout = dmg >= Tuning.PunchKOThreshold ? 0.5f + dmg / 900f : 0f,
                        stagger = 0.25f,
                    });
                    hit = true;
                }
                else if (!rb.isKinematic)
                {
                    rb.AddForce(Facing * 7f + Vector3.up * 2.5f, ForceMode.VelocityChange);
                    hit = true;
                }
            }

            if (hit)
            {
                Sfx.PlayAt(dmg >= Tuning.PunchKOThreshold ? Sfx.PunchStrong : Sfx.Punch, p);
                FX.PunchFlash(p, dmg / 400f);
                CameraRig.Shake(Mathf.Min(0.3f, dmg / 1800f));
            }
        }

        // ------------------------------------------------------------------ Bombas / agarrar

        Vector3 HoldPoint(Pickupable p)
        {
            Vector3 b = transform.position;
            if (p.AsCharacter != null) return b + Vector3.up * 0.35f + Facing * 0.8f;
            // Al cargar, el brazo lleva la bomba hacia atras y arriba (anticipacion del lanzamiento).
            float c = Mathf.Max(0f, ThrowCharge);
            return b + Vector3.up * (1.0f + 0.2f * c) + Facing * (0.42f - 0.6f * c);
        }

        void SpawnBombInHand()
        {
            Bomb b;
            if (LandMines > 0)
            {
                LandMines--;
                b = Bomb.Create(BombType.LandMine, transform.position + Vector3.up * 1.0f + Facing * 0.42f, this);
            }
            else
            {
                if (Bomb.ActiveCount(this) >= BombCount) return;
                b = Bomb.Create(CurrentBombType, transform.position + Vector3.up * 1.0f + Facing * 0.42f, this);
                b.Light();
            }
            Pick(b.Grab);
        }

        void TryPickup()
        {
            Vector3 gp = transform.position + Vector3.up * 0.6f + Facing * 0.55f;
            Pickupable best = null;
            float bestD = 0.95f;
            foreach (var p in Pickupable.All)
            {
                if (p == Grab || !p.CanBePicked || p.Body == null) continue;
                // Ya agarrado por otro: solo se puede ayudar a cargar a un personaje (segundo agarrador).
                if (p.Holder != null && (p.AsCharacter == null || p.CoHolder != null || p.Holder == this)) continue;
                if (p.AsCharacter != null && p.AsCharacter == HeldBy) continue;
                if (p.AsCharacter != null && !CanGrabPlayers) continue; // sin un brazo no puede agarrar jugadores
                Vector3 c = p.AsCharacter != null ? p.AsCharacter.Center : p.transform.position;
                float d = Vector3.Distance(gp, c) - (p.AsCharacter != null ? 0.35f : 0f);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            if (best != null)
            {
                Pick(best);
                Sfx.PlayAt(Sfx.Pickup, transform.position, 0.6f);
            }
        }

        void Pick(Pickupable p)
        {
            if (p.Holder != null && p.AsCharacter != null)
            {
                // Segundo agarrador: ayuda a cargarlo (ya esta cinematico y sujeto por el primero).
                Holding = p;
                p.CoHolder = this;
                foreach (var c in p.Cols)
                    if (c != null) Physics.IgnoreCollision(c, Col, true);
                p.AsCharacter.heldTime = 0f;
                HUD.Popup(HeadPos + Vector3.up * 0.4f, "¡Entre dos!", new Color(1f, 0.9f, 0.4f), 28);
                return;
            }
            Holding = p;
            p.Holder = this;
            foreach (var c in p.Cols)
                if (c != null) Physics.IgnoreCollision(c, Col, true);
            p.Body.isKinematic = true;
            SetHeldGhost(p, true); // en la mano no empuja a nadie
            Vector3 hp = HoldPoint(p);
            p.transform.position = hp;
            p.Body.position = hp;
            if (p.AsCharacter != null) p.AsCharacter.OnGrabbed(this);
        }

        void CarryHeld()
        {
            var p = Holding;
            if (p == null || p.Body == null)
            {
                Holding = null;
                return;
            }
            if (p.Holder != this) return; // el segundo agarrador no mueve: lo hace el principal

            Vector3 target = HoldPoint(p);
            var co = p.CoHolder;
            if (co != null)
            {
                // Entre dos: el agarrado queda entre ambas manos. Si se separan demasiado, el segundo se suelta.
                if (Vector3.Distance(transform.position, co.transform.position) > Tuning.CoHoldMaxDist)
                    co.ReleaseHeld(false);
                else
                    target = (target + co.HoldPoint(p)) * 0.5f;
            }
            p.Body.MovePosition(target);
            if (p.AsCharacter != null)
            {
                // De frente: mira al agarrador. Por la espalda: mira hacia el mismo lado.
                Vector3 look = p.AsCharacter.GrabbedFromBehind ? Facing : -Facing;
                p.Body.MoveRotation(Quaternion.LookRotation(look));
            }
        }

        /// <summary>Lanza lo que tiene en la mano. charge (0..1) decide la distancia; el stick, la direccion.</summary>
        void ThrowHeld(float charge)
        {
            var p = Holding;
            if (p == null)
            {
                Holding = null;
                return;
            }
            if (p.AsCharacter != null && p.AsCharacter.Clinching)
            {
                // Se aferro a nosotros: no hay forma de lanzarlo.
                chargeButton = ChargeButton.None;
                chargeTime = 0f;
                HUD.Popup(p.AsCharacter.HeadPos + Vector3.up * 0.4f, "¡No te suelta!", new Color(1f, 0.6f, 0.4f), 28);
                Sfx.PlayAt(Sfx.Bounce, transform.position, 0.5f);
                return;
            }
            Vector3 move = new Vector3(Cur.move.x, 0f, Cur.move.y);
            Vector3 dir = move.magnitude > 0.2f ? move.normalized : Facing;
            if (AimDir(out Vector3 aim)) dir = aim;
            Vector3 v = Body.isKinematic ? Vector3.zero : Body.Vel();
            Vector3 horiz = new Vector3(v.x, 0f, v.z);

            // La estamina limita la distancia maxima y el lanzamiento la gasta en proporcion a la distancia.
            charge = Mathf.Min(Mathf.Clamp01(charge), MaxThrowCharge);
            float throwCost = Tuning.ThrowCostMax * ThrowDistance(charge) / ThrowDistance(1f);
            Vector2 tv = ThrowVelocity(charge);
            // Sin mano o sin brazo se lanza mas cerca (la distancia escala con ThrowMul).
            float armMul = Mathf.Sqrt(ThrowMul);
            float fwd = tv.x * armMul, up = tv.y * armMul;
            if (p.AsCharacter != null)
            {
                fwd *= Tuning.ThrowCharacterMul;
                up *= 0.9f;
            }

            ReleaseHeld(true);
            if (p.AsCharacter != null && !p.AsCharacter.Dead)
            {
                // Lanzar a un rival le suma % y lo manda mas lejos cuanto mas % tenga.
                p.AsCharacter.TakeHit(new HitInfo { damage = Tuning.ThrowCharDamage * (0.5f + charge), source = this, kind = HitKind.Impact });
                float km = Mathf.Max(1f, p.AsCharacter.KnockMul);
                fwd *= km;
                up *= Mathf.Sqrt(km);
            }
            if (p.Body != null)
            {
                p.Body.SetVel(horiz * 0.3f + dir * fwd + Vector3.up * up);
                p.Body.angularVelocity = Random.insideUnitSphere * 6f;
            }
            Facing = dir;
            Sfx.PlayAt(Sfx.Throw, transform.position, 0.4f + 0.4f * charge);
            SpendStamina(throwCost); // si se agota, cae de cansancio justo despues de lanzar
        }

        void ReleaseHeld(bool thrown)
        {
            var p = Holding;
            Holding = null;
            chargeButton = ChargeButton.None;
            chargeTime = 0f;
            if (p == null) return;

            if (p.CoHolder == this)
            {
                // Soy el segundo agarrador.
                p.CoHolder = null;
                SeparationGuard.Begin(p, Col);
                if (!thrown || p.Holder == null) return; // solo me suelto; el principal lo sigue cargando
                // Lo lanzo yo: el principal tambien lo pierde.
                var primary = p.Holder;
                primary.LoseHeld(p);
                p.Holder = null;
            }
            else
            {
                p.Holder = null;
                var co = p.CoHolder;
                if (co != null)
                {
                    if (!thrown)
                    {
                        // El principal suelta: el segundo pasa a cargarlo solo.
                        p.CoHolder = null;
                        p.Holder = co;
                        if (p.AsCharacter != null) p.AsCharacter.HeldBy = co;
                        SeparationGuard.Begin(p, Col);
                        return;
                    }
                    co.LoseHeld(p);
                    p.CoHolder = null;
                }
            }

            SetHeldGhost(p, false);
            if (p.Body != null) p.Body.isKinematic = false;
            p.LastThrower = this;
            p.LastThrownTime = Time.time;
            if (p.AsCharacter != null) p.AsCharacter.OnReleased(thrown);
            // Solo vuelve a chocar con nosotros (y con quien este tocando) cuando se hayan separado.
            SeparationGuard.Begin(p, Col);
            p.NotifyReleased(thrown);
        }

        /// <summary>
        /// Mientras algo esta en la mano no empuja a nadie (sus colliders pasan a ser trigger);
        /// al soltarlo vuelve a ser solido.
        /// </summary>
        static void SetHeldGhost(Pickupable p, bool ghost)
        {
            foreach (var c in p.Cols)
                if (c != null) c.isTrigger = ghost;
        }

        /// <summary>Suelta lo que tenga en la mano sin lanzarlo.</summary>
        public void DropHeld()
        {
            var p = Holding;
            if (p == null) return;
            ReleaseHeld(false);
            if (p.Body != null) p.Body.SetVel(Facing * 1.5f + Vector3.up * 2f);
        }

        /// <summary>Otro agarrador se llevo lo que teniamos entre los dos (lo lanzo): dejamos de sujetarlo.</summary>
        void LoseHeld(Pickupable p)
        {
            if (Holding != p) return;
            Holding = null;
            chargeButton = ChargeButton.None;
            chargeTime = 0f;
            SeparationGuard.Begin(p, Col);
        }

        public void OnHeldDestroyed(Pickupable p)
        {
            if (Holding == p)
            {
                Holding = null;
                chargeButton = ChargeButton.None;
                chargeTime = 0f;
            }
        }

        public void OnGrabbed(LBCharacter holder)
        {
            DropHeld();
            HeldBy = holder;
            heldTime = 0f;
            struggle = 0;
            PunchTimer = 0f;
            Clinching = false;
            // ¿De frente o por la espalda? Se mira hacia donde estaba mirando el agarrado.
            Vector3 toHolder = holder.transform.position - transform.position;
            toHolder.y = 0f;
            Vector3 look = Facing;
            look.y = 0f;
            GrabbedFromBehind = toHolder.sqrMagnitude > 0.0001f && look.sqrMagnitude > 0.0001f &&
                                Vector3.Dot(look.normalized, toHolder.normalized) < 0f;
        }

        public void OnReleased(bool thrown)
        {
            HeldBy = null;
            Clinching = false;
            if (thrown && !Dead) KnockOut(0.9f);
        }

        /// <summary>Le agarraron por la espalda (no puede aferrarse; solo soltarse golpeando con suerte).</summary>
        public bool GrabbedFromBehind;
        /// <summary>Agarrado de frente que se aferro a su agarrador: no lo pueden lanzar.</summary>
        public bool Clinching;

        /// <summary>
        /// Controles del agarrado:
        ///  - De frente: forcejear (cualquier boton) para soltarse, o pulsar agarrar para aferrarse al agarrador.
        ///  - Por la espalda: solo golpear; cada golpe tiene una probabilidad de soltarse que baja con el %.
        /// </summary>
        /// <summary>1 justo al forcejear y baja a 0: la animacion lo usa para las sacudidas.</summary>
        public float StruggleTug;

        /// <summary>Cada forcejeo da un tiron: sacude al agarrado y empuja un poco a quien lo carga.</summary>
        void Tug()
        {
            StruggleTug = 1f;
            Vector2 r = Random.insideUnitCircle.normalized * Tuning.StruggleJolt;
            var jolt = new Vector3(r.x, 0f, r.y);
            if (HeldBy != null && !HeldBy.Body.isKinematic) HeldBy.Body.AddForce(jolt, ForceMode.VelocityChange);
            var co = Grab.CoHolder;
            if (co != null && !co.Body.isKinematic) co.Body.AddForce(-jolt, ForceMode.VelocityChange);
        }

        void UpdateBeingHeld(float dt)
        {
            heldTime += dt;
            bool canFight = !IsFrozen;
            bool anyPress = Pressed(Cur.punch, prev.punch) || Pressed(Cur.jump, prev.jump) ||
                            Pressed(Cur.bomb, prev.bomb) || Pressed(Cur.pickup, prev.pickup);
            if (canFight && anyPress && !Clinching) Tug();
            if (canFight && !GrabbedFromBehind)
            {
                if (Pressed(Cur.pickup, prev.pickup) && !Clinching && !IsKO && CanGrabPlayers)
                {
                    Clinching = true;
                    HUD.Popup(HeadPos + Vector3.up * 0.4f, "¡Aferrado!", new Color(1f, 0.9f, 0.4f), 30);
                    Sfx.PlayAt(Sfx.Pickup, transform.position, 0.6f);
                }
                if (Pressed(Cur.punch, prev.punch) || Pressed(Cur.jump, prev.jump) ||
                    Pressed(Cur.bomb, prev.bomb) || Pressed(Cur.pickup, prev.pickup))
                    struggle++;
                int need = Mathf.RoundToInt(Tuning.StruggleBase + Damage / Tuning.StrugglePerPercent);
                if (struggle >= need)
                {
                    BreakFree();
                    return;
                }
            }
            else if (canFight && Pressed(Cur.punch, prev.punch))
            {
                float chance = Tuning.BehindEscapeChance / (1f + Damage / Tuning.BehindEscapePercentScale);
                if (Random.value < chance)
                {
                    BreakFree();
                    HUD.Popup(HeadPos + Vector3.up * 0.4f, "¡Libre!", new Color(0.6f, 1f, 0.6f), 30);
                    return;
                }
                Sfx.PlayAt(Sfx.Bounce, transform.position, 0.4f);
            }

            float maxHold = Mathf.Min(Tuning.MaxHoldCap, Tuning.MaxHoldBase + Damage / Tuning.MaxHoldPerPercent);
            if (heldTime > maxHold) BreakFree();
        }

        /// <summary>Se suelta de todos los que lo agarran.</summary>
        void BreakFree()
        {
            if (Grab.CoHolder != null) Grab.CoHolder.ReleaseHeld(false);
            if (HeldBy != null) HeldBy.DropHeld();
        }

        // ------------------------------------------------------------------ Dano / estados

        /// <summary>Multiplicador de empuje segun el % acumulado (estilo Smash).</summary>
        public float KnockMul => Tuning.KnockBase + Damage / Tuning.KnockPerPercent;

        /// <summary>
        /// Recibir un golpe: suma % de dano y aplica un empuje que crece con ese %.
        /// Nadie muere por dano: solo al caer de la plataforma.
        /// </summary>
        public void TakeHit(HitInfo h)
        {
            if (Dead) return;
            if (h.source != null && h.source != this && h.source.Slot != null)
            {
                lastAttacker = h.source.Slot;
                lastAttackTime = Time.time;
            }

            float dmg = h.damage;
            Vector3 imp = h.impulse;

            // Golpear a alguien congelado rompe el hielo: no lo mata, pero pega mas fuerte.
            bool shattered = false;
            if (IsFrozen && !h.freeze && dmg > 1f)
            {
                shattered = true;
                FrozenTimer = 0f;
                Visual.SetFrozen(false);
                FX.Shatter(Center);
                Sfx.PlayAt(Sfx.Shatter, Center);
                dmg *= 1.5f;
                imp *= 1.4f;
            }

            if (ShieldHp > 0f && dmg > 0f)
            {
                float absorbed = Mathf.Min(ShieldHp, dmg);
                ShieldHp -= absorbed;
                dmg -= absorbed;
                imp *= 0.4f;
                if (ShieldHp <= 0f)
                {
                    ShieldHp = 0f;
                    Visual.SetShield(false);
                    Sfx.PlayAt(Sfx.ShieldBreak, Center);
                }
                else Visual.ShieldHit();
            }

            if (dmg > 0f)
            {
                Damage = Mathf.Min(Tuning.MaxPercent, Damage + dmg * Tuning.DamageToPercent);
                LastHitTime = Time.time;
                Visual.Flash();
            }

            // El empuje se calcula con el % ya actualizado.
            imp *= KnockMul;
            if (!Body.isKinematic) Body.AddForce(imp, ForceMode.VelocityChange);
            if (h.freeze && !shattered) Freeze();
            if (dmg <= 0f) return;

            float launch = imp.magnitude;
            bool alwaysDown = h.kind == HitKind.Punch || h.kind == HitKind.Blast ||
                              (h.kind == HitKind.Impact && h.knockout > 0f);
            if (alwaysDown || launch >= Tuning.TumbleSpeed)
            {
                // Golpes y bombas SIEMPRE tumban; lo lejos que vuelas depende del % (ya aplicado al empuje).
                // Un golpe a alguien que ya esta en el suelo no alarga su K.O. (evita dejarlo tirado para siempre).
                if (!(ragdoll && h.kind == HitKind.Punch))
                {
                    float ko = (h.kind == HitKind.Punch ? Tuning.PunchKOBase : 0.35f) + launch * 0.035f + h.knockout * 0.3f;
                    // Las bombas aturden mas tiempo cuanto mas % llevas.
                    if (h.kind == HitKind.Blast) ko += Damage * Tuning.BlastKOPerPercent;
                    KnockOut(Mathf.Min(ko, Tuning.MaxKOTime));
                }
                // Aunque el empuje sea minimo, que se note la caida.
                if (!Body.isKinematic && launch < 2f)
                    Body.AddForce(Vector3.up * 2f + (imp.sqrMagnitude > 0.0001f ? imp.normalized : Vector3.zero), ForceMode.VelocityChange);
            }
            else
                StaggerTimer = Mathf.Max(StaggerTimer, h.stagger + launch * 0.02f);
        }

        public void KnockOut(float t)
        {
            if (Dead || t <= 0f) return;
            KoTimer = Mathf.Max(KoTimer, t);
            recoverTimer = 0f;
            if (ragdoll) return;
            ragdoll = true;
            DropHeld();
            PunchTimer = 0f;
            Body.freezeRotation = false;
            Compat.SetPhysMat(Col, 0.7f, 0.15f, false);
            if (!Body.isKinematic) Body.angularVelocity += Random.insideUnitSphere * 5f;
            Visual.SetFloppy(true);
        }

        void TryRecover()
        {
            if (Dead) return;
            if (!Body.isKinematic && Body.Vel().magnitude > 3.5f)
            {
                KoTimer = 0.15f;
                return;
            }
            ragdoll = false;
            Vector3 f = transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Facing;
            Facing = f.normalized;
            Body.freezeRotation = true;
            Compat.SetPhysMat(Col, 0f, 0f, true);
            Visual.SetFloppy(false);

            if (HeldBy != null || Body.isKinematic)
            {
                // Agarrado: simplemente se endereza.
                Quaternion q = Quaternion.LookRotation(Facing);
                transform.rotation = q;
                Body.rotation = q;
                return;
            }

            // Brinquito de recuperacion: sale hacia arriba y el cuerpo gira hasta quedar de pie (ver RecoverStep).
            Body.angularVelocity = Vector3.zero;
            Vector3 v = Body.Vel();
            Body.SetVel(new Vector3(v.x * 0.3f, Tuning.RecoverHop, v.z * 0.3f));
            recoverTimer = Tuning.RecoverTime;
            Sfx.PlayAt(Sfx.Jump, transform.position, 0.35f);
        }

        /// <summary>Gira el cuerpo hacia la vertical alrededor de su centro, sin atravesar el suelo.</summary>
        void RecoverStep(float dt)
        {
            Quaternion upright = Quaternion.LookRotation(Facing);
            Vector3 center = Body.position + Body.rotation * Col.center;
            Quaternion nr = recoverTimer <= 0f
                ? upright
                : Quaternion.RotateTowards(Body.rotation, upright, Tuning.RecoverSpin * dt);
            Vector3 np = center - nr * Col.center;

            if (GroundBelow(center, out float gy))
            {
                float half = Col.height * 0.5f - Col.radius;
                Vector3 a = np + nr * (Col.center + Vector3.down * half);
                Vector3 b = np + nr * (Col.center + Vector3.up * half);
                float lowest = Mathf.Min(a.y, b.y) - Col.radius;
                if (lowest < gy + 0.01f) np.y += gy + 0.01f - lowest;
            }
            Body.rotation = nr;
            Body.position = np;
        }

        void Freeze()
        {
            FrozenTimer = Tuning.FreezeTime;
            DropHeld();
            PunchTimer = 0f;
            Visual.SetFrozen(true);
            Sfx.PlayAt(Sfx.Freeze, Center);
        }

        public void Die(HitKind kind)
        {
            if (Dead) return;
            Dead = true;
            DiedFalling = kind == HitKind.Fall;
            DropHeld();
            if (HeldBy != null) HeldBy.DropHeld();
            ShieldHp = 0f;
            CurseTimer = -1f;
            FrozenTimer = 0f;
            Visual.SetShield(false);
            Visual.SetCursed(false);
            Visual.SetFrozen(false);

            PlayerSlot killer = (lastAttacker != null && lastAttacker != Slot &&
                                 Time.time - lastAttackTime < Tuning.KillCreditTime) ? lastAttacker : null;

            if (!Body.isKinematic)
            {
                ragdoll = true;
                Body.freezeRotation = false;
                Compat.SetPhysMat(Col, 0.7f, 0.15f, false);
            }
            if (kind == HitKind.Fall) Visual.SetFallingDeath(); // sigue pataleando mientras cae
            else Visual.SetFloppy(true);
            if (kind == HitKind.Shatter) Visual.Hide();
            if (kind != HitKind.Fall) Sfx.PlayAt(Sfx.Death, Center);
            else if (!screamed) Sfx.PlayVoice(Sfx.GetScream(voice), Center, 0.85f, voicePitch);

            Died?.Invoke(this, killer, kind);
            StartCoroutine(DeathRoutine(kind));
        }

        IEnumerator DeathRoutine(HitKind kind)
        {
            float wait = kind == HitKind.Shatter ? 0.1f : (kind == HitKind.Fall ? 1.0f : 1.7f);
            yield return new WaitForSeconds(wait);
            if (kind != HitKind.Shatter && kind != HitKind.Fall)
                FX.Poof(Center, Slot != null ? Slot.Color : Color.white);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (IsProxy)
            {
                if (Slot != null && Slot.Character == this) Slot.Character = null;
                return;
            }
            if (Holding != null)
            {
                var p = Holding;
                Holding = null;
                if (p.CoHolder == this) p.CoHolder = null;
                else if (p.CoHolder != null)
                {
                    // Pasa a cargarlo el segundo agarrador.
                    p.Holder = p.CoHolder;
                    p.CoHolder = null;
                    if (p.AsCharacter != null) p.AsCharacter.HeldBy = p.Holder;
                }
                else
                {
                    p.Holder = null;
                    SetHeldGhost(p, false);
                    if (p.Body != null) p.Body.isKinematic = false;
                    SeparationGuard.Begin(p, null);
                    if (p.AsCharacter != null) p.AsCharacter.OnReleased(false);
                    p.NotifyReleased(false);
                }
            }
            if (Slot != null && Slot.Character == this) Slot.Character = null;
        }

        // ------------------------------------------------------------------ Powerups

        public void ApplyPowerup(PowerupType t)
        {
            string label;
            switch (t)
            {
                case PowerupType.TripleBombs:
                    tripleTimer = Tuning.PowerupWearOff;
                    label = "¡Triple Bomba!";
                    break;
                case PowerupType.IceBombs:
                    bombType = BombType.Ice;
                    bombTypeTimer = Tuning.PowerupWearOff;
                    label = "¡Bombas de Hielo!";
                    break;
                case PowerupType.StickyBombs:
                    bombType = BombType.Sticky;
                    bombTypeTimer = Tuning.PowerupWearOff;
                    label = "¡Bombas Pegajosas!";
                    break;
                case PowerupType.ImpactBombs:
                    bombType = BombType.Impact;
                    bombTypeTimer = Tuning.PowerupWearOff;
                    label = "¡Bombas de Impacto!";
                    break;
                case PowerupType.LandMines:
                    LandMines = 3;
                    label = "¡Minas!";
                    break;
                case PowerupType.Gloves:
                    glovesTimer = Tuning.PowerupWearOff;
                    Visual.SetGloves(true);
                    label = "¡Guantes de Boxeo!";
                    break;
                case PowerupType.Shield:
                    ShieldHp = Tuning.ShieldHp;
                    Visual.SetShield(true);
                    label = "¡Escudo!";
                    break;
                case PowerupType.Health:
                    Damage = Mathf.Max(0f, Damage - Tuning.HealPercent);
                    Visual.HealLayers(1); // repara una capa del cuerpo en todas las zonas
                    CurseTimer = -1f;
                    Visual.SetCursed(false);
                    label = "¡Salud!";
                    break;
                default:
                    if (CurseTimer < 0f) CurseTimer = Tuning.CurseTime;
                    Visual.SetCursed(true);
                    label = "¡MALDICIÓN!";
                    break;
            }
            HUD.Popup(HeadPos + Vector3.up * 0.4f, label, Gfx.PowerupColor(t));
            Sfx.PlayAt(t == PowerupType.Curse ? Sfx.Curse : Sfx.Powerup, Center);
        }
    }
}
