using UnityEngine;

namespace LB
{
    /// <summary>
    /// Ayuda de punteria para el jugador con raton: una mira en el suelo bajo el puntero y,
    /// mientras lleva algo en la mano, un circulo donde caeria con la carga actual
    /// (se aleja hacia el puntero a medida que se carga), unidos por puntos.
    /// </summary>
    public class AimMarker : MonoBehaviour
    {
        const int Dots = 7;
        LBCharacter ch;
        Transform cursor, landing;
        Transform[] dots;
        Material cursorMat, landingMat, dotMat;

        public static void Attach(LBCharacter c)
        {
            var m = c.gameObject.AddComponent<AimMarker>();
            m.ch = c;
        }

        void Start()
        {
            Color col = ch.Slot != null ? ch.Slot.Color : Color.white;
            cursorMat = Gfx.Unlit(new Color(1f, 1f, 1f, 0.55f), false, Gfx.Ring());
            landingMat = Gfx.Unlit(new Color(col.r, col.g, col.b, 0.9f), false, Gfx.Ring());
            dotMat = Gfx.Unlit(new Color(1f, 1f, 1f, 0.6f), false, Gfx.SoftCircle());
            cursor = MakeQuad("AimCursor", cursorMat, 0.7f);
            landing = MakeQuad("AimLanding", landingMat, 1f);
            dots = new Transform[Dots];
            for (int i = 0; i < Dots; i++) dots[i] = MakeQuad("AimDot", dotMat, 0.14f);
        }

        static Transform MakeQuad(string name, Material m, float size)
        {
            var go = Gfx.Part(name, null, Gfx.Prim(PrimitiveType.Quad), m, Vector3.zero, Vector3.one * size);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        void LateUpdate()
        {
            if (cursor == null) return;
            bool alive = ch != null && !ch.Dead && GameManager.AllowControl;
            bool aiming = alive && ch.Cur.hasAim;
            cursor.gameObject.SetActive(aiming);
            if (aiming)
            {
                cursor.position = ch.Cur.aimPoint + Vector3.up * 0.03f;
                cursor.rotation = Quaternion.Euler(90f, Time.time * 90f, 0f);
            }

            bool showThrow = aiming && ch.Holding != null && ch.AimDir(out _);
            landing.gameObject.SetActive(showThrow);
            for (int i = 0; i < Dots; i++) dots[i].gameObject.SetActive(showThrow);
            if (!showThrow) return;

            ch.AimDir(out Vector3 dir);
            float charge = Mathf.Max(0f, ch.ThrowCharge);
            float dist = ch.ThrowDistanceFor(charge); // mas corto sin mano o sin brazo
            if (ch.Holding.AsCharacter != null) dist *= Tuning.ThrowCharacterMul * Tuning.ThrowCharacterMul;
            Vector3 start = ch.transform.position;
            start.y = 0f;
            Vector3 end = start + dir * dist;
            landing.position = end + Vector3.up * 0.035f;
            float s = 0.9f + 0.15f * Mathf.Sin(Time.time * 10f);
            landing.localScale = new Vector3(s, s, s);

            // Puntos del arco proyectados en el suelo.
            for (int i = 0; i < Dots; i++)
            {
                float t = (i + 1f) / (Dots + 1f);
                dots[i].position = Vector3.Lerp(start, end, t) + Vector3.up * 0.03f;
            }
        }

        void OnDestroy()
        {
            if (cursor != null) Destroy(cursor.gameObject);
            if (landing != null) Destroy(landing.gameObject);
            if (dots != null)
                foreach (var d in dots) if (d != null) Destroy(d.gameObject);
            if (cursorMat != null) Destroy(cursorMat);
            if (landingMat != null) Destroy(landingMat);
            if (dotMat != null) Destroy(dotMat);
        }
    }
}
