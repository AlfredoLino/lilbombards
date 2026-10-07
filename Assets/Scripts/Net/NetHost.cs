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
        Welcome = 10,   // anfitrion -> cliente: tu id
        Reject = 11,    // anfitrion -> cliente: motivo
        Snapshot = 12,  // anfitrion -> cliente: estado del mundo + eventos
    }

    /// <summary>Conexion TCP con mensajes delimitados por longitud (sin hilos: se sondea cada frame).</summary>
    public class NetConn
    {
        public readonly TcpClient Tcp;
        readonly NetworkStream stream;
        byte[] buf = new byte[1 << 16];
        int len;
        public bool Dead;

        public NetConn(TcpClient tcp)
        {
            Tcp = tcp;
            Tcp.NoDelay = true;
            stream = tcp.GetStream();
            stream.WriteTimeout = 3000;
        }

        public void Send(byte[] payload, int count)
        {
            if (Dead) return;
            try
            {
                var frame = new byte[count + 4];
                frame[0] = (byte)count;
                frame[1] = (byte)(count >> 8);
                frame[2] = (byte)(count >> 16);
                frame[3] = (byte)(count >> 24);
                Buffer.BlockCopy(payload, 0, frame, 4, count);
                stream.Write(frame, 0, frame.Length);
            }
            catch (Exception)
            {
                Dead = true;
            }
        }

        public void Send(MemoryStream ms) => Send(ms.GetBuffer(), (int)ms.Length);

        /// <summary>Lee lo disponible y devuelve los mensajes completos.</summary>
        public List<byte[]> Poll()
        {
            var msgs = new List<byte[]>();
            if (Dead) return msgs;
            try
            {
                if (!Tcp.Connected) { Dead = true; return msgs; }
                while (Tcp.Available > 0)
                {
                    if (buf.Length - len < Tcp.Available) Array.Resize(ref buf, Math.Max(buf.Length * 2, len + Tcp.Available));
                    int n = stream.Read(buf, len, buf.Length - len);
                    if (n <= 0) { Dead = true; break; }
                    len += n;
                }
                // Detectar cierre por el otro lado.
                if (Tcp.Client.Poll(0, SelectMode.SelectRead) && Tcp.Available == 0) Dead = true;
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
            Dead = true;
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
            public NetConn Conn;
            public int Id;
            public string Name = "?";
            public readonly RemoteInput Input = new RemoteInput();
            public bool Joined;
        }

        TcpListener listener;
        public readonly List<Peer> Peers = new List<Peer>();
        int nextPeerId = 1;
        float snapTimer;
        public string Status = "";

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
        }

        public void Tick()
        {
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
            snapTimer -= Time.unscaledDeltaTime;
            if (snapTimer <= 0f)
            {
                snapTimer = 1f / 30f;
                var ms = new MemoryStream(4096);
                var w = new BinaryWriter(ms);
                w.Write((byte)Msg.Snapshot);
                NetSnapshot.Write(w, this);
                foreach (var p in Peers)
                    if (p.Joined) p.Conn.Send(ms);
            }
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
        public static string LocalAddresses()
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
