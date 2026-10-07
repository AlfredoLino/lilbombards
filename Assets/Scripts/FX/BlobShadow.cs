using UnityEngine;

namespace LB
{
    /// <summary>
    /// Sombra de contacto suave bajo un objeto (como en BombSquad): ayuda a "asentar"
    /// personajes y bombas en el suelo y a leer la altura de los saltos y lanzamientos.
    /// Se proyecta sobre la geometria estatica y se aclara/agranda con la altura.
    /// </summary>
    public class BlobShadow : MonoBehaviour
    {
        public float Size = 1f;
        public float MaxHeight = 5f;
        public float Opacity = 0.5f;
        public Collider CenterFrom;

        Transform quad;
        Renderer rend;
        MaterialPropertyBlock mpb;
        static Material sharedMat;
        static readonly RaycastHit[] hits = new RaycastHit[8];
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static BlobShadow Attach(GameObject target, float size, float opacity = 0.5f, Collider centerFrom = null)
        {
            var b = target.AddComponent<BlobShadow>();
            b.Size = size;
            b.Opacity = opacity;
            b.CenterFrom = centerFrom;
            return b;
        }

        void Start()
        {
            if (sharedMat == null)
            {
                sharedMat = Gfx.Unlit(Color.black, false, Gfx.SoftCircle());
                sharedMat.renderQueue = 2990; // antes que el resto de transparentes
            }
            var go = Gfx.Part("BlobShadow", null, Gfx.Prim(PrimitiveType.Quad), sharedMat, Vector3.zero, Vector3.one);
            quad = go.transform;
            rend = go.GetComponent<Renderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            if (quad == null) return;
            Vector3 c = CenterFrom != null ? CenterFrom.bounds.center : transform.position;
            Vector3 origin = c + Vector3.up * 0.3f;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, MaxHeight + 1f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.attachedRigidbody != null) continue; // solo suelo/estaticos
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    bi = i;
                }
            }
            if (bi < 0 || rend == null)
            {
                if (rend != null) rend.enabled = false;
                return;
            }
            var h = hits[bi];
            float height = Mathf.Max(0f, c.y - h.point.y);
            float k = Mathf.Clamp01(1f - height / MaxHeight);
            rend.enabled = k > 0.02f;
            quad.position = h.point + h.normal * 0.02f;
            quad.rotation = Quaternion.LookRotation(-h.normal);
            float s = Size * Mathf.Lerp(1.4f, 0.8f, k);
            quad.localScale = new Vector3(s, s, s);
            mpb.SetColor(ColorId, new Color(0f, 0f, 0f, Opacity * k * k));
            rend.SetPropertyBlock(mpb);
        }

        void OnDisable()
        {
            if (rend != null) rend.enabled = false;
        }

        void OnDestroy()
        {
            if (quad != null) Destroy(quad.gameObject);
        }
    }
}
