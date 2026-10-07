using System.IO;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Fachada del modo online (anfitrion con autoridad):
    ///  - Offline: todo como siempre.
    ///  - Host:    simula todo y reenvia a los clientes el estado del mundo y los EVENTOS (sonidos, efectos,
    ///             textos, explosiones sobre las capas del cuerpo, cortes de extremidades).
    ///  - Client:  no simula nada; reproduce el estado recibido. Los efectos/sonidos/textos solo se
    ///             ejecutan cuando vienen como evento del anfitrion (asi no se duplican).
    /// </summary>
    public static class Net
    {
        public enum Mode { Offline, Host, Client }

        public const int Port = 7777;
        public const int Protocol = 1;
        public const string GameVersion = "0.2.0";

        /// <summary>
        /// Servidor de rele para las salas online (dominio o IP del VPS, opcionalmente con ":puerto").
        /// Ponlo aqui antes de compilar el .exe para que tus amigos no tengan que escribirlo.
        /// Cada jugador puede cambiarlo en el panel ONLINE.
        /// </summary>
        public const string DefaultServer = "https://dokploy.aayin.dev";

        public static Mode Current = Mode.Offline;
        public static bool IsHost => Current == Mode.Host;
        public static bool IsClient => Current == Mode.Client;
        public static bool IsOnline => Current != Mode.Offline;

        /// <summary>En el cliente, true mientras se reproduce un evento del anfitrion.</summary>
        public static bool Playing;

        /// <summary>Se pueden lanzar efectos/sonidos/textos aqui (en el cliente solo desde eventos).</summary>
        public static bool FxAllowed => !IsClient || Playing;

        public static NetHost Host;
        public static NetClient Client;

        static int nextId = 1;
        static int seedCounter = 12345;

        /// <summary>Identificador de red para entidades (personajes, bombas, cajas, TNT).</summary>
        public static int NewId() => nextId++;

        /// <summary>Semilla compartida para que el dano por trozos sea igual en anfitrion y clientes.</summary>
        public static int NextSeed() => seedCounter++;

        public static void Tick()
        {
            if (Host != null) Host.Tick();
            if (Client != null) Client.Tick();
        }

        /// <summary>Crea partida: en red local (server = null) o como sala online en el servidor de rele.</summary>
        public static void StartHost(string server = null)
        {
            Stop();
            Host = new NetHost();
            if (server == null ? Host.Start() : Host.StartRelay(server)) Current = Mode.Host;
            else Host = null;
        }

        /// <summary>Se une por IP (code = null) o a una sala online con su codigo.</summary>
        public static void StartClient(string address, string name, string code = null)
        {
            Stop();
            Client = new NetClient(address, name, code);
            Current = Mode.Client;
        }

        /// <summary>"host", "host:puerto" o "[ipv6]:puerto".</summary>
        public static void ParseAddress(string s, out string host, out int port)
        {
            s = (s ?? "").Trim();
            host = s;
            port = Port;
            int c = s.LastIndexOf(':');
            if (c > 0 && s.IndexOf(':') == c || s.StartsWith("[") && c > s.IndexOf(']'))
            {
                if (int.TryParse(s.Substring(c + 1), out int p)) port = p;
                host = s.Substring(0, c).Trim('[', ']');
            }
        }

        /// <summary>Un codigo de sala: 4-6 letras/numeros, sin puntos ni dos puntos.</summary>
        public static bool LooksLikeCode(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length < 4 || s.Length > 6) return false;
            foreach (char ch in s)
                if (!char.IsLetterOrDigit(ch)) return false;
            return true;
        }

        public static void Stop()
        {
            if (Host != null) Host.Stop();
            if (Client != null) Client.Stop();
            Host = null;
            Client = null;
            Current = Mode.Offline;
            Playing = false;
        }

        // ------------------------------------------------------------------ Eventos (anfitrion -> clientes)

        public enum Ev : byte
        {
            Sound = 1, Explosion, Scorch, PunchFlash, Sparkle, Poof, SpawnFlash, Shatter, Confetti,
            Shake, Popup, Feed, Center, BlastLayers, Limb,
        }

        static readonly MemoryStream evStream = new MemoryStream();
        static readonly BinaryWriter evW = new BinaryWriter(evStream);
        static int evCount;

        /// <summary>Empieza un evento (solo en el anfitrion). Devuelve el escritor para sus datos, o null.</summary>
        static BinaryWriter Begin(Ev e)
        {
            if (!IsHost) return null;
            evW.Write((byte)e);
            evCount++;
            return evW;
        }

        /// <summary>Saca los eventos acumulados desde el ultimo envio.</summary>
        public static byte[] TakeEvents(out int count)
        {
            count = evCount;
            var data = evStream.ToArray();
            evStream.SetLength(0);
            evCount = 0;
            return data;
        }

        public static void EvSound(short clip, float vol, float pitch, float pan)
        {
            var w = Begin(Ev.Sound);
            if (w == null) return;
            w.Write(clip); w.Write(vol); w.Write(pitch); w.Write(pan);
        }

        public static void EvExplosion(Vector3 p, float radius, bool ice)
        {
            var w = Begin(Ev.Explosion);
            if (w == null) return;
            w.WriteV(p); w.Write(radius); w.Write(ice);
        }

        public static void EvScorch(Vector3 p, Vector3 n, float radius, bool ice)
        {
            var w = Begin(Ev.Scorch);
            if (w == null) return;
            w.WriteV(p); w.WriteV(n); w.Write(radius); w.Write(ice);
        }

        public static void EvPosFloat(Ev e, Vector3 p, float f)
        {
            var w = Begin(e);
            if (w == null) return;
            w.WriteV(p); w.Write(f);
        }

        public static void EvPosColor(Ev e, Vector3 p, Color c)
        {
            var w = Begin(e);
            if (w == null) return;
            w.WriteV(p); w.WriteC(c);
        }

        public static void EvPos(Ev e, Vector3 p)
        {
            var w = Begin(e);
            if (w == null) return;
            w.WriteV(p);
        }

        public static void EvShake(float a)
        {
            var w = Begin(Ev.Shake);
            if (w == null) return;
            w.Write(a);
        }

        public static void EvPopup(Vector3 p, string text, Color c, int size)
        {
            var w = Begin(Ev.Popup);
            if (w == null) return;
            w.WriteV(p); w.Write(text ?? ""); w.WriteC(c); w.Write((short)size);
        }

        public static void EvFeed(string line)
        {
            var w = Begin(Ev.Feed);
            if (w == null) return;
            w.Write(line ?? "");
        }

        public static void EvCenter(string big, string sub, float seconds)
        {
            var w = Begin(Ev.Center);
            if (w == null) return;
            w.Write(big ?? ""); w.Write(sub ?? ""); w.Write(seconds);
        }

        public static void EvBlastLayers(int charId, Vector3 p, float strength, float radius, int seed)
        {
            var w = Begin(Ev.BlastLayers);
            if (w == null) return;
            w.Write(charId); w.WriteV(p); w.Write(strength); w.Write(radius); w.Write(seed);
        }

        public static void EvLimb(int charId, bool arm, int side, bool lower, Vector3 blast)
        {
            var w = Begin(Ev.Limb);
            if (w == null) return;
            w.Write(charId); w.Write(arm); w.Write((byte)side); w.Write(lower); w.WriteV(blast);
        }

        /// <summary>Reproduce los eventos recibidos (en el cliente).</summary>
        public static void PlayEvents(BinaryReader r, int count, System.Func<int, LBCharacter> findChar)
        {
            for (int i = 0; i < count; i++)
            {
                var e = (Ev)r.ReadByte();
                Playing = true;
                try
                {
                    switch (e)
                    {
                        case Ev.Sound:
                        {
                            short clip = r.ReadInt16();
                            float vol = r.ReadSingle(), pitch = r.ReadSingle(), pan = r.ReadSingle();
                            Sfx.Play(Sfx.ClipById(clip), vol, pitch, pan);
                            break;
                        }
                        case Ev.Explosion: FX.Explosion(r.ReadV(), r.ReadSingle(), r.ReadBoolean()); break;
                        case Ev.Scorch: FX.Scorch(r.ReadV(), r.ReadV(), r.ReadSingle(), r.ReadBoolean()); break;
                        case Ev.PunchFlash: FX.PunchFlash(r.ReadV(), r.ReadSingle()); break;
                        case Ev.Sparkle: FX.Sparkle(r.ReadV(), r.ReadC()); break;
                        case Ev.Poof: FX.Poof(r.ReadV(), r.ReadC()); break;
                        case Ev.SpawnFlash: FX.SpawnFlash(r.ReadV(), r.ReadC()); break;
                        case Ev.Shatter: FX.Shatter(r.ReadV()); break;
                        case Ev.Confetti: FX.Confetti(r.ReadV()); break;
                        case Ev.Shake: CameraRig.Shake(r.ReadSingle()); break;
                        case Ev.Popup:
                        {
                            Vector3 p = r.ReadV();
                            string text = r.ReadString();
                            Color c = r.ReadC();
                            HUD.Popup(p, text, c, r.ReadInt16());
                            break;
                        }
                        case Ev.Feed: HUD.Feed(r.ReadString()); break;
                        case Ev.Center:
                        {
                            string big = r.ReadString(), sub = r.ReadString();
                            HUD.Center(big, sub, r.ReadSingle());
                            break;
                        }
                        case Ev.BlastLayers:
                        {
                            int id = r.ReadInt32();
                            Vector3 p = r.ReadV();
                            float strength = r.ReadSingle(), radius = r.ReadSingle();
                            int seed = r.ReadInt32();
                            var ch = findChar(id);
                            if (ch != null && ch.Visual != null)
                            {
                                // Lo cosmetico local (trozos que saltan) si; sus sonidos ya llegan del anfitrion.
                                Playing = false;
                                var st = Random.state;
                                Random.InitState(seed);
                                ch.Visual.ApplyBlastLayers(p, strength, radius);
                                Random.state = st;
                            }
                            break;
                        }
                        case Ev.Limb:
                        {
                            int id = r.ReadInt32();
                            bool arm = r.ReadBoolean();
                            int side = r.ReadByte();
                            bool lower = r.ReadBoolean();
                            Vector3 blast = r.ReadV();
                            var ch = findChar(id);
                            if (ch != null && ch.Visual != null)
                            {
                                Playing = false;
                                ch.Visual.SeverNet(arm, side, lower, blast);
                            }
                            break;
                        }
                    }
                }
                finally
                {
                    Playing = false;
                }
            }
        }
    }

    /// <summary>Lectura/escritura de tipos de Unity en binario.</summary>
    public static class NetIO
    {
        public static void WriteV(this BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 ReadV(this BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        public static void WriteQ(this BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        public static Quaternion ReadQ(this BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        public static void WriteC(this BinaryWriter w, Color c) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
        public static Color ReadC(this BinaryReader r) => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        public static void WriteU8(this BinaryWriter w, float v01) { w.Write((byte)Mathf.Clamp(Mathf.RoundToInt(v01 * 254f), 0, 254)); }
        public static float ReadU8(this BinaryReader r) => r.ReadByte() / 254f;
    }
}
