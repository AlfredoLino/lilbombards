using System.Collections.Generic;
using UnityEngine;

namespace LB
{
    public enum GamePhase { Lobby, Playing, Ended }

    /// <summary>
    /// Flujo del juego: sala de espera (unirse con teclado/mando, ajustar bots),
    /// partida "Todos contra todos" (Deathmatch) y pantalla de victoria.
    /// Construye el mundo entero por codigo: no hace falta configurar la escena.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;
        public static bool AllowControl => I != null && I.Phase != GamePhase.Lobby;

        public GamePhase Phase { get; private set; }
        public readonly List<PlayerSlot> Players = new List<PlayerSlot>();
        public int BotCount = 2;
        public int KillsToWin = Tuning.DefaultKillsToWin;
        /// <summary>Habilidad de los bots (0 = muy facil, 1 = dificil).</summary>
        public float BotSkill = 0.1f;

        readonly List<PlayerSlot> humans = new List<PlayerSlot>();
        readonly KeyboardInput kb0 = new KeyboardInput(0);
        readonly KeyboardInput kb1 = new KeyboardInput(1);
        readonly Dictionary<Vector3, float> tntRespawn = new Dictionary<Vector3, float>();
        float nextPowerup;
        float endTimer;
        float slowMoTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I == null) new GameObject("LilBombards").AddComponent<GameManager>();
        }

        void Awake()
        {
            if (I != null && I != this)
            {
                Destroy(gameObject);
                return;
            }
            I = this;

            Physics.gravity = Tuning.Gravity;
            Time.fixedDeltaTime = 1f / 60f;
            Application.targetFrameRate = 144;
            Application.runInBackground = true; // online: el anfitrion sigue aunque la ventana pierda el foco
            QualitySettings.vSyncCount = 1;

            // Apagar camaras/luces que traiga la escena: el juego crea las suyas.
            foreach (var cam in Camera.allCameras) cam.gameObject.SetActive(false);
            if (RenderSettings.sun != null) RenderSettings.sun.gameObject.SetActive(false);

            Sfx.Init(transform);
            Arena.Build();
            CameraRig.Create();
            HUD.Create();
            gameObject.AddComponent<NetMenu>();
            LBCharacter.Died += OnDied;
            EnterLobby();
        }

        void OnDestroy()
        {
            LBCharacter.Died -= OnDied;
            if (I == this) I = null;
        }

        void OnApplicationQuit() => Net.Stop();

        void Update()
        {
            Net.Tick();
            if (Net.IsClient)
            {
                UpdateClient();
                return;
            }
            switch (Phase)
            {
                case GamePhase.Lobby: UpdateLobby(); break;
                case GamePhase.Playing: UpdatePlaying(); break;
                case GamePhase.Ended: UpdateEnded(); break;
            }
        }

        // ------------------------------------------------------------------ Sala de espera

        void EnterLobby()
        {
            Phase = GamePhase.Lobby;
            Time.timeScale = 1f;
            ClearWorld();
            Players.Clear();
            HUD.ClearFeed();
            HUD.Center("", "", 0f);
            RefreshLobbyText();
        }

        void UpdateLobby()
        {
            TryJoins();

            int d = MenuInput.BotDelta();
            if (d != 0)
            {
                BotCount = Mathf.Clamp(BotCount + d, 0, 7);
                Sfx.Play(Sfx.Pickup, 0.5f);
            }
            int k = MenuInput.KillsDelta();
            if (k != 0)
            {
                KillsToWin = Mathf.Clamp(KillsToWin + k, 1, 20);
                Sfx.Play(Sfx.Pickup, 0.5f);
            }

            if (MenuInput.Start())
            {
                if (humans.Count == 0) Join(kb0, kb0, "Jugador 1");
                if (humans.Count + BotCount >= 1) // con 0 bots: modo practica
                {
                    StartMatch();
                    return; // no volver a escribir el texto de la sala
                }
                Sfx.Play(Sfx.Beep, 0.5f);
            }
            else if (MenuInput.Back())
            {
                if (humans.Exists(h => !(h.Device is NetHost.Peer)))
                {
                    humans.RemoveAll(h => !(h.Device is NetHost.Peer));
                    Sfx.Play(Sfx.Pop, 0.5f);
                }
                else
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }
            RefreshLobbyText();
        }

        void RefreshLobbyText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<size=110><color=#FFD23F>LIL</color> <color=#FF5A3C>BOMBARDS</color></size>");
            sb.AppendLine("<size=34><color=#BFE8FF>Todos contra todos | primero en llegar a " + KillsToWin + " eliminaciones</color></size>");
            sb.AppendLine();
            if (humans.Count == 0)
                sb.AppendLine("<color=#FFFFFF>¡Pulsa un botón de acción para unirte!</color>");
            foreach (var h in humans)
                sb.AppendLine("<color=#" + ColorUtility.ToHtmlStringRGB(h.Color) + ">- " + h.Name + "</color>  <size=26><color=#DDDDDD>(" + DeviceName(h) + ")</color></size>");
            sb.AppendLine();
            sb.AppendLine("Bots: <color=#FFD23F>" + BotCount + "</color>" +
                          (BotCount == 0 ? "  <color=#7CFF6B>(modo práctica: sin rivales)</color>" : "") +
                          "   <size=26>( - / + | LB / RB )</size>");
            sb.AppendLine("Eliminaciones para ganar: <color=#FFD23F>" + KillsToWin + "</color>   <size=26>( RePág / AvPág | cruceta )</size>");
            sb.AppendLine();
            sb.AppendLine("<color=#7CFF6B>ENTER / START para empezar</color>   <size=26>ESC para " + (humans.Count > 0 ? "vaciar la sala" : "salir") + "</size>");
            if (Net.IsHost)
                sb.AppendLine("<size=28><color=#7FD8FF>Online: anfitrión en " + NetHost.LocalAddresses() + "  (puerto " + Net.Port + ")</color></size>");
            string footer =
                "Teclado 1: " + kb0.Describe() + "\n" +
                "Teclado 2: " + kb1.Describe() + "\n" +
                "Mando: A saltar | X golpe | B bomba | Y agarrar | gatillos correr      (correr, saltar, golpear y lanzar gastan estamina)";
            HUD.Lobby(sb.ToString(), footer);
        }

        string DeviceName(PlayerSlot s)
        {
            if (s.Device == (object)kb0) return "Teclado 1";
            if (s.Device == (object)kb1) return "Teclado 2";
            if (s.Device is NetHost.Peer) return "Online";
            return "Mando";
        }

        bool DeviceTaken(object dev)
        {
            foreach (var h in humans) if (h.Device == dev) return true;
            return false;
        }

        void TryJoins()
        {
            if (!DeviceTaken(kb0) && kb0.JoinPressed()) Join(kb0, kb0, null);
            if (!DeviceTaken(kb1) && kb1.JoinPressed()) Join(kb1, kb1, null);
            foreach (var pad in GamepadInput.All())
            {
                if (DeviceTaken(pad)) continue;
#if ENABLE_INPUT_SYSTEM
                if (GamepadInput.AnyButtonDown(pad)) Join(new GamepadInput(pad), pad, null);
#endif
            }
        }

        PlayerSlot Join(IInputProvider input, object device, string name)
        {
            if (humans.Count >= 8) return null;
            var used = new HashSet<int>();
            foreach (var h in humans) used.Add(h.Index);
            int idx = 0;
            while (used.Contains(idx)) idx++;
            var slot = new PlayerSlot
            {
                Index = idx,
                Name = name ?? ("Jugador " + (idx + 1)),
                Color = PlayerSlot.Palette[idx % PlayerSlot.Palette.Length],
                Highlight = PlayerSlot.Highlights[idx % PlayerSlot.Highlights.Length],
                Input = input,
                Device = device,
            };
            humans.Add(slot);
            Sfx.Play(Sfx.Ding, 0.6f);

            // Unirse a mitad de partida tambien es posible.
            if (Phase == GamePhase.Playing)
            {
                Players.Add(slot);
                slot.RespawnAt = Time.time;
                HUD.Feed(slot.Tag + " se ha unido");
            }
            return slot;
        }

        // ------------------------------------------------------------------ Online

        /// <summary>Jugadores que ven los clientes: en la sala solo los humanos, en partida todos.</summary>
        public List<PlayerSlot> VisiblePlayers => Phase == GamePhase.Lobby ? humans : Players;

        /// <summary>Anfitrion: un jugador remoto pide entrar.</summary>
        public bool JoinRemote(object device, IInputProvider input, string name)
        {
            if (DeviceTaken(device)) return true;
            return Join(input, device, name) != null;
        }

        /// <summary>Anfitrion: un jugador remoto se fue.</summary>
        public void RemoveRemote(object device)
        {
            var s = humans.Find(h => h.Device == device);
            if (s == null) return;
            humans.Remove(s);
            Players.Remove(s);
            if (s.Character != null) Destroy(s.Character.gameObject);
            s.Character = null;
            HUD.Feed(s.Tag + " se ha desconectado");
            if (Phase == GamePhase.Lobby) RefreshLobbyText();
        }

        /// <summary>Se cerro la sesion online (por boton o por error): volver a la sala local.</summary>
        public void OnNetStopped()
        {
            humans.RemoveAll(h => h.Device is NetHost.Peer || h.Device is int);
            EnterLobby();
        }

        /// <summary>Cliente: fase, meta de eliminaciones y camara lenta dictadas por el anfitrion.</summary>
        public void ClientApplyState(GamePhase phase, int kills, float timeScale)
        {
            if (phase != Phase)
            {
                Phase = phase;
                if (phase != GamePhase.Lobby) HUD.Lobby("", "");
            }
            KillsToWin = kills;
            Time.timeScale = Mathf.Clamp(timeScale, 0.05f, 1f);
        }

        /// <summary>Cliente: lista de jugadores (marcadores y paneles de dano).</summary>
        public void ClientSetPlayers(List<PlayerSlot> list)
        {
            Players.Clear();
            Players.AddRange(list);
        }

        void UpdateClient()
        {
            if (Phase == GamePhase.Lobby) RefreshClientLobby();
            if (MenuInput.Back())
            {
                NetMenu.Message = "Desconectado";
                Net.Stop();
                OnNetStopped();
            }
        }

        void RefreshClientLobby()
        {
            var cl = Net.Client;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<size=110><color=#FFD23F>LIL</color> <color=#FF5A3C>BOMBARDS</color></size>");
            sb.AppendLine("<size=34><color=#7FD8FF>" + (cl != null ? cl.Status : "") + "</color></size>");
            sb.AppendLine();
            if (cl != null && cl.Connected)
            {
                sb.AppendLine("Jugadores en la sala:");
                foreach (var h in Players)
                {
                    bool me = h.Device is int id && id == cl.MyId;
                    sb.AppendLine("<color=#" + ColorUtility.ToHtmlStringRGB(h.Color) + ">- " + h.Name + "</color>" + (me ? "  <size=26><color=#7CFF6B>(tú)</color></size>" : ""));
                }
                sb.AppendLine();
                sb.AppendLine("Eliminaciones para ganar: <color=#FFD23F>" + KillsToWin + "</color>");
                sb.AppendLine();
                sb.AppendLine("<color=#7CFF6B>Esperando a que el anfitrión empiece la partida...</color>");
            }
            sb.AppendLine("<size=26>ESC para desconectarte</size>");
            HUD.Lobby(sb.ToString(), "Teclado: " + kb0.Describe() + "\nMando: A saltar | X golpe | B bomba | Y agarrar | gatillos correr");
        }

        // ------------------------------------------------------------------ Partida

        void StartMatch()
        {
            ClearWorld();
            Players.Clear();
            HUD.Lobby("", "");
            HUD.ClearFeed();

            foreach (var h in humans)
            {
                h.Kills = 0;
                h.Deaths = 0;
                h.Character = null;
                Players.Add(h);
            }
            var usedColors = new HashSet<int>();
            foreach (var h in humans) usedColors.Add(h.Index % PlayerSlot.Palette.Length);
            int ci = 0;
            for (int b = 0; b < BotCount; b++)
            {
                while (usedColors.Contains(ci % PlayerSlot.Palette.Length) && usedColors.Count < PlayerSlot.Palette.Length) ci++;
                int c = ci % PlayerSlot.Palette.Length;
                usedColors.Add(c);
                var bot = new BotBrain(BotSkill);
                Players.Add(new PlayerSlot
                {
                    Index = 101 + b,
                    Name = PlayerSlot.BotNames[b % PlayerSlot.BotNames.Length],
                    Color = PlayerSlot.Palette[c],
                    Highlight = PlayerSlot.Highlights[c],
                    Input = bot,
                    IsBot = true,
                });
            }

            var spawns = new List<Vector3>(Arena.I.SpawnPoints);
            for (int i = 0; i < Players.Count; i++)
            {
                Vector3 p = spawns[i % spawns.Count];
                if (i >= spawns.Count) p += new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                SpawnSlot(Players[i], p);
            }

            foreach (var spot in Arena.I.TntSpots) TntBox.Create(spot);
            tntRespawn.Clear();
            for (int i = 0; i < 3; i++) SpawnPowerup();
            nextPowerup = Time.time + Tuning.PowerupInterval;

            Phase = GamePhase.Playing;
            if (Players.Count == 1)
                HUD.Center("Modo práctica", "Sin rivales: prueba las bombas con calma (ESC vuelve al menú)", 3f);
            else
                HUD.Center("¡Todos contra todos!", "Elimina a " + KillsToWin + " rivales para ganar", 2.5f);
            Sfx.Play(Sfx.Go, 0.7f);
        }

        void SpawnSlot(PlayerSlot s, Vector3 p)
        {
            float yaw = Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg;
            LBCharacter.Spawn(s, p, yaw);
            s.RespawnAt = -1f;
        }

        Vector3 BestSpawn()
        {
            Vector3 best = Arena.I.SpawnPoints[0];
            float bestScore = -1f;
            foreach (var sp in Arena.I.SpawnPoints)
            {
                float minD = 99f;
                foreach (var c in LBCharacter.All)
                    if (!c.Dead) minD = Mathf.Min(minD, Vector3.Distance(c.transform.position, sp));
                float score = minD + Random.Range(0f, 1.5f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = sp;
                }
            }
            return best;
        }

        void UpdatePlaying()
        {
            TryJoins();

            foreach (var s in Players)
            {
                if (s.Character == null && s.RespawnAt >= 0f && Time.time >= s.RespawnAt)
                    SpawnSlot(s, BestSpawn());
            }

            if (Time.time >= nextPowerup)
            {
                nextPowerup = Time.time + Tuning.PowerupInterval * Random.Range(0.8f, 1.2f);
                if (PowerupBox.All.Count < Tuning.MaxPowerups) SpawnPowerup();
            }

            var ready = new List<Vector3>();
            foreach (var kv in tntRespawn) if (Time.time >= kv.Value) ready.Add(kv.Key);
            foreach (var spot in ready)
            {
                tntRespawn.Remove(spot);
                TntBox.Create(spot);
            }

            if (MenuInput.Back()) EnterLobby();
        }

        void SpawnPowerup()
        {
            var pts = Arena.I.PowerupPoints;
            for (int tries = 0; tries < 6; tries++)
            {
                Vector3 p = pts[Random.Range(0, pts.Count)];
                bool free = true;
                foreach (var b in PowerupBox.All)
                    if (Vector3.Distance(Flat(b.transform.position), Flat(p)) < 1.2f) free = false;
                if (!free) continue;
                PowerupBox.Create(PowerupBox.RandomType(), p);
                return;
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public void OnTntGone(Vector3 home)
        {
            if (Phase == GamePhase.Playing) tntRespawn[home] = Time.time + Tuning.TntRespawn;
        }

        void OnDied(LBCharacter victim, PlayerSlot killer, HitKind kind)
        {
            if (Net.IsClient) return; // el marcador lo manda el anfitrion
            var vs = victim.Slot;
            if (vs == null) return;
            vs.Deaths++;
            if (Phase == GamePhase.Playing) vs.RespawnAt = Time.time + Tuning.RespawnDelay;

            if (killer != null && killer != vs)
            {
                if (Phase == GamePhase.Playing) killer.Kills++;
                string how = kind == HitKind.Fall ? " tiró a " : (kind == HitKind.Shatter ? " hizo añicos a " : " eliminó a ");
                HUD.Feed(killer.Tag + how + vs.Tag);
                if (killer.Character != null)
                    HUD.Popup(killer.Character.HeadPos + Vector3.up * 0.4f, "+1", Color.Lerp(killer.Color, Color.white, 0.4f), 56);
                if (Phase == GamePhase.Playing && killer.Kills >= KillsToWin) EndMatch(killer);
            }
            else
            {
                string how = kind == HitKind.Fall ? " se cayó" : (kind == HitKind.Curse ? " sucumbió a la maldición" : " se eliminó solo");
                HUD.Feed(vs.Tag + how);
            }
        }

        void EndMatch(PlayerSlot winner)
        {
            Phase = GamePhase.Ended;
            endTimer = 6f;
            slowMoTimer = 1.4f;
            Time.timeScale = 0.35f;
            HUD.Center("<color=#" + ColorUtility.ToHtmlStringRGB(winner.Color) + ">" + winner.Name + "</color> ¡GANA!", "", 5f);
            Sfx.Play(Sfx.Win, 0.8f);
            Vector3 p = winner.Character != null ? winner.Character.Center : Vector3.zero;
            FX.Confetti(p + Vector3.up * 2f);
        }

        void UpdateEnded()
        {
            float dt = Time.unscaledDeltaTime;
            if (slowMoTimer > 0f)
            {
                slowMoTimer -= dt;
                if (slowMoTimer <= 0f) Time.timeScale = 1f;
            }
            endTimer -= dt;
            if (endTimer <= 0f || (endTimer < 4f && MenuInput.Start())) EnterLobby();
        }

        public void ClearWorld()
        {
            foreach (var c in new List<LBCharacter>(LBCharacter.All)) Destroy(c.gameObject);
            foreach (var b in new List<Bomb>(Bomb.All)) Destroy(b.gameObject);
            foreach (var p in new List<PowerupBox>(PowerupBox.All)) Destroy(p.gameObject);
            foreach (var t in new List<TntBox>(TntBox.All)) Destroy(t.gameObject);
            foreach (var fx in new List<FxAnim>(FxAnim.All)) Destroy(fx.gameObject);
            DebrisFade.ClearAll(); // los trozos del cuerpo duran toda la partida; se limpian al empezar otra
            ScorchMark.ClearAll();
            SeveredPart.ClearAll(); // extremidades cortadas: duran toda la partida
            tntRespawn.Clear();
            foreach (var s in Players) s.Character = null;
        }
    }
}
