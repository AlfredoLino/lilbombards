using UnityEngine;

namespace LB
{
    /// <summary>
    /// IA de bot: produce un InputState igual que un jugador humano.
    /// Persigue al enemigo mas cercano, golpea de cerca, lanza bombas a media distancia,
    /// deja bombas a sus pies y huye, recoge powerups, huye de bombas encendidas,
    /// evita caerse y a veces agarra rivales para tirarlos por el borde.
    /// </summary>
    public class BotBrain : IInputProvider
    {
        LBCharacter me;
        readonly float skill;
        InputState st, last;
        LBCharacter target;
        float retarget, bombHold = -1f, bombCool, punchCool, holdTimer, wanderT, fleeT;
        Vector3 wander, fleeDir;
        bool aimNeutral;
        float aimErr;
        float struggleCool;

        public BotBrain(float skill)
        {
            this.skill = Mathf.Clamp01(skill);
        }

        public void Attach(LBCharacter c)
        {
            me = c;
            bombHold = -1f;
            bombCool = Random.Range(0.5f, 1.5f);
            target = null;
        }

        public InputState Read()
        {
            if (me == null || me.Dead) return default;
            last = st;
            Think(Time.deltaTime);
            return st;
        }

        bool Tap(bool lastVal) => !lastVal;

