using UnityEngine;

namespace LB
{
    /// <summary>
    /// Panel online de la sala de espera (IMGUI): nombre, codigo de sala o IP, servidor, y botones para
    /// crear una sala online (servidor de rele), crear en red local, unirse o desconectarse.
    /// </summary>
    public class NetMenu : MonoBehaviour
    {
        /// <summary>Ultimo aviso (errores de conexion, desconexiones...).</summary>
        public static string Message = "";

        string playerName;
        string address;   // codigo de sala o IP del anfitrion
        string server;    // servidor de rele
        GUIStyle box, label, small, button, field, code;
        float lastScale;

        void Awake()
        {
            playerName = PlayerPrefs.GetString("lb_name", "Jugador");
            address = PlayerPrefs.GetString("lb_host", "");
            server = PlayerPrefs.GetString("lb_server", Net.DefaultServer);
            if (string.IsNullOrWhiteSpace(server)) server = Net.DefaultServer;
        }

        void OnDisable()
        {
            Keys.Blocked = false;
            Keys.MouseOverUI = false;
        }

        bool Visible => GameManager.I != null && (GameManager.I.Phase == GamePhase.Lobby || Net.IsClient && !Net.Client.Connected);

        void Styles(float s)
        {
            if (box != null && Mathf.Approximately(s, lastScale)) return;
            lastScale = s;
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.05f, 0.08f, 0.16f, 0.82f));
            bg.Apply();
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 12) };
            box.normal.background = bg;
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
            label.normal.textColor = new Color(1f, 0.82f, 0.25f);
            small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), wordWrap = true, richText = true };
            small.normal.textColor = new Color(0.85f, 0.92f, 1f);
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(17 * s), fontStyle = FontStyle.Bold };
            field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(17 * s), alignment = TextAnchor.MiddleLeft };
            code = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(44 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            code.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (!Visible)
            {
                Keys.Blocked = false;
                Keys.MouseOverUI = false;
                return;
            }
            float s = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            Styles(s);

            float w = 380f * s, h = (Net.IsOnline ? (Net.IsHost && Net.Host.UsesRelay ? 270f : 210f) : 420f) * s;
            var rect = new Rect(Screen.width - w - 20f * s, 20f * s, w, h);
            // Selector de mapa (arriba a la izquierda, solo en la sala).
            bool lobby = GameManager.I.Phase == GamePhase.Lobby;
            var mapRect = new Rect(20f * s, 20f * s, 420f * s, 118f * s);
            var e = Event.current;
            bool over = rect.Contains(e.mousePosition) || lobby && mapRect.Contains(e.mousePosition);
            if (e.type == EventType.Repaint) Keys.MouseOverUI = over;
            // Clic fuera del panel: deja de escribir.
            if (e.type == EventType.MouseDown && !over) GUI.FocusControl(null);
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) &&
                GUIUtility.keyboardControl != 0)
            {
                GUI.FocusControl(null);
                e.Use();
            }

            GUILayout.BeginArea(rect, box);
            GUILayout.Label("ONLINE  <size=" + Mathf.RoundToInt(13 * s) + "><color=#AAAAAA>v" + Net.GameVersion + "</color></size>", label);

            float lh = 30f * s;
            if (!Net.IsOnline)
            {
                GUILayout.Label("Tu nombre", small);
                playerName = GUILayout.TextField(playerName, 16, field, GUILayout.Height(lh));
                GUILayout.Space(6f * s);
                if (GUILayout.Button("Crear sala online", button, GUILayout.Height(lh + 6f * s)))
                {
                    Save();
                    if (string.IsNullOrWhiteSpace(server)) Message = "Escribe abajo la dirección del servidor";
                    else
                    {
                        Message = "";
                        Net.StartHost(server.Trim());
                    }
                }
                GUILayout.Label("Código de sala (o IP en red local)", small);
                GUILayout.BeginHorizontal();
                address = GUILayout.TextField(address, 64, field, GUILayout.Height(lh));
                if (GUILayout.Button("Unirse", button, GUILayout.Width(100f * s), GUILayout.Height(lh)))
                {
                    Save();
                    Join();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6f * s);
                if (GUILayout.Button("Crear en red local (LAN)", button, GUILayout.Height(lh)))
                {
                    Save();
                    Message = "";
                    Net.StartHost();
                }
                GUILayout.Space(4f * s);
                GUILayout.Label("Servidor online", small);
                server = GUILayout.TextField(server, 128, field, GUILayout.Height(lh));
            }
            else if (Net.IsHost && Net.Host.UsesRelay)
            {
                if (Net.Host.RoomCode != null)
                {
                    GUILayout.Label("Código de sala", small);
                    GUILayout.Label(Net.Host.RoomCode, code, GUILayout.Height(56f * s));
                    GUILayout.Label("Pásaselo a tus amigos. Jugadores online: " + Net.Host.Peers.Count, small);
                    if (GUILayout.Button("Copiar código", button, GUILayout.Height(lh)))
                        GUIUtility.systemCopyBuffer = Net.Host.RoomCode;
                }
                else GUILayout.Label(Net.Host.Status, small);
                GUILayout.Space(6f * s);
                if (GUILayout.Button("Cerrar sala", button, GUILayout.Height(lh + 6f * s))) Disconnect();
            }
            else
            {
                string status = Net.IsHost ? Net.Host.Status + "\nJugadores online: " + Net.Host.Peers.Count +
                                             "\nTu IP: " + NetHost.LocalAddresses()
                                           : Net.Client.Status;
                GUILayout.Label(status, small);
                GUILayout.Space(6f * s);
                if (GUILayout.Button(Net.IsHost ? "Cerrar partida" : "Desconectar", button, GUILayout.Height(lh + 6f * s))) Disconnect();
            }
            if (!string.IsNullOrEmpty(Message))
            {
                GUILayout.Space(4f * s);
                GUILayout.Label("<color=#FF8A6B>" + Message + "</color>", small);
            }
            GUILayout.EndArea();

            if (lobby) MapPanel(mapRect, s);

            Keys.Blocked = GUIUtility.keyboardControl != 0;
        }

        void MapPanel(Rect rect, float s)
        {
            bool canChange = !Net.IsClient;
            int map = Arena.I != null ? Arena.I.Map : 0;
            GUILayout.BeginArea(rect, box);
            GUILayout.Label("MAPA  <size=" + Mathf.RoundToInt(13 * s) + "><color=#AAAAAA>" + (map + 1) + "/" + Arena.MapCount +
                            (canChange ? "   TAB / cruceta izq./der." : "   (lo elige el anfitrión)") + "</color></size>", label);
            GUILayout.BeginHorizontal();
            float bh = 34f * s;
            if (canChange && GUILayout.Button("<", button, GUILayout.Width(44f * s), GUILayout.Height(bh))) GameManager.I.ChangeMap(-1);
            GUILayout.Label("<size=" + Mathf.RoundToInt(24 * s) + "><color=#FFFFFF>" + Arena.MapNames[map] + "</color></size>", small, GUILayout.Height(bh));
            if (canChange && GUILayout.Button(">", button, GUILayout.Width(44f * s), GUILayout.Height(bh))) GameManager.I.ChangeMap(1);
            GUILayout.EndHorizontal();
            GUILayout.Label(Arena.MapInfo[map], small);
            GUILayout.EndArea();
        }

        void Join()
        {
            string a = address.Trim();
            if (a.Length == 0)
            {
                Message = "Escribe el código de la sala o la IP del anfitrión";
                return;
            }
            Message = "";
            if (Net.LooksLikeCode(a))
            {
                if (string.IsNullOrWhiteSpace(server))
                {
                    Message = "Escribe abajo la dirección del servidor";
                    return;
                }
                Net.StartClient(server.Trim(), playerName, a);
            }
            else Net.StartClient(a, playerName);
        }

        static void Disconnect()
        {
            Message = "";
            Net.Stop();
            GameManager.I.OnNetStopped();
        }

        void Save()
        {
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "Jugador";
            PlayerPrefs.SetString("lb_name", playerName.Trim());
            PlayerPrefs.SetString("lb_host", address.Trim());
            PlayerPrefs.SetString("lb_server", server.Trim());
            PlayerPrefs.Save();
            GUI.FocusControl(null);
        }
    }
}
