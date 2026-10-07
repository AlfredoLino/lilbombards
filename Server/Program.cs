// Lil Bombards - servidor de relé.
//
// No ejecuta el juego: solo crea "salas" con un código y reenvía los mensajes entre el anfitrión
// (un jugador) y sus clientes. Así nadie tiene que abrir puertos ni usar VPN.
//
// Todos los mensajes van con el mismo formato que el juego: [int32 longitud][datos].
//
// Primer mensaje de cada conexión:
//   anfitrión -> relé  [1][int32 protocolo]           relé -> anfitrión  [1][string código]
//   cliente   -> relé  [2][int32 protocolo][string código]
//                                                     relé -> cliente    [2] ok  |  [3][string motivo]
// Después:
//   relé -> anfitrión  [10][int32 peer] se unió   [11][int32 peer] se fue   [12][int32 peer][datos] del peer
//   anfitrión -> relé  [20][int32 peer][datos] al peer   [21][int32 peer] expulsar   [30] latido
//   cliente <-> relé   los datos del juego tal cual

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace LB.Relay;

static class Program
{
    const int MaxFrame = 4 * 1024 * 1024;
    const int MaxRooms = 500;
    const int MaxPeersPerRoom = 16;
    static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(45);
    const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sin I, O, 0, 1

    static readonly ConcurrentDictionary<string, Room> rooms = new();
    static int connections;

