using UnityEngine;

namespace LB
{
    /// <summary>
    /// Panel online de la sala de espera (IMGUI): nombre, IP del anfitrion y botones para
    /// crear partida, unirse o desconectarse.
    /// </summary>
    public class NetMenu : MonoBehaviour
    {
        /// <summary>Ultimo aviso (errores de conexion, desconexiones...).</summary>
        public static string Message = "";

        string playerName;
        string address;
        GUIStyle box, label, small, button, field;
        float lastScale;

        void Awake()
        {
            playerName = PlayerPrefs.GetString("lb_name", "Jugador");
            address = PlayerPrefs.GetString("lb_host", "127.0.0.1");
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

            float w = 360f * s, h = (Net.IsOnline ? 210f : 290f) * s;
            var rect = new Rect(Screen.width - w - 20f * s, 20f * s, w, h);
            var e = Event.current;
            bool over = rect.Contains(e.mousePosition);
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
                GUI.SetNextControlName("lb_name");
                playerName = GUILayout.TextField(playerName, 16, field, GUILayout.Height(lh));
                GUILayout.Label("IP del anfitrión (para unirte)", small);
                GUI.SetNextControlName("lb_host");
                address = GUILayout.TextField(address, 64, field, GUILayout.Height(lh));
                GUILayout.Space(6f * s);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Crear partida", button, GUILayout.Height(lh + 6f * s)))
                {
                    Save();
                    Message = "";
                    Net.StartHost();
                }
                if (GUILayout.Button("Unirse", button, GUILayout.Height(lh + 6f * s)))
                {
                    Save();
                    Message = "";
                    Net.StartClient(address.Trim(), playerName);
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                string status = Net.IsHost ? Net.Host.Status + "\nJugadores online: " + Net.Host.Peers.Count +
                                             "\nTu IP: " + NetHost.LocalAddresses()
                                           : Net.Client.Status;
                GUILayout.Label(status, small);
                GUILayout.Space(6f * s);
                if (GUILayout.Button(Net.IsHost ? "Cerrar partida online" : "Desconectar", button, GUILayout.Height(lh + 6f * s)))
                {
                    Message = "";
                    Net.Stop();
                    GameManager.I.OnNetStopped();
                }
            }
            if (!string.IsNullOrEmpty(Message))
            {
                GUILayout.Space(4f * s);
                GUILayout.Label("<color=#FF8A6B>" + Message + "</color>", small);
            }
            GUILayout.EndArea();

            Keys.Blocked = GUIUtility.keyboardControl != 0;
        }

        void Save()
        {
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "Jugador";
            PlayerPrefs.SetString("lb_name", playerName.Trim());
            PlayerPrefs.SetString("lb_host", address.Trim());
            PlayerPrefs.Save();
            GUI.FocusControl(null);
        }
    }
}
