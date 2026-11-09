using UnityEngine;

namespace CarSales
{
    /// <summary>Put on the root of every car MODEL prefab. Applies paint colour and dirt.</summary>
    public class CarVisual : MonoBehaviour
    {
        [SerializeField] Renderer[] paintRenderers;            // body panels
        [SerializeField] int paintMaterialIndex = 0;
        [Tooltip("Optional: material slot per paint renderer (imported models). Empty = paintMaterialIndex for all.")]
        [SerializeField] int[] paintMaterialIndices = new int[0];
        [SerializeField] string colorProperty = "_BaseColor";  // URP Lit. Built-in Standard = "_Color"
        [SerializeField] string dirtProperty = "_Dirt";        // optional custom Shader Graph property
        static readonly Color DirtColor = new Color(0.35f, 0.3f, 0.25f);
        // Colour properties of other common shaders (glTFast, Built-in) so imported models can be painted too.
        static readonly int GltfColor = Shader.PropertyToID("baseColorFactor"), LegacyColor = Shader.PropertyToID("_Color");
        MaterialPropertyBlock mpb;

        public void Apply(Color paint, float dirt01)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            Color shown = Color.Lerp(paint, DirtColor, dirt01 * 0.5f); // simple "dirty" look
            for (int i = 0; i < paintRenderers.Length; i++)
            {
                var r = paintRenderers[i];
                if (!r) continue;
                int slot = paintMaterialIndices != null && i < paintMaterialIndices.Length ? paintMaterialIndices[i] : paintMaterialIndex;
                r.GetPropertyBlock(mpb, slot);
                mpb.SetColor(colorProperty, shown);
                mpb.SetColor(GltfColor, shown);
                mpb.SetColor(LegacyColor, shown);
                mpb.SetFloat(dirtProperty, dirt01);
                r.SetPropertyBlock(mpb, slot);
            }
        }
    }
}
