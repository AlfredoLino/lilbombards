using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Instantanea del mundo: fase de la partida, jugadores, personajes (pose y estado), bombas, cajas,
    /// TNT y los eventos ocurridos desde la anterior. El anfitrion la escribe; el cliente la aplica.
    /// </summary>
    public static class NetSnapshot
    {
        const ushort FGrounded = 1, FRagdoll = 2, FDead = 4, FFrozen = 8, FCursed = 16, FShield = 32, FGloves = 64,
            FPunchRight = 128, FClinching = 256, FBehind = 512, FFallDeath = 1024;

        // ================================================================== Anfitrion

        public static void Write(BinaryWriter w, NetHost host)
        {
            var gm = GameManager.I;
            w.Write((byte)gm.Phase);
            w.Write((short)gm.KillsToWin);
            w.Write(Time.timeScale);
            w.Write((byte)(Arena.I != null ? Arena.I.Map : gm.MapIndex));

            var players = gm.VisiblePlayers;
            w.Write((byte)players.Count);
            foreach (var p in players)
            {
                w.Write(p.Index);
                w.Write(p.Name ?? "");
                w.WriteC(p.Color);
                w.WriteC(p.Highlight);
                w.Write(p.IsBot);
                w.Write((short)p.Kills);
                w.Write((short)p.Deaths);
                w.Write(p.Character != null ? p.Character.NetId : 0);
                w.Write(p.Device is NetHost.Peer peer ? peer.Id : 0);
            }

            w.Write((short)LBCharacter.All.Count);
            foreach (var c in LBCharacter.All)
            {
                w.Write(c.NetId);
                w.Write(c.Slot != null ? c.Slot.Index : -1);
                w.WriteV(c.transform.position);
                w.WriteQ(c.transform.rotation);
                ushort f = 0;
                if (c.Grounded) f |= FGrounded;
                if (c.IsKO && !c.Dead) f |= FRagdoll;
                if (c.Dead) f |= FDead;
                if (c.IsFrozen) f |= FFrozen;
                if (c.Cursed) f |= FCursed;
                if (c.ShieldHp > 0f) f |= FShield;
                if (c.HasGloves) f |= FGloves;
                if (c.PunchRight) f |= FPunchRight;
                if (c.Clinching) f |= FClinching;
                if (c.GrabbedFromBehind) f |= FBehind;
                if (c.DiedFalling) f |= FFallDeath;
                w.Write(f);
                w.Write(c.PunchTimer);
                w.Write(c.ThrowCharge);
                w.Write(HoldId(c.Holding));
                w.Write(c.HeldBy != null ? c.HeldBy.NetId : 0);
                w.Write(c.Damage);
                w.WriteU8(c.Stamina / Tuning.StaminaMax);
                w.Write(c.CurseTimer);
                w.WriteU8(c.StruggleTug);
                w.Write((byte)(c.ArmLoss[0] | (c.ArmLoss[1] << 2) | (c.LegLoss[0] << 4) | (c.LegLoss[1] << 6)));
                for (int i = 0; i < CharacterVisual.SectionCount; i++)
                {
                    float d = c.Visual != null ? c.Visual.SectionDanger((CharacterVisual.Section)i) : 0f;
                    w.Write(d < 0f ? (byte)255 : (byte)Mathf.RoundToInt(Mathf.Clamp01(d) * 254f));
                }
            }

            w.Write((short)Bomb.All.Count);
            foreach (var b in Bomb.All)
            {
                w.Write(b.NetId);
                w.Write((byte)b.Type);
                w.WriteV(b.transform.position);
                w.WriteQ(b.transform.rotation);
                w.Write(b.Lit);
                w.WriteU8(b.FuseFraction);
                w.Write(b.Armed);
            }

            w.Write((short)PowerupBox.All.Count);
            foreach (var p in PowerupBox.All)
            {
                w.Write(p.NetId);
                w.Write((byte)p.Type);
                w.WriteV(p.transform.position);
                w.WriteQ(p.transform.rotation);
            }

            w.Write((short)TntBox.All.Count);
            foreach (var t in TntBox.All)
            {
                w.Write(t.NetId);
                w.WriteV(t.transform.position);
                w.WriteQ(t.transform.rotation);
            }

            var ev = Net.TakeEvents(out int count);
            w.Write(count);
            w.Write(ev);
        }

        static int HoldId(Pickupable p)
        {
            if (p == null) return 0;
            if (p.AsCharacter != null) return p.AsCharacter.NetId;
            if (p.AsBomb != null) return p.AsBomb.NetId;
            return 0;
        }

        // ================================================================== Cliente

        struct CharState
        {
            public LBCharacter ch;
            public int holding, heldBy;
        }

        public static void Apply(BinaryReader r, NetClient cl)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var phase = (GamePhase)r.ReadByte();
            int kills = r.ReadInt16();
            float timeScale = r.ReadSingle();
            int map = r.ReadByte();
            gm.ClientApplyState(phase, kills, timeScale, map);

            // --- Jugadores
            int pc = r.ReadByte();
            var players = new List<PlayerSlot>(pc);
            for (int i = 0; i < pc; i++)
            {
                int index = r.ReadInt32();
                if (!cl.Slots.TryGetValue(index, out var s))
                {
                    s = new PlayerSlot { Index = index };
                    cl.Slots[index] = s;
                }
                s.Name = r.ReadString();
                s.Color = r.ReadC();
                s.Highlight = r.ReadC();
                s.IsBot = r.ReadBoolean();
                s.Kills = r.ReadInt16();
                s.Deaths = r.ReadInt16();
                r.ReadInt32(); // id del personaje (se enlaza al crearlo)
                s.Device = r.ReadInt32();
                players.Add(s);
            }
            gm.ClientSetPlayers(players);

            // --- Personajes
            var seen = new HashSet<int>();
            var states = new List<CharState>();
            int cc = r.ReadInt16();
            for (int i = 0; i < cc; i++)
            {
                int id = r.ReadInt32();
                int slotIndex = r.ReadInt32();
                Vector3 pos = r.ReadV();
                Quaternion rot = r.ReadQ();
                ushort f = r.ReadUInt16();
                float punch = r.ReadSingle();
                float charge = r.ReadSingle();
                int holding = r.ReadInt32();
                int heldBy = r.ReadInt32();
                float dmg = r.ReadSingle();
                float stamina = r.ReadU8() * Tuning.StaminaMax;
                float curse = r.ReadSingle();
                float tug = r.ReadU8();
                byte loss = r.ReadByte();
                var danger = new float[CharacterVisual.SectionCount];
                for (int k = 0; k < danger.Length; k++)
                {
                    byte b = r.ReadByte();
                    danger[k] = b == 255 ? -1f : b / 254f;
                }
                seen.Add(id);

                if (!cl.Chars.TryGetValue(id, out var ch) || ch == null)
                {
                    if (!cl.Slots.TryGetValue(slotIndex, out var slot))
                    {
                        slot = new PlayerSlot { Index = slotIndex, Name = "?", Color = Color.gray, Highlight = Color.white };
                        cl.Slots[slotIndex] = slot;
                    }
                    ch = LBCharacter.Spawn(slot, pos, rot.eulerAngles.y);
                    ch.MakeProxy(id);
                    if (slot.Device is int owner && owner == cl.MyId) AimMarker.Attach(ch);
                    cl.Chars[id] = ch;
                }
                ch.ApplyNetState(pos, rot, (f & FGrounded) != 0, (f & FRagdoll) != 0, (f & FDead) != 0, (f & FFallDeath) != 0,
                    (f & FFrozen) != 0, (f & FCursed) != 0, (f & FShield) != 0, (f & FGloves) != 0,
                    (f & FPunchRight) != 0, (f & FClinching) != 0, (f & FBehind) != 0,
                    punch, charge, dmg, stamina, curse, tug, loss, danger);
                states.Add(new CharState { ch = ch, holding = holding, heldBy = heldBy });
            }
            RemoveMissing(cl.Chars, seen);

            // --- Bombas
            seen.Clear();
            int bc = r.ReadInt16();
            for (int i = 0; i < bc; i++)
            {
                int id = r.ReadInt32();
                var type = (BombType)r.ReadByte();
                Vector3 pos = r.ReadV();
                Quaternion rot = r.ReadQ();
                bool lit = r.ReadBoolean();
                float fuse = r.ReadU8();
                bool armed = r.ReadBoolean();
                seen.Add(id);
                if (!cl.Bombs.TryGetValue(id, out var b) || b == null)
                {
                    b = Bomb.Create(type, pos, null);
                    b.MakeProxy(id);
                    cl.Bombs[id] = b;
                }
                b.ApplyNetState(pos, rot, lit, fuse, armed);
            }
            RemoveMissing(cl.Bombs, seen);

            // --- Cajas de powerup
            seen.Clear();
            int pwc = r.ReadInt16();
            for (int i = 0; i < pwc; i++)
            {
                int id = r.ReadInt32();
                var type = (PowerupType)r.ReadByte();
                Vector3 pos = r.ReadV();
                Quaternion rot = r.ReadQ();
                seen.Add(id);
                if (!cl.Powerups.TryGetValue(id, out var p) || p == null)
                {
                    p = PowerupBox.Create(type, pos);
                    p.MakeProxy(id);
                    cl.Powerups[id] = p;
                }
                NetSmooth.Set(p.gameObject, pos, rot);
            }
            RemoveMissing(cl.Powerups, seen);

            // --- TNT
            seen.Clear();
            int tc = r.ReadInt16();
            for (int i = 0; i < tc; i++)
            {
                int id = r.ReadInt32();
                Vector3 pos = r.ReadV();
                Quaternion rot = r.ReadQ();
                seen.Add(id);
                if (!cl.Tnts.TryGetValue(id, out var t) || t == null)
                {
                    t = TntBox.Create(pos);
                    t.MakeProxy(id);
                    cl.Tnts[id] = t;
                }
                NetSmooth.Set(t.gameObject, pos, rot);
            }
            RemoveMissing(cl.Tnts, seen);

            // --- Quien sostiene a quien (cuando ya existen todas las entidades)
            foreach (var s in states)
                s.ch.ApplyNetHolding(Resolve(cl, s.holding), s.heldBy != 0 && cl.Chars.TryGetValue(s.heldBy, out var hb) ? hb : null);

            // --- Eventos
            int evCount = r.ReadInt32();
            Net.PlayEvents(r, evCount, id => cl.Chars.TryGetValue(id, out var c) ? c : null);
        }

        static Pickupable Resolve(NetClient cl, int id)
        {
            if (id == 0) return null;
            if (cl.Chars.TryGetValue(id, out var c) && c != null) return c.Grab;
            if (cl.Bombs.TryGetValue(id, out var b) && b != null) return b.Grab;
            return null;
        }

        static void RemoveMissing<T>(Dictionary<int, T> map, HashSet<int> seen) where T : Component
        {
            var gone = new List<int>();
            foreach (var kv in map)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                var c = map[id];
                if (c != null) Object.Destroy(c.gameObject);
                map.Remove(id);
            }
        }
    }

    /// <summary>Movimiento suave de las entidades espejo hacia su ultima posicion recibida.</summary>
    public class NetSmooth : MonoBehaviour
    {
        Vector3 pos;
        Quaternion rot;
        bool has;

        public static void Set(GameObject go, Vector3 p, Quaternion r)
        {
            var s = go.GetComponent<NetSmooth>();
            if (s == null)
            {
                s = go.AddComponent<NetSmooth>();
                go.transform.SetPositionAndRotation(p, r);
            }
            s.pos = p;
            s.rot = r;
            s.has = true;
        }

        void Update()
        {
            if (!has) return;
            float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            // Saltos grandes (reaparecer, teletransporte): sin suavizado.
            if ((transform.position - pos).sqrMagnitude > 9f) transform.position = pos;
            else transform.position = Vector3.Lerp(transform.position, pos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, k);
        }
    }
}