        void Think(float dt)
        {
            st = new InputState();
            bombCool -= dt;
            punchCool -= dt;
            retarget -= dt;
            wanderT -= dt;
            fleeT -= dt;

            if (me.HeldBy != null)
            {
                // De frente: a veces se aferra al agarrador para que no lo lance.
                if (!me.GrabbedFromBehind && !me.Clinching && Random.value < 0.02f + skill * 0.06f)
                {
                    st.pickup = Tap(last.pickup);
                    return;
                }
                // Forcejear / golpear para soltarse, a ritmo humano (3-6 pulsaciones por segundo).
                struggleCool -= dt;
                if (struggleCool <= 0f)
                {
                    st.punch = true;
                    struggleCool = Mathf.Lerp(0.35f, 0.17f, skill) + Random.Range(0f, 0.1f);
                }
                return;
            }
            if (me.IsKO || me.IsFrozen || !GameManager.AllowControl)
            {
                bombHold = -1f;
                return;
            }

            Vector3 pos = me.transform.position;
            if (retarget <= 0f || target == null || target.Dead)
            {
                target = PickTarget(pos);
                retarget = Random.Range(0.6f, 1.4f);
            }

            Vector3 move = Vector3.zero;
            bool run = false;
            bool allowEdge = false;

            // --- 1. Lo que tengo en la mano
            if (me.Holding != null)
            {
                if (me.Holding.AsCharacter != null)
                {
                    holdTimer += dt;
                    Vector3 outward = Outward(pos);
                    move = outward;
                    run = true;
                    allowEdge = true;
                    if (Arena.I.EdgeDistance(pos) < 1.8f || holdTimer > 2.2f)
                    {
                        // Cargar un poco el lanzamiento para sacarlo del mapa.
                        st.pickup = me.ThrowCharge < 0f ? !last.pickup : me.ThrowCharge < 0.6f;
                        move *= 0.3f;
                    }
                }
                else if (bombHold > 0f)
                {
                    // Cargando la bomba: casi quieto, mirando al objetivo; se suelta al llegar a la carga necesaria.
                    bombHold -= dt;
                    Vector3 dir = AimDir(pos, out float dist);
                    move = dir * 0.25f;
                    float wanted = aimNeutral ? 0f : LBCharacter.ChargeForDistance(dist / me.ThrowMul);
                    bool release = bombHold <= 0f || (me.ThrowCharge >= 0f && me.ThrowCharge >= wanted);
                    st.bomb = !release;
                    if (release)
                    {
                        bombHold = -1f;
                        move = aimNeutral ? Vector3.zero : dir;
                        bombCool = Mathf.Lerp(4f, 1.2f, skill) + Random.Range(0f, 1.5f);
                        if (aimNeutral)
                        {
                            fleeT = 1.2f;
                            fleeDir = -me.Facing;
                        }
                    }
                }
                else
                {
                    // Algo agarrado sin plan: lanzarlo hacia el objetivo con la carga adecuada.
                    Vector3 dir = AimDir(pos, out float dist);
                    move = dir * 0.25f;
                    if (me.ThrowCharge < 0f) st.pickup = !last.pickup;
                    else
                    {
                        st.pickup = me.ThrowCharge < LBCharacter.ChargeForDistance(dist / me.ThrowMul);
                        if (!st.pickup) move = dir;
                    }
                }
                Output(move, run, allowEdge, pos);
                return;
            }
            holdTimer = 0f;
            bombHold = -1f;

            // --- 2. Huir de bombas peligrosas
            Bomb danger = null;
            float dangerD = 3.4f;
            foreach (var b in Bomb.All)
            {
                if (!b.IsDangerous || b.Grab.Holder != null) continue;
                float d = Vector3.Distance(b.transform.position, pos);
                if (d < dangerD)
                {
                    dangerD = d;
                    danger = b;
                }
            }
            if (danger != null && (danger.FuseLeft < 1.6f || danger.Type != BombType.Normal || dangerD < 1.5f))
            {
                Vector3 away = pos - danger.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away.y = 0f;
                move = away.normalized;
                run = true;
                if (dangerD < 1.2f && me.Grounded && Random.value < 0.05f) st.jump = Tap(last.jump);
                Output(move, run, false, pos);
                return;
            }
            if (fleeT > 0f)
            {
                Output(fleeDir, true, false, pos);
                return;
            }

            // --- 3. Powerups cercanos
            PowerupBox best = null;
            float bestD = 7f;
            foreach (var p in PowerupBox.All)
            {
                if (p.Type == PowerupType.Curse && skill > 0.3f) continue;
                if (p.Type == PowerupType.Health && me.Damage < 40f) continue;
                float d = Vector3.Distance(p.transform.position, pos);
                if (d < bestD && Arena.I.EdgeDistance(p.transform.position) > 0.6f)
                {
                    bestD = d;
                    best = p;
                }
            }
            float targetD = target != null ? Flat(target.transform.position - pos).magnitude : 999f;
            if (best != null && bestD < targetD * 0.7f)
            {
                move = Flat(SteerTo(pos, best.transform.position) - pos).normalized;
                Output(move, bestD > 2f, false, pos);
                return;
            }

            // --- 4. Atacar
            if (target != null)
            {
                Vector3 to = Flat(target.transform.position - pos);
                float d = to.magnitude;
                Vector3 dir = d > 0.01f ? to / d : me.Facing;
                float facingDot = Vector3.Dot(me.Facing, dir);
                bool canBomb = bombCool <= 0f && (Bomb.ActiveCount(me) < me.BombCount || me.LandMines > 0);

                if (d < 1.5f)
                {
                    move = dir * 0.6f;
                    if (punchCool <= 0f && facingDot > 0.6f && me.Stamina > Tuning.PunchCost + 12f)
                    {
                        if (Random.value < 0.12f * skill && !target.IsKO)
                            st.pickup = Tap(last.pickup);
                        else
                            st.punch = Tap(last.punch);
                        punchCool = Mathf.Lerp(1.3f, 0.28f, skill) + Random.Range(0f, 0.4f);
                    }
                }
                else if (canBomb && !me.BothArmsGone && d > 3f && d < me.ThrowDistanceFor(1f) && Random.value < dt * (0.25f + skill * 2.5f))
                {
                    bombHold = Tuning.ThrowChargeTime + 0.3f; // limite de seguridad
                    aimNeutral = false;
                    aimErr = Random.Range(-1f, 1f) * (1f - skill) * 20f;
                    st.bomb = true;
                    move = dir * 0.25f;
                }
                else if (canBomb && d < 3.2f && d > 1.6f && Random.value < dt * (0.1f + 0.5f * skill))
                {
                    // Dejar la bomba a los pies y salir corriendo.
                    bombHold = 0.05f;
                    aimNeutral = true;
                    st.bomb = true;
                }
                else
                {
                    // Acercarse con algo de zigzag (por los puentes / rodeando agujeros si hace falta).
                    Vector3 path = Flat(SteerTo(pos, target.transform.position) - pos);
                    path = path.sqrMagnitude > 0.0001f ? path.normalized : dir;
                    Vector3 side = new Vector3(-path.z, 0f, path.x) * Mathf.Sin(Time.time * 1.7f + me.GetHashCode()) * 0.35f;
                    move = (path + side).normalized;
                    run = d > 3f && skill > 0.4f;
                }
            }
            else
            {
                if (wanderT <= 0f)
                {
                    wanderT = Random.Range(1f, 2.5f);
                    wander = Arena.I != null ? Arena.I.RandomSafePoint() : new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-3f, 3f));
                }
                move = Flat(SteerTo(pos, wander) - pos);
                if (Flat(wander - pos).magnitude < 0.5f) move = Vector3.zero;
                if (move.magnitude < 0.5f) move = Vector3.zero;
                else move.Normalize();
            }

