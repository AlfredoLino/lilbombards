using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LB
{
    /// <summary>
    /// Cliente: se conecta al anfitrion, le envia sus controles (teclado+raton o mando) y reproduce
    /// el mundo recibido con personajes/bombas/cajas "espejo" (sin logica propia).
    /// </summary>
    public class NetClient
    {
        readonly string address;
        readonly string name;
        TcpClient tcp;
        Task connectTask;
        NetConn conn;
        public int MyId = -1;
        public string Status = "Conectando...";
        public bool Connected => conn != null && !conn.Dead && MyId > 0;

        readonly KeyboardInput keyboard = new KeyboardInput(0);
        readonly byte[] counts = new byte[4];
        InputState prev;
        float sendTimer;

        // Entidades espejo por id de red.
        public readonly Dictionary<int, LBCharacter> Chars = new Dictionary<int, LBCharacter>();
        public readonly Dictionary<int, Bomb> Bombs = new Dictionary<int, Bomb>();
        public readonly Dictionary<int, PowerupBox> Powerups = new Dictionary<int, PowerupBox>();
        public readonly Dictionary<int, TntBox> Tnts = new Dictionary<int, TntBox>();
        public readonly Dictionary<int, PlayerSlot> Slots = new Dictionary<int, PlayerSlot>();

        public NetClient(string address, string name)
        {
            this.address = address;
            this.name = string.IsNullOrWhiteSpace(name) ? "Jugador" : name.Trim();
            try
            {
                tcp = new TcpClient();
                connectTask = tcp.ConnectAsync(address, Net.Port);
            }
            catch (Exception e)
            {
                Status = "Error: " + e.Message;
            }
            if (GameManager.I != null) GameManager.I.ClearWorld();
        }

        public void Stop()
        {
            conn?.Close();
            try { tcp?.Close(); } catch (Exception) { }
            if (GameManager.I != null) GameManager.I.ClearWorld();
            Chars.Clear();
            Bombs.Clear();
            Powerups.Clear();
            Tnts.Clear();
            Slots.Clear();
        }

        public void Tick()
        {
            if (conn == null)
            {
                if (connectTask == null) { Fail(Status); return; }
                if (!connectTask.IsCompleted) return;
                if (connectTask.IsFaulted || !tcp.Connected)
                {
                    Fail("No se pudo conectar a " + address + ":" + Net.Port);
                    return;
                }
                conn = new NetConn(tcp);
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                w.Write((byte)Msg.Hello);
                w.Write(Net.Protocol);
                w.Write(Net.GameVersion);
                w.Write(name);
                conn.Send(ms);
                Status = "Conectado, esperando al anfitrión...";
            }

            foreach (var m in conn.Poll())
            {
                var r = new BinaryReader(new MemoryStream(m));
                switch ((Msg)r.ReadByte())
                {
                    case Msg.Welcome:
                        MyId = r.ReadInt32();
                        Status = "Conectado a " + address;
                        break;
                    case Msg.Reject:
                        Fail(r.ReadString());
                        return;
                    case Msg.Snapshot:
                        NetSnapshot.Apply(r, this);
                        break;
                }
            }
            if (conn.Dead)
            {
                Fail("Se perdió la conexión con el anfitrión");
                return;
            }

            SendInput();
        }

        void Fail(string why)
        {
            NetMenu.Message = why;
            Net.Stop();
            if (GameManager.I != null) GameManager.I.OnNetStopped();
        }

        /// <summary>Teclado+raton y mando combinados: lo que se este usando.</summary>
        InputState ReadLocal()
        {
            var s = keyboard.Read();
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null)
            {
                var g = new GamepadInput(pad).Read();
                if (g.move.sqrMagnitude > s.move.sqrMagnitude) s.move = g.move;
                s.jump |= g.jump;
                s.punch |= g.punch;
                s.pickup |= g.pickup;
                s.run |= g.run;
                if (g.bomb && !s.bomb)
                {
                    s.bomb = true;
                    s.clickBomb = false; // con mando: mantener = cargar
                }
            }
#endif
            return s;
        }

        void SendInput()
        {
            if (MyId <= 0) return;
            var s = ReadLocal();
            if (s.jump && !prev.jump) counts[0]++;
            if (s.punch && !prev.punch) counts[1]++;
            if (s.bomb && !prev.bomb) counts[2]++;
            if (s.pickup && !prev.pickup) counts[3]++;
            bool changed = s.jump != prev.jump || s.punch != prev.punch || s.bomb != prev.bomb || s.pickup != prev.pickup ||
                           s.run != prev.run || (s.move - prev.move).sqrMagnitude > 0.0025f;
            prev = s;

            // El personaje propio espejo usa la punteria local (mira del raton).
            var me = MyCharacter();
            if (me != null) me.Cur = s;

            sendTimer -= Time.unscaledDeltaTime;
            if (!changed && sendTimer > 0f) return;
            sendTimer = 1f / 30f;

            var ms = new MemoryStream(48);
            var w = new BinaryWriter(ms);
            w.Write((byte)Msg.Input);
            w.Write(s.move.x);
            w.Write(s.move.y);
            byte f = 0;
            if (s.jump) f |= 1;
            if (s.punch) f |= 2;
            if (s.bomb) f |= 4;
            if (s.pickup) f |= 8;
            if (s.run) f |= 16;
            if (s.clickBomb) f |= 32;
            if (s.hasAim) f |= 64;
            w.Write(f);
            w.WriteV(s.aimPoint);
            w.Write(counts);
            conn.Send(ms);
        }

        public LBCharacter MyCharacter()
        {
            foreach (var s in Slots.Values)
                if (s.Device is int owner && owner == MyId) return s.Character;
            return null;
        }
    }
}
