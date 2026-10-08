using UnityEngine;

namespace LB
{
    /// <summary>
    /// Camara elevada de angulo fijo como la de BombSquad: sigue el centro de los jugadores vivos
    /// y se aleja o acerca segun lo separados que esten. Incluye temblor por explosiones.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I;
        public Camera Cam;

        public float Pitch = 47f;
        PostFX post;
        Vector3 focus, focusVel;
        float dist = 22f, distVel;
        float shake;

        public static CameraRig Create()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var rig = go.AddComponent<CameraRig>();
            rig.Cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            rig.Cam.fieldOfView = 38f;
            rig.Cam.nearClipPlane = 0.3f;
            rig.Cam.farClipPlane = 600f;
            rig.Cam.clearFlags = CameraClearFlags.Skybox;
            rig.Cam.allowMSAA = true;
            rig.Cam.allowHDR = true;
            rig.post = go.AddComponent<PostFX>();
            I = rig;
            rig.Snap();
            return rig;
        }

        public static void Shake(float amount)
        {
            if (!Net.FxAllowed) return;
            Net.EvShake(amount);
            if (I != null) I.shake = Mathf.Min(1.2f, Mathf.Max(I.shake, amount));
        }

        void Snap()
        {
            ComputeTarget(out focus, out dist);
            Apply(0f);
        }

        void ComputeTarget(out Vector3 f, out float d)
        {
            bool lobby = GameManager.I == null || GameManager.I.Phase == GamePhase.Lobby;
            int n = 0;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            if (!lobby)
            {
                foreach (var c in LBCharacter.All)
                {
                    if (c.Dead) continue;
                    Vector3 p = c.transform.position;
                    if (p.y < -3f) continue;
                    if (n == 0) { min = p; max = p; }
                    else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    n++;
                }
            }
            if (n == 0)
            {
                f = new Vector3(0f, 0f, 0.5f);
                d = lobby ? 25f : 22f;
                return;
            }
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            float spread = Mathf.Max(size.x, size.z * 1.7f);
            // Mezcla con el centro del mapa para que la camara no se desplace demasiado.
            f = Vector3.Lerp(new Vector3(0f, 0f, 0.5f), new Vector3(center.x, 0f, center.z), 0.6f);
            Vector2 cx = Arena.I != null ? Arena.I.CamX : new Vector2(-4f, 4f);
            Vector2 cz = Arena.I != null ? Arena.I.CamZ : new Vector2(-2.5f, 3f);
            f.x = Mathf.Clamp(f.x, cx.x, cx.y);
            f.z = Mathf.Clamp(f.z, cz.x, cz.y);
            d = Mathf.Clamp(14f + spread * 0.75f, 15f, Arena.I != null ? Arena.I.CamMaxDist : 26f);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            ComputeTarget(out var tf, out var td);
            focus = Vector3.SmoothDamp(focus, tf, ref focusVel, 0.6f, Mathf.Infinity, dt);
            dist = Mathf.SmoothDamp(dist, td, ref distVel, 0.8f, Mathf.Infinity, dt);
            Apply(dt);
        }

        void Apply(float dt)
        {
            Quaternion rot = Quaternion.Euler(Pitch, 0f, 0f);
            Vector3 pos = focus - rot * Vector3.forward * dist;
            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 40f;
                Vector3 off = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * shake * 0.8f;
                pos += rot * off;
                shake = Mathf.Max(0f, shake - dt * 2.2f);
            }
            transform.SetPositionAndRotation(pos, rot);
            if (post != null) post.FocusDistance = dist;
        }
    }
}
