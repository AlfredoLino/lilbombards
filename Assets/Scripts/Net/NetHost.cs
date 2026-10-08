using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace LB
{
    /// <summary>Tipos de mensaje del protocolo.</summary>
    public enum Msg : byte
    {
        Hello = 1,      // cliente -> anfitrion: protocolo, version, nombre
        Input = 2,      // cliente -> anfitrion: controles
        Ping = 3,       // cliente -> anfitrion: marca de tiempo + su ultimo ping medido
        Welcome = 10,   // anfitrion -> cliente: tu id
        Reject = 11,    // anfitrion -> cliente: motivo
        Snapshot = 12,  // anfitrion -> cliente: estado del mundo + eventos
        Pong = 13,      // anfitrion -> cliente: devuelve la marca de tiempo
    }

    /// <summary>Canal con un jugador remoto: conexion directa o a traves del servidor de rele.</summary>
    public interface IPeerLink
    {
        bool Dead { get; set; }
        /// <summary>Bytes pendientes de enviar (si la conexion va atascada, se saltan instantaneas).</summary>
        int Backlog { get; }
        List<byte[]> Poll();
        void Send(MemoryStream ms);
        void Close();
    }

    /// <summary>
    /// Conexion TCP con mensajes delimitados por longitud. Sin hilos y SIN BLOQUEAR: lo que no cabe en el
    /// socket se guarda y se envia en el siguiente frame, asi una conexion lenta nunca congela el juego.
    /// </summary>
    public class NetConn : IPeerLink
    {
        public readonly TcpClient Tcp;
        readonly Socket sock;
        byte[] buf = new byte[1 << 16];
        int len;
        byte[] outBuf = new byte[1 << 16];
        int outStart, outLen;
        const int MaxBacklog = 4 * 1024 * 1024;
        public bool Dead { get; set; }
        public int Backlog => outLen;

        public NetConn(TcpClient tcp)
        {
            Tcp = tcp;
            Tcp.NoDelay = true;
            sock = tcp.Client;
            sock.Blocking = false;
        }

        public void Send(byte[] payload, int count)
        {
            if (Dead) return;
            if (outLen + count + 4 > MaxBacklog)
            {
                Dead = true; // el otro lado no recibe nada desde hace mucho
                return;
            }
            // Compactar y crecer el bufer de salida si hace falta.
            if (outStart > 0 && outStart + outLen + count + 4 > outBuf.Length)
            {
                Buffer.BlockCopy(outBuf, outStart, outBuf, 0, outLen);
                outStart = 0;
            }
            if (outLen + count + 4 > outBuf.Length)
            {
                var nb = new byte[Math.Max(outBuf.Length * 2, outLen + count + 4)];
                Buffer.BlockCopy(outBuf, outStart, nb, 0, outLen);
                outBuf = nb;
                outStart = 0;
            }
            int p = outStart + outLen;
            outBuf[p] = (byte)count;
            outBuf[p + 1] = (byte)(count >> 8);
            outBuf[p + 2] = (byte)(count >> 16);
            outBuf[p + 3] = (byte)(count >> 24);
            Buffer.BlockCopy(payload, 0, outBuf, p + 4, count);
            outLen += count + 4;
            Flush();
        }

        public void Send(MemoryStream ms) => Send(ms.GetBuffer(), (int)ms.Length);

        /// <summary>Envia todo lo que el socket acepte ahora mismo, sin esperar.</summary>
        public void Flush()
        {
            if (Dead || outLen == 0) return;
            try
            {
                int n = sock.Send(outBuf, outStart, outLen, SocketFlags.None, out SocketError err);
                if (err == SocketError.WouldBlock || err == SocketError.NoBufferSpaceAvailable) n = 0;
                else if (err != SocketError.Success) { Dead = true; return; }
                outStart += n;
                outLen -= n;
                if (outLen == 0) outStart = 0;
            }
            catch (Exception)
            {
                Dead = true;
            }
        }

        /// <summary>Lee lo disponible y devuelve los mensajes completos.</summary>
        public List<byte[]> Poll()
        {
            var msgs = new List<byte[]>();
            if (Dead) return msgs;
            Flush();
            try
            {
                while (true)
                {
                    if (buf.Length - len < 4096) Array.Resize(ref buf, buf.Length * 2);
                    int n = sock.Receive(buf, len, buf.Length - len, SocketFlags.None, out SocketError err);
                    if (err == SocketError.WouldBlock) break;
                    if (err != SocketError.Success || n <= 0) { Dead = true; break; } // 0 = el otro lado cerro
                    len += n;
                }
            }
            catch (Exception)
            {
                Dead = true;
            }
            int pos = 0;
            while (len - pos >= 4)
            {
                int size = buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16) | (buf[pos + 3] << 24);
                if (size < 0 || size > 16 * 1024 * 1024) { Dead = true; break; }
                if (len - pos - 4 < size) break;
                var m = new byte[size];
                Buffer.BlockCopy(buf, pos + 4, m, 0, size);
                msgs.Add(m);
                pos += 4 + size;
            }
            if (pos > 0)
            {
                Buffer.BlockCopy(buf, pos, buf, 0, len - pos);
                len -= pos;
            }
            return msgs;
        }

        public void Close()
        {
            if (!Dead) Flush(); // lo ultimo (p. ej. el motivo de un rechazo)
            Dead = true;
            try { sock.Shutdown(SocketShutdown.Both); } catch (Exception) { }
            try { Tcp.Close(); } catch (Exception) { }
        }
    }

    /// <summary>Controles de un jugador remoto (lo ultimo recibido + pulsaciones que no se pierden).</summary>
    public class RemoteInput : IInputProvider
    {
        InputState latest;
        readonly byte[] counts = new byte[4];
        readonly byte[] seen = new byte[4];

        public void Apply(BinaryReader r)
        {
            latest.move = new Vector2(r.ReadSingle(), r.ReadSingle());
            byte f = r.ReadByte();
            latest.jump = (f & 1) != 0;
            latest.punch = (f & 2) != 0;
            latest.bomb = (f & 4) != 0;
            latest.pickup = (f & 8) != 0;
            latest.run = (f & 16) != 0;
            latest.clickBomb = (f & 32) != 0;
            latest.hasAim = (f & 64) != 0;
            latest.aimPoint = r.ReadV();
            for (int i = 0; i < 4; i++) counts[i] = r.ReadByte();
        }

        public InputState Read()
        {
            var s = latest;
            // Una pulsacion corta que empezo y acabo entre dos mensajes se convierte en un toque de un frame.
            if (counts[0] != seen[0]) { seen[0] = counts[0]; s.jump = true; }
            if (counts[1] != seen[1]) { seen[1] = counts[1]; s.punch = true; }
            if (counts[2] != seen[2]) { seen[2] = counts[2]; s.bomb = true; }
            if (counts[3] != seen[3]) { seen[3] = counts[3]; s.pickup = true; }
            return s;
        }
    }

    /// <summary>Anfitrion: acepta clientes, recibe sus controles y les envia el mundo ~30 veces por segundo.</summary>
    public class NetHost
    {
        public class Peer
        {
            public IPeerLink Conn;
            public int Id;
            public string Name = "?";
            public readonly RemoteInput Input = new RemoteInput();
            public bool Joined;
            /// <summary>Ping que mide el propio cliente (ms), para mostrarlo a todos.</summary>
            public int Ping;
        }

        TcpListener listener;
        public readonly List<Peer> Peers = new List<Peer>();
        int nextPeerId = 1;
        float snapTimer;
        public string Status = "";
        /// <summary>Instantaneas por segundo.</summary>
        public const float SnapshotRate = 30f;
        const int SkipBacklog = 24 * 1024;

        // Modo sala online (a traves del servidor de rele).
        TcpClient relayTcp;
        System.Threading.Tasks.Task relayConnect;
        NetConn relay;
        string relayHost;
        float beatTimer;
        /// <summary>Ping entre el anfitrion y el servidor de rele (ms), -1 si no se sabe.</summary>
        public int RelayPing = -1;
        /// <summary>Codigo de la sala online (null si es una partida en red local o aun no hay codigo).</summary>
        public string RoomCode;
        public bool UsesRelay => relayHost != null;

        /// <summary>Crea una sala online en el servidor de rele (host o host:puerto).</summary>
        public bool StartRelay(string server)
        {
            Net.ParseAddress(server, out relayHost, out int port);
            try
            {
                relayTcp = new TcpClient();
                relayConnect = relayTcp.ConnectAsync(relayHost, port);
                Status = "Conectando con el servidor...";
                return true;
            }
            catch (Exception e)
            {
                Status = "No se pudo conectar con el servidor: " + e.Message;
                NetMenu.Message = Status;
                return false;
            }
        }

        void Fail(string why)
        {
            NetMenu.Message = why;
            Net.Stop();
            if (GameManager.I != null) GameManager.I.OnNetStopped();
        }

        /// <summary>Atiende la conexion con el rele. Devuelve false si la sesion termino.</summary>
        bool TickRelay()
        {
            if (relay == null)
            {
                if (!relayConnect.IsCompleted) return true;
                if (relayConnect.IsFaulted || !relayTcp.Connected)
                {
                    Fail("No se pudo conectar con el servidor " + relayHost);
                    return false;
                }
                relay = new NetConn(relayTcp);
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                w.Write((byte)1);
                w.Write(Net.Protocol);
                relay.Send(ms);
            }

            foreach (var m in relay.Poll())
            {
                if (m.Length == 0) continue;
                var r = new BinaryReader(new MemoryStream(m));
                byte op = r.ReadByte();
                switch (op)
                {
                    case 1:
                        RoomCode = r.ReadString();
                        Status = "Sala online creada";
                        break;
                    case 10:
                    {
                        var rp = new RelayPeer(this, r.ReadInt32());
                        Peers.Add(new Peer { Conn = rp, Id = rp.Id });
                        break;
                    }
                    case 11:
                    {
                        int id = r.ReadInt32();
                        foreach (var p in Peers)
                            if (p.Conn is RelayPeer rp && rp.Id == id) rp.Left = true;
                        break;
                    }
                    case 31:
                    {
                        float sent = r.ReadSingle();
                        int ms = Mathf.RoundToInt((Time.realtimeSinceStartup - sent) * 1000f);
                        RelayPing = RelayPing < 0 ? ms : Mathf.RoundToInt(Mathf.Lerp(RelayPing, ms, 0.3f));
                        break;
                    }
                    case 12:
                    {
                        int id = r.ReadInt32();
                        var data = new byte[m.Length - 5];
                        Buffer.BlockCopy(m, 5, data, 0, data.Length);
                        foreach (var p in Peers)
                            if (p.Conn is RelayPeer rp && rp.Id == id) rp.Inbox.Add(data);
                        break;
                    }
                }
            }
            if (relay.Dead)
            {
                Fail("Se perdió la conexión con el servidor");
                return false;
            }

            beatTimer -= Time.unscaledDeltaTime;
            if (beatTimer <= 0f)
            {
                // Latido con eco (cada segundo): mantiene viva la conexion y mide el ping con el servidor.
                beatTimer = 1f;
                var b = new byte[5];
                b[0] = 31;
                BitConverter.GetBytes(Time.realtimeSinceStartup).CopyTo(b, 1);
                relay.Send(b, 5);
            }
            return true;
        }

        internal void RelaySend(int peer, byte[] payload, int count)
        {
            if (relay == null) return;
            var f = new byte[count + 5];
            f[0] = 20;
            f[1] = (byte)peer;
            f[2] = (byte)(peer >> 8);
            f[3] = (byte)(peer >> 16);
            f[4] = (byte)(peer >> 24);
            Buffer.BlockCopy(payload, 0, f, 5, count);
            relay.Send(f, f.Length);
        }

        /// <summary>Una sola copia para varios jugadores: el rele la reparte (ahorra subida al anfitrion).</summary>
        void RelayBroadcast(List<int> ids, MemoryStream ms)
        {
            if (relay == null || ids.Count == 0) return;
            int count = (int)ms.Length;
            var f = new byte[2 + ids.Count * 4 + count];
            f[0] = 22;
            f[1] = (byte)ids.Count;
            for (int i = 0; i < ids.Count; i++) BitConverter.GetBytes(ids[i]).CopyTo(f, 2 + i * 4);
            Buffer.BlockCopy(ms.GetBuffer(), 0, f, 2 + ids.Count * 4, count);
            relay.Send(f, f.Length);
        }

        internal void RelayKick(int peer)
        {
            if (relay == null) return;
            relay.Send(new byte[] { 21, (byte)peer, (byte)(peer >> 8), (byte)(peer >> 16), (byte)(peer >> 24) }, 5);
        }

        /// <summary>Jugador remoto que llega a traves del rele.</summary>
        class RelayPeer : IPeerLink
        {
            readonly NetHost host;
            public readonly int Id;
            public readonly List<byte[]> Inbox = new List<byte[]>();
            public bool Dead { get; set; }
            public int Backlog => host.relay != null ? host.relay.Backlog : 0;
            bool closed, left;

            /// <summary>El rele avisa de que este jugador se fue.</summary>
            public bool Left
            {
                set { left = value; Dead = true; }
            }

            public RelayPeer(NetHost host, int id)
            {
                this.host = host;
                Id = id;
            }

            public List<byte[]> Poll()
            {
                var l = new List<byte[]>(Inbox);
                Inbox.Clear();
                return l;
            }

            public void Send(MemoryStream ms)
            {
                if (!Dead) host.RelaySend(Id, ms.GetBuffer(), (int)ms.Length);
            }

            public void Close()
            {
                if (closed) return;
                closed = true;
                Dead = true;
                if (!left) host.RelayKick(Id); // el rele entrega antes lo pendiente (p. ej. el motivo del rechazo)
            }
        }

        public bool Start()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, Net.Port);
                listener.Start();
                Status = "Anfitrión en el puerto " + Net.Port;
                return true;
            }
            catch (Exception e)
            {
                Status = "No se pudo abrir el puerto " + Net.Port + ": " + e.Message;
                NetMenu.Message = Status;
                return false;
            }
        }

        public void Stop()
        {
            foreach (var p in Peers)
            {
                if (p.Joined && GameManager.I != null) GameManager.I.RemoveRemote(p);
                p.Conn.Close();
            }
            Peers.Clear();
            try { listener?.Stop(); } catch (Exception) { }
            relay?.Close();
            try { relayTcp?.Close(); } catch (Exception) { }
        }

        public void Tick()
        {
            if (UsesRelay && !TickRelay()) return;

            // Nuevas conexiones.
            try
            {
                while (listener != null && listener.Pending())
                {
                    var peer = new Peer { Conn = new NetConn(listener.AcceptTcpClient()), Id = nextPeerId++ };
                    Peers.Add(peer);
                }
            }
            catch (Exception) { }

            for (int i = Peers.Count - 1; i >= 0; i--)
            {
                var p = Peers[i];
                foreach (var m in p.Conn.Poll()) Handle(p, m);
                if (p.Conn.Dead)
                {
                    if (p.Joined && GameManager.I != null)
                    {
                        HUD.Feed(p.Name + " se desconectó");
                        GameManager.I.RemoveRemote(p);
                    }
                    p.Conn.Close();
                    Peers.RemoveAt(i);
                }
            }

            // Instantanea del mundo + eventos.
            // El temporizador se ACUMULA (antes se reiniciaba y a 60 FPS salian ~20 por segundo en vez de 30).
            snapTimer -= Time.unscaledDeltaTime;
            if (snapTimer <= 0f)
            {
                snapTimer = Mathf.Max(snapTimer + 1f / SnapshotRate, 0f);
                var ms = new MemoryStream(4096);
                var w = new BinaryWriter(ms);
                w.Write((byte)Msg.Snapshot);
                NetSnapshot.Write(w, this);
                var relayIds = new List<int>();
                foreach (var p in Peers)
                {
                    if (!p.Joined) continue;
                    // Conexion atascada: mejor saltarse esta instantanea (la siguiente ya trae todo) que acumular retraso.
                    if (p.Conn.Backlog > SkipBacklog) continue;
                    if (p.Conn is RelayPeer rp) relayIds.Add(rp.Id);
                    else p.Conn.Send(ms);
                }
                RelayBroadcast(relayIds, ms);
            }
            foreach (var p in Peers)
                if (p.Conn is NetConn nc) nc.Flush();
            relay?.Flush();
        }

        void Handle(Peer p, byte[] m)
        {
            var r = new BinaryReader(new MemoryStream(m));
            var type = (Msg)r.ReadByte();
            switch (type)
            {
                case Msg.Hello:
                {
                    int proto = r.ReadInt32();
                    string ver = r.ReadString();
                    p.Name = r.ReadString();
                    if (proto != Net.Protocol)
                    {
                        Reply(p, Msg.Reject, "Versión distinta (anfitrión " + Net.GameVersion + ", tú " + ver + ")");
                        p.Conn.Dead = true;
                        return;
                    }
                    if (GameManager.I == null || !GameManager.I.JoinRemote(p, p.Input, p.Name))
                    {
                        Reply(p, Msg.Reject, "La partida está llena");
                        p.Conn.Dead = true;
                        return;
                    }
                    p.Joined = true;
                    var ms = new MemoryStream();
                    var w = new BinaryWriter(ms);
                    w.Write((byte)Msg.Welcome);
                    w.Write(p.Id);
                    p.Conn.Send(ms);
                    HUD.Feed(p.Name + " se conectó");
                    break;
                }
                case Msg.Input:
                    if (p.Joined) p.Input.Apply(r);
                    break;
                case Msg.Ping:
                {
                    float stamp = r.ReadSingle();
                    p.Ping = r.ReadUInt16();
                    var ms = new MemoryStream(8);
                    var w = new BinaryWriter(ms);
                    w.Write((byte)Msg.Pong);
                    w.Write(stamp);
                    p.Conn.Send(ms);
                    break;
                }
            }
        }

        static void Reply(Peer p, Msg type, string text)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((byte)type);
            w.Write(text);
            p.Conn.Send(ms);
        }

        /// <summary>IPs de este equipo (para decirselas a los amigos).</summary>
        static string localCache;
        static float localTime = -99f;

        public static string LocalAddresses()
        {
            if (localCache != null && Time.unscaledTime - localTime < 10f) return localCache;
            localTime = Time.unscaledTime;
            return localCache = ReadLocalAddresses();
        }

        static string ReadLocalAddresses()
        {
            try
            {
                var list = new List<string>();
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
                return list.Count > 0 ? string.Join(", ", list) : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }
    }
}
