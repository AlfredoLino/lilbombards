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
        readonly string code;      // sala online (null = conexion directa por IP)
        bool waitingRelay;
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
        float pingTimer;
        /// <summary>Ping con el anfitrion en ms (ida y vuelta), -1 mientras no se sabe.</summary>
        public int Ping = -1;

        // Entidades espejo por id de red.
        public readonly Dictionary<int, LBCharacter> Chars = new Dictionary<int, LBCharacter>();
        public readonly Dictionary<int, Bomb> Bombs = new Dictionary<int, Bomb>();
        public readonly Dictionary<int, PowerupBox> Powerups = new Dictionary<int, PowerupBox>();
        public readonly Dictionary<int, TntBox> Tnts = new Dictionary<int, TntBox>();
        public readonly Dictionary<int, PlayerSlot> Slots = new Dictionary<int, PlayerSlot>();

        public NetClient(string address, string name, string code = null)
        {
            this.address = address;
            this.name = string.IsNullOrWhiteSpace(name) ? "Jugador" : name.Trim();
            this.code = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
            try
            {
                Net.ParseAddress(address, out string host, out int port);
                tcp = new TcpClient();
                connectTask = tcp.ConnectAsync(host, port);
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
                    Fail(code != null ? "No se pudo conectar con el servidor " + address : "No se pudo conectar a " + address);
                    return;
                }
                conn = new NetConn(tcp);
                if (code != null)
                {
                    // Primero se pide entrar a la sala; el saludo al anfitrion va despues.
                    var ms = new MemoryStream();
                    var w = new BinaryWriter(ms);
                    w.Write((byte)2);
                    w.Write(Net.Protocol);
                    w.Write(code);
                    conn.Send(ms);
                    waitingRelay = true;
                    Status = "Buscando la sala " + code + "...";
                }
                else SendHello();
            }

            foreach (var m in conn.Poll())
            {
                var r = new BinaryReader(new MemoryStream(m));
                if (waitingRelay)
                {
                    if (m.Length > 0 && m[0] == 2)
                    {
                        waitingRelay = false;
                        SendHello();
                    }
                    else
                    {
                        r.ReadByte();
                        Fail(m.Length > 1 ? r.ReadString() : "El servidor rechazó la conexión");
                        return;
                    }
                    continue;
                }
                switch ((Msg)r.ReadByte())
                {
                    case Msg.Welcome:
                        MyId = r.ReadInt32();
                        Status = code != null ? "En la sala " + code : "Conectado a " + address;
                        break;
                    case Msg.Reject:
                        Fail(r.ReadString());
                        return;
                    case Msg.Snapshot:
                        NetSnapshot.Apply(r, this);
                        break;
                    case Msg.Pong:
                    {
                        int ms = Mathf.RoundToInt((Time.realtimeSinceStartup - r.ReadSingle()) * 1000f);
                        Ping = Ping < 0 ? ms : Mathf.RoundToInt(Mathf.Lerp(Ping, ms, 0.25f));
                        break;
                    }
                }
            }
            if (conn.Dead)
            {
                Fail("Se perdió la conexión con el anfitrión");
                return;
            }

            SendInput();
            SendPing();
            conn.Flush();
        }

        void SendPing()
        {
            if (MyId <= 0) return;
            pingTimer -= Time.unscaledDeltaTime;
            if (pingTimer > 0f) return;
            pingTimer = 0.5f;
            var ms = new MemoryStream(8);
            var w = new BinaryWriter(ms);
            w.Write((byte)Msg.Ping);
            w.Write(Time.realtimeSinceStartup);
            w.Write((ushort)Mathf.Clamp(Ping, 0, 65535));
            conn.Send(ms);
        }

        void SendHello()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((byte)Msg.Hello);
            w.Write(Net.Protocol);
            w.Write(Net.GameVersion);
            w.Write(name);
            conn.Send(ms);
            Status = "Conectado, esperando al anfitrión...";
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
            if (me != null)
            {
                me.Cur = s;
                // El personaje propio se adelanta media latencia (mas reactivo), con tope para no pasarse.
                var sm = me.GetComponent<NetSmooth>();
                if (sm != null) sm.Lead = Mathf.Clamp(Ping, 0, 160) / 2000f;
            }

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
