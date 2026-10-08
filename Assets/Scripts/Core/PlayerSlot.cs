using UnityEngine;

namespace LB
{
    /// <summary>Un participante de la partida (humano o bot) y su marcador.</summary>
    public class PlayerSlot
    {
        public int Index;
        public string Name;
        public Color Color;
        public Color Highlight;
        public IInputProvider Input;
        public object Device;      // teclado (esquema) o mando, para no unirlo dos veces
        public bool IsBot;
        public int Kills;
        public int Deaths;
        public LBCharacter Character;
        public float RespawnAt = -1f;
        /// <summary>Ping online en ms (0 = local / anfitrion).</summary>
        public int Ping;

        public bool Alive => Character != null && !Character.Dead;

        public string Tag => "<color=#" + ColorUtility.ToHtmlStringRGB(Color) + ">" + Name + "</color>";

        public static readonly Color[] Palette =
        {
            new Color(1f, 0.24f, 0.2f),
            new Color(0.22f, 0.48f, 1f),
            new Color(0.28f, 0.86f, 0.32f),
            new Color(1f, 0.85f, 0.15f),
            new Color(1f, 0.55f, 0.12f),
            new Color(0.68f, 0.32f, 0.96f),
            new Color(1f, 0.45f, 0.75f),
            new Color(0.2f, 0.86f, 0.9f),
        };

        public static readonly Color[] Highlights =
        {
            new Color(1f, 0.9f, 0.3f),
            new Color(0.95f, 0.95f, 1f),
            new Color(0.15f, 0.35f, 0.15f),
            new Color(0.95f, 0.35f, 0.15f),
            new Color(0.3f, 0.2f, 0.5f),
            new Color(0.95f, 0.8f, 0.2f),
            new Color(0.95f, 0.95f, 0.95f),
            new Color(0.12f, 0.25f, 0.5f),
        };

        public static readonly string[] BotNames =
        {
            "Bombardero", "Pateador", "Chispas", "Mecha", "Pegajoso", "Petardo", "Trueno", "Cohete",
        };
    }
}
