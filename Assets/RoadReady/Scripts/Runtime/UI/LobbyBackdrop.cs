using UnityEngine;

namespace RoadReady.UI
{
    /// <summary>
    /// Full-view background picture for the lobby (splash, welcome and setup menus). A quad kept far in front of
    /// the camera and sized to cover the whole view, like CSS "background-size: cover". It lives under the Lobby
    /// object, so it disappears automatically while a scenario is loaded.
    /// </summary>
    public class LobbyBackdrop : MonoBehaviour
    {
        [SerializeField] float m_Distance = 40f;
        [Tooltip("Extra size beyond the exact view so edges never show (head movement, lens distortion in VR).")]
        [SerializeField] float m_Overscan = 1.3f;

        Camera m_Camera;
        Transform m_Quad;
        float m_ImageAspect = 16f / 9f;

        /// <summary>Adds the backdrop under the lobby and hides the lobby's own placeholder walls and floor.</summary>
        public static LobbyBackdrop Create(GameObject lobby, Camera camera, Material material)
        {
            // Placeholder geometry would sit in front of the picture. Colliders stay so the floor still works.
            foreach (var r in lobby.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Lobby Backdrop";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(lobby.transform, false);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var backdrop = quad.AddComponent<LobbyBackdrop>();
            backdrop.m_Camera = camera;
            backdrop.m_Quad = quad.transform;
            var texture = material.mainTexture;
            if (texture != null && texture.height > 0)
                backdrop.m_ImageAspect = (float)texture.width / texture.height;
            return backdrop;
        }

        void LateUpdate()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;

            var cam = m_Camera.transform;
            m_Quad.SetPositionAndRotation(cam.position + cam.forward * m_Distance, cam.rotation);

            // Cover the view: grow whichever side is short so the picture keeps its proportions.
            var height = 2f * m_Distance * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * m_Overscan;
            var width = height * m_Camera.aspect;
            if (width / height > m_ImageAspect)
                height = width / m_ImageAspect;
            else
                width = height * m_ImageAspect;
            m_Quad.localScale = new Vector3(width, height, 1f);
        }
    }
}
