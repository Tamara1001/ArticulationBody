using UnityEngine;

namespace Station.Visual
{
    /// <summary>Variables globales del shader industrial (ambiente de hemisferio y outline).</summary>
    [ExecuteAlways]
    public class StationLook : MonoBehaviour
    {
        [SerializeField] private Color skyAmbient = new(0.62f, 0.68f, 0.78f);
        [SerializeField] private Color groundAmbient = new(0.30f, 0.27f, 0.25f);
        [SerializeField] private Color outlineColor = new(0.07f, 0.08f, 0.10f);
        [SerializeField] private float outlineWidth = 0.006f;

        private void OnEnable() => Push();
        private void OnValidate() => Push();

        private void Push()
        {
            Shader.SetGlobalColor("_StationSky", skyAmbient);
            Shader.SetGlobalColor("_StationGround", groundAmbient);
            Shader.SetGlobalColor("_StationOutlineColor", outlineColor);
            Shader.SetGlobalFloat("_StationOutlineWidth", outlineWidth);
        }
    }
}
