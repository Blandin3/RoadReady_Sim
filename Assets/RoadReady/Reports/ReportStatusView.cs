using TMPro;
using UnityEngine;

namespace RoadReady.Reports
{
    /// <summary>
    /// A small message that floats in front of the player's view ("Sending report...", "Report sent..."). It is made
    /// of 3D text so it works in the headset as well as on a flat screen. It is placed once, in front of the
    /// camera, when shown, and then only turns to face the camera (it does not follow the head, which would be
    /// uncomfortable).
    /// </summary>
    public class ReportStatusView : MonoBehaviour
    {
        const float k_Distance = 0.9f;   // metres in front of the camera
        const float k_Drop = 0.30f;      // metres below the line of sight, clear of the menu in the middle

        TextMeshPro m_Text;
        GameObject m_Root;

        public static ReportStatusView Create(Transform parent)
        {
            var view = new GameObject("ReportStatusView").AddComponent<ReportStatusView>();
            view.transform.SetParent(parent, false);
            view.Build();
            return view;
        }

        void Build()
        {
            m_Root = new GameObject("Panel");
            m_Root.transform.SetParent(transform, false);

            // Dark rounded-looking backing so white text is readable over any scene.
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(m_Root.transform, false);
            back.transform.localScale = new Vector3(1.3f, 0.26f, 1f);
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                back.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.05f, 0.07f, 0.1f, 0.85f) };

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(m_Root.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.005f); // just in front of the backing
            m_Text = textObject.AddComponent<TextMeshPro>();
            m_Text.rectTransform.sizeDelta = new Vector2(1.2f, 0.22f);
            m_Text.alignment = TextAlignmentOptions.Center;
            m_Text.fontSize = 0.7f;
            m_Text.color = Color.white;
            m_Root.SetActive(false);
        }

        public void Show(string message)
        {
            if (m_Text == null)
                return;
            if (!m_Root.activeSelf)
                PlaceInFrontOfCamera();
            m_Text.text = message;
            m_Root.SetActive(true);
        }

        public void Hide()
        {
            if (m_Root != null)
                m_Root.SetActive(false);
        }

        void PlaceInFrontOfCamera()
        {
            var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (cam == null)
                return;
            // Use the flat (left-right) direction the player faces so the message stays level even if they look down.
            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = cam.transform.forward;
            forward.Normalize();
            m_Root.transform.position = cam.transform.position + forward * k_Distance + Vector3.down * k_Drop;
        }

        void LateUpdate()
        {
            if (m_Root == null || !m_Root.activeSelf)
                return;
            var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (cam != null)
                m_Root.transform.rotation = Quaternion.LookRotation(m_Root.transform.position - cam.transform.position, Vector3.up);
        }
    }
}
