using UnityEngine;

namespace LB
{
    /// <summary>
    /// Post-procesado de la camara (Built-in Render Pipeline, OnRenderImage):
    /// profundidad de campo del fondo, bloom, gradacion de color calida y vinieta.
    /// Los valores se pueden ajustar en vivo desde el Inspector de "Main Camera" durante Play.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PostFX : MonoBehaviour
    {
        [Header("Bloom")]
        [Range(0f, 3f)] public float Threshold = 1.66f;
        [Range(0f, 1f)] public float SoftKnee = 0.3f;
        [Range(0f, 3f)] public float Intensity = 0.45f;
        [Range(1, 8)] public int Iterations = 6;

        [Header("Profundidad de campo")]
        [Tooltip("Distancia de la camara a la accion (la pone CameraRig).")]
        public float FocusDistance = 22f;
        public float FocusStart = 6f;
        public float FocusRange = 14f;
        [Range(0f, 1f)] public float DofAmount = 0.9f;

        [Header("Color")]
        [Range(0f, 2f)] public float Saturation = 1.15f;
        [Range(0.5f, 1.5f)] public float Contrast = 1.1f;
        public Color Tint = new Color(1.05f, 1.0f, 0.93f);
        public Color ShadowTint = new Color(0.84f, 0.87f, 1.0f);
        [Range(0f, 1f)] public float Vignette = 0.45f;

        Material mat;
        readonly RenderTexture[] chain = new RenderTexture[8];

        void OnEnable()
        {
            GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                var s = Shader.Find("Hidden/LB/PostFX");
                if (s == null || !s.isSupported)
                {
                    Graphics.Blit(src, dst);
                    enabled = false;
                    return;
                }
                mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            }

            float knee = Threshold * SoftKnee;
            mat.SetVector("_Filter", new Vector4(Threshold, Threshold - knee, 2f * knee, 0.25f / (knee + 0.00001f)));
            mat.SetFloat("_Intensity", Intensity);
            mat.SetFloat("_Saturation", Saturation);
            mat.SetFloat("_Contrast", Contrast);
            mat.SetFloat("_Vignette", Vignette);
            mat.SetVector("_Tint", Tint);
            mat.SetVector("_ShadowTint", ShadowTint);
            mat.SetFloat("_FocusDist", FocusDistance);
            mat.SetFloat("_FocusStart", FocusStart);
            mat.SetFloat("_FocusRange", Mathf.Max(0.1f, FocusRange));
            mat.SetFloat("_DofAmount", DofAmount);

            var fmt = src.format;

            // Copia desenfocada de la imagen (1/4 de resolucion) para la profundidad de campo.
            var b1 = RenderTexture.GetTemporary(src.width / 2, src.height / 2, 0, fmt);
            var b2 = RenderTexture.GetTemporary(src.width / 4, src.height / 4, 0, fmt);
            var b3 = RenderTexture.GetTemporary(src.width / 4, src.height / 4, 0, fmt);
            Graphics.Blit(src, b1, mat, 1);
            Graphics.Blit(b1, b2, mat, 1);
            Graphics.Blit(b2, b3, mat, 1);
            mat.SetTexture("_BlurTex", b3);

            // Cadena de bloom.
            int w = src.width / 2, h = src.height / 2;
            RenderTexture cur = chain[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, cur, mat, 0);
            RenderTexture source = cur;
            int i = 1;
            for (; i < Iterations; i++)
            {
                w /= 2;
                h /= 2;
                if (h < 2 || w < 2) break;
                cur = chain[i] = RenderTexture.GetTemporary(w, h, 0, fmt);
                Graphics.Blit(source, cur, mat, 1);
                source = cur;
            }
            for (i -= 2; i >= 0; i--)
            {
                cur = chain[i];
                chain[i] = null;
                Graphics.Blit(source, cur, mat, 2);
                RenderTexture.ReleaseTemporary(source);
                source = cur;
            }
            chain[0] = null;

            mat.SetTexture("_BloomTex", source);
            Graphics.Blit(src, dst, mat, 3);
            RenderTexture.ReleaseTemporary(source);
            RenderTexture.ReleaseTemporary(b1);
            RenderTexture.ReleaseTemporary(b2);
            RenderTexture.ReleaseTemporary(b3);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