    static async Task Main()
    {
        int port = int.TryParse(Environment.GetEnvironmentVariable("RELAY_PORT"), out var p) ? p : 7777;
        var listener = new TcpListener(IPAddress.IPv6Any, port);
        listener.Server.DualMode = true;
        listener.Start();
        Log($"Relé de Lil Bombards escuchando en el puerto {port}");

        while (true)
        {
            var tcp = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => Handle(tcp));
        }
    }

    static void Log(string s) => Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {s}");

    static async Task Handle(TcpClient tcp)
    {
        var conn = new Conn(tcp);
        Interlocked.Increment(ref connections);
        try
        {
            if (connections > MaxRooms * 4) return;
            var first = await conn.Read(HelloTimeout);
            if (first == null || first.Length < 5) return;
            var r = new BinaryReader(new MemoryStream(first));
            byte op = r.ReadByte();
            int protocol = r.ReadInt32();
            if (op == 1) await RunHost(conn, protocol);
            else if (op == 2) await RunClient(conn, protocol, r.ReadString());
        }
        catch (Exception) { }
        finally
        {
            conn.Close();
            Interlocked.Decrement(ref connections);
        }
    }

    static async Task RunHost(Conn host, int protocol)
    {
        if (rooms.Count >= MaxRooms)
            return;
        var room = new Room { Host = host, Protocol = protocol };
        do room.Code = NewCode(); while (!rooms.TryAdd(room.Code, room));
        Log($"Sala {room.Code} creada por {host.Address} (salas: {rooms.Count})");
        host.Send(Frame(w => { w.Write((byte)1); w.Write(room.Code); }));

        try
        {
            while (true)
            {
                var m = await host.Read(IdleTimeout);
                if (m == null || m.Length == 0) break;
                switch (m[0])
                {
                    case 20 when m.Length >= 5:
                    {
                        var peer = room.Get(BitConverter.ToInt32(m, 1));
                        if (peer == null) break;
                        var data = new byte[m.Length - 5];
                        Buffer.BlockCopy(m, 5, data, 0, data.Length);
                        peer.Send(data);
                        break;
                    }
                    case 21 when m.Length >= 5:
                    {
                        // Expulsar: primero se entrega lo pendiente (el motivo del rechazo) y luego se corta.
                        var kicked = room.Get(BitConverter.ToInt32(m, 1));
                        if (kicked != null) _ = Task.Run(async () => { await kicked.Flush(); kicked.Close(); });
                        break;
                    }
                    // 30 = latido: no hace nada, solo mantiene viva la conexión.
                }
            }
        }
        finally
        {
            rooms.TryRemove(room.Code, out _);
            foreach (var p in room.All()) p.Close();
            Log($"Sala {room.Code} cerrada (salas: {rooms.Count})");
        }
    }

    static async Task RunClient(Conn client, int protocol, string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!rooms.TryGetValue(code, out var room))
        {
            client.Send(Frame(w => { w.Write((byte)3); w.Write("No existe la sala " + code); }));
            await client.Flush();
            return;
        }
        if (room.Protocol != protocol)
        {
            client.Send(Frame(w => { w.Write((byte)3); w.Write("Versión del juego distinta a la del anfitrión"); }));
            await client.Flush();
            return;
        }
        int id = room.Add(client);
        if (id < 0)
        {
            client.Send(Frame(w => { w.Write((byte)3); w.Write("La sala está llena"); }));
            await client.Flush();
            return;
        }
        client.Send(new byte[] { 2 });
        room.Host.Send(Frame(w => { w.Write((byte)10); w.Write(id); }));

        try
        {
            while (true)
            {
                var m = await client.Read(IdleTimeout);
                if (m == null) break;
                var f = new byte[m.Length + 5];
                f[0] = 12;
                BitConverter.TryWriteBytes(new Span<byte>(f, 1, 4), id);
                Buffer.BlockCopy(m, 0, f, 5, m.Length);
                room.Host.Send(f);
            }
        }
        finally
        {
            room.Remove(id);
            room.Host.Send(Frame(w => { w.Write((byte)11); w.Write(id); }));
        }
    }

    static string NewCode()
    {
        Span<char> c = stackalloc char[4];
        for (int i = 0; i < c.Length; i++) c[i] = CodeChars[Random.Shared.Next(CodeChars.Length)];
        return new string(c);
    }

    static byte[] Frame(Action<BinaryWriter> write)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) write(w);
        return ms.ToArray();
    }

    sealed class Room
    {
        public string Code = "";
        public int Protocol;
        public Conn Host = null!;
        readonly Dictionary<int, Conn> peers = new();
        int nextId = 1;

        public int Add(Conn c)
        {
            lock (peers)
            {
                if (peers.Count >= MaxPeersPerRoom) return -1;
                int id = nextId++;
                peers[id] = c;
                return id;
            }
        }

        public void Remove(int id) { lock (peers) peers.Remove(id); }
        public Conn? Get(int id) { lock (peers) return peers.TryGetValue(id, out var c) ? c : null; }
        public List<Conn> All() { lock (peers) return new List<Conn>(peers.Values); }
    }

    /// <summary>Conexión con cola de envío propia: un cliente lento no frena al resto.</summary>
    sealed class Conn
    {
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly Channel<byte[]> outq = Channel.CreateBounded<byte[]>(256);
        readonly CancellationTokenSource cts = new();
        readonly Task writer;
        public readonly string Address;

        public Conn(TcpClient tcp)
        {
            this.tcp = tcp;
            tcp.NoDelay = true;
            stream = tcp.GetStream();
            Address = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
            writer = Task.Run(WriteLoop);
        }

        /// <summary>Encola un mensaje. Si la cola está llena (conexión atascada) se corta.</summary>
        public void Send(byte[] payload)
        {
            if (!outq.Writer.TryWrite(payload)) Close();
        }

        public async Task Flush()
        {
            outq.Writer.TryComplete();
            try { await writer.WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        }

        async Task WriteLoop()
        {
            try
            {
                await foreach (var p in outq.Reader.ReadAllAsync(cts.Token))
                {
                    var f = new byte[p.Length + 4];
                    BitConverter.TryWriteBytes(new Span<byte>(f, 0, 4), p.Length);
                    Buffer.BlockCopy(p, 0, f, 4, p.Length);
                    await stream.WriteAsync(f, cts.Token);
                }
            }
            catch (Exception) { Close(); }
        }

        /// <summary>Lee un mensaje completo; null si se cerró o pasó el tiempo.</summary>
        public async Task<byte[]?> Read(TimeSpan timeout)
        {
            using var t = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            t.CancelAfter(timeout);
            try
            {
                var head = new byte[4];
                await stream.ReadExactlyAsync(head, t.Token);
                int size = BitConverter.ToInt32(head, 0);
                if (size < 0 || size > MaxFrame) return null;
                var data = new byte[size];
                await stream.ReadExactlyAsync(data, t.Token);
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Close()
        {
            if (cts.IsCancellationRequested) return;
            cts.Cancel();
            outq.Writer.TryComplete();
            try { tcp.Close(); } catch (Exception) { }
        }
    }
}
