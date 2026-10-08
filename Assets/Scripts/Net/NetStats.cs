using System.Text;
using UnityEngine;

namespace LB
{
    /// <summary>
    /// Ping en tiempo real (esquina inferior derecha) durante las partidas online.
    /// Cliente: su ping con el anfitrion. Anfitrion: su ping con el servidor y el de cada jugador online.
    /// </summary>
    public class NetStats : MonoBehaviour
    {
        GUIStyle style, shadow;
        float lastScale;
        readonly StringBuilder sb = new StringBuilder();

        static string Colored(int ms)
        {
            if (ms < 0) return "<color=#AAAAAA>-- ms</color>";
            string c = ms < 80 ? "#7CFF6B" : ms < 150 ? "#FFD23F" : "#FF6A4D";
            return "<color=" + c + ">" + ms + " ms</color>";
        }

        void OnGUI()
        {
            if (!Net.IsOnline || GameManager.I == null) return;

            sb.Length = 0;
            if (Net.IsClient && Net.Client != null)
            {
                if (!Net.Client.Connected) return;
                sb.Append("Ping ").Append(Colored(Net.Client.Ping));
            }
            else if (Net.IsHost && Net.Host != null)
            {
                if (Net.Host.UsesRelay) sb.Append("Servidor ").Append(Colored(Net.Host.RelayPing));
                foreach (var p in Net.Host.Peers)
                {
                    if (!p.Joined) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(p.Name).Append(' ').Append(Colored(p.Ping > 0 ? p.Ping : -1));
                }
                if (sb.Length == 0) return;
            }
            else return;

            float s = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            if (style == null || !Mathf.Approximately(s, lastScale))
            {
                lastScale = s;
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(17 * s),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.LowerRight,
                    richText = true,
                };
                style.normal.textColor = Color.white;
                shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            }

            string text = sb.ToString();
            float w = 260f * s, h = 200f * s, m = 8f * s;
            var r = new Rect(Screen.width - w - m, Screen.height - h - m, w, h);
            // Sombra (sin colores) para que se lea sobre cualquier fondo.
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, w, h), System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", ""), shadow);
            GUI.Label(r, text, style);
        }
    }
}