            Output(move, run, allowEdge, pos);
        }

        Vector3 steerGoal, steerResult;
        float steerTimer;

        /// <summary>Ruta hacia un punto (puentes, rodear agujeros), recalculada 5 veces por segundo.</summary>
        Vector3 SteerTo(Vector3 pos, Vector3 goal)
        {
            if (Arena.I == null) return goal;
            steerTimer -= Time.deltaTime;
            if (steerTimer <= 0f || (goal - steerGoal).sqrMagnitude > 1f)
            {
                steerTimer = 0.2f;
                steerGoal = goal;
                steerResult = Arena.I.Steer(pos, goal);
            }
            return steerResult;
        }

        void Output(Vector3 move, bool run, bool allowEdge, Vector3 pos)
        {
            if (!allowEdge && Arena.I != null)
            {
                float edge = Arena.I.EdgeDistance(pos);
                Vector3 inward = -Arena.I.Outward(pos); // lejos del borde o agujero mas cercano
                if (edge < 1.4f)
                {
                    float outwardAmount = Vector3.Dot(move, -inward);
                    if (outwardAmount > 0f) move += inward * outwardAmount * 1.2f;
                    if (edge < 0.7f) move = (move + inward * 1.5f).normalized;
                }
            }
            if (move.sqrMagnitude > 1f) move.Normalize();
            st.move = new Vector2(move.x, move.z);
            st.run = run && me.Stamina > 35f; // los bots tambien administran su estamina
        }

        /// <summary>Direccion de lanzamiento hacia el objetivo (con prediccion y error segun habilidad) y distancia.</summary>
        Vector3 AimDir(Vector3 pos, out float dist)
        {
            dist = 3f;
            if (target == null || target.Dead) return me.Facing;
            Vector3 tv = target.Body != null && !target.Body.isKinematic ? target.Body.Vel() : Vector3.zero;
            Vector3 tp = target.transform.position + Flat(tv) * 0.7f;
            Vector3 to = Flat(tp - pos);
            dist = to.magnitude;
            Vector3 dir = dist > 0.01f ? to / dist : me.Facing;
            return Quaternion.Euler(0f, aimErr, 0f) * dir;
        }

        LBCharacter PickTarget(Vector3 pos)
        {
            LBCharacter best = null;
            float bestScore = float.MaxValue;
            foreach (var c in LBCharacter.All)
            {
                if (c == me || c.Dead) continue;
                if (Arena.I != null && Arena.I.EdgeDistance(c.transform.position) < -1f) continue;
                float d = Vector3.Distance(c.transform.position, pos);
                // Preferencia leve por humanos para que la partida sea entretenida.
                if (c.Slot != null && !c.Slot.IsBot) d *= 0.8f;
                d += Random.Range(0f, 2f);
                if (d < bestScore)
                {
                    bestScore = d;
                    best = c;
                }
            }
            return best;
        }

        static Vector3 Outward(Vector3 pos)
        {
            // Hacia el borde (o agujero) mas cercano del mapa.
            if (Arena.I != null) return Arena.I.Outward(pos);
            Vector3 o = Flat(pos);
            return o.sqrMagnitude < 0.01f ? Vector3.right : o.normalized;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
