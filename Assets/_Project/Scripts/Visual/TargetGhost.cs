using System.Collections.Generic;
using Station.Mechanism;
using UnityEngine;

namespace Station.Visual
{
    /// <summary>
    /// Dibuja una copia translúcida del cuerpo en la pose del TARGET del drive, así se ve
    /// la diferencia entre lo que se pide y lo que el mecanismo logra.
    /// Pose = padre · anclaPadre · movimiento(target) · anclaHijo⁻¹
    /// </summary>
    [RequireComponent(typeof(JointDriveController))]
    public class TargetGhost : MonoBehaviour
    {
        [SerializeField] private Material ghostMaterial;
        // Diferencia target-real (m, o °/100) desde la cual aparece el fantasma. Con menos, la malla real
        // y la copia casi coinciden y la superposición tiñe de celeste la pieza entera.
        private const float ShowThreshold = 0.02f;
        [SerializeField] private float fadeSpeed = 6f;

        private JointDriveController joint;
        private ArticulationBody body;
        private Transform ghostRoot;
        private readonly List<(Transform source, Transform copy)> pairs = new();
        private MaterialPropertyBlock block;
        private readonly List<Renderer> copies = new();
        private float alpha;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        public void Configure(Material mat) => ghostMaterial = mat;

        private void Start()
        {
            joint = GetComponent<JointDriveController>();
            body = joint.Body;
            if (joint.Mode != JointDriveController.DriveMode.Position || ghostMaterial == null) { enabled = false; return; }

            ghostRoot = new GameObject($"{name}_Ghost").transform;
            ghostRoot.SetParent(transform.parent, false);
            block = new MaterialPropertyBlock();

            // Copia solo lo visual (MeshFilter + MeshRenderer) de este cuerpo y sus descendientes
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.TryGetComponent(out MeshRenderer src) || src.sharedMaterial == ghostMaterial) continue;
                // Las piezas de otro joint hijo (mesa, empujador...) tienen su propio fantasma: si se copiaran aquí
                // aparecerían dos veces, una en su pose real y otra en la del target (el "desfasado" al girar).
                if (mf.GetComponentInParent<JointDriveController>() != joint) continue;
                var go = new GameObject(mf.name + "_g");
                go.transform.SetParent(ghostRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = ghostMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                copies.Add(r);
                pairs.Add((mf.transform, go.transform));
            }
            ghostRoot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            float err = Mathf.Abs(joint.Target - joint.Actual);
            if (joint.IsRevolute) err /= 100f;
            // Opacidad proporcional a la diferencia: aparece despacio al separarse y se apaga al llegar
            float goal = body.xDrive.stiffness > 0f ? Mathf.Clamp01((err - ShowThreshold) / ShowThreshold) : 0f;
            alpha = Mathf.MoveTowards(alpha, goal, fadeSpeed * Time.deltaTime);
            bool visible = alpha > 0.01f;
            if (ghostRoot.gameObject.activeSelf != visible) ghostRoot.gameObject.SetActive(visible);
            if (!visible) return;

            // Pose del cuerpo con el joint en el target, relativa al padre
            Matrix4x4 parentAnchor = Matrix4x4.TRS(body.parentAnchorPosition, body.parentAnchorRotation, Vector3.one);
            Matrix4x4 childAnchor = Matrix4x4.TRS(body.anchorPosition, body.anchorRotation, Vector3.one);
            Matrix4x4 motion = joint.IsRevolute
                ? Matrix4x4.Rotate(Quaternion.AngleAxis(joint.Target, Vector3.right))
                : Matrix4x4.Translate(Vector3.right * joint.Target);
            Matrix4x4 bodyWorld = transform.parent.localToWorldMatrix * parentAnchor * motion * childAnchor.inverse;

            // Cada copia mantiene su pose relativa al cuerpo real (sigue también a los hijos móviles)
            Matrix4x4 toBody = transform.worldToLocalMatrix;
            foreach (var (source, copy) in pairs)
            {
                Matrix4x4 m = bodyWorld * toBody * source.localToWorldMatrix;
                copy.SetPositionAndRotation(m.GetPosition(), m.rotation);
            }

            block.SetFloat(AlphaId, alpha);
            foreach (var r in copies) r.SetPropertyBlock(block);
        }
    }
}
