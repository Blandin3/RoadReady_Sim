using System.Collections;
using RoadReady.Scenarios;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    /// <summary>
    /// Fade-to-black between scenes (comfort: never teleport the view while visible). A UI Toolkit world-space
    /// panel parented just in front of the camera.
    /// </summary>
    [RequireComponent(typeof(WorldSpacePanel))]
    public class ScreenFader : MonoBehaviour, IScreenFader
    {
        [SerializeField] float m_StartAlpha = 1f;

        VisualElement m_Fader;
        float m_Alpha;

        void Awake() => m_Alpha = m_StartAlpha;

        void OnEnable() => Set(m_Alpha);

        public IEnumerator FadeTo(float alpha, float seconds)
        {
            var from = m_Alpha;
            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                Set(Mathf.Lerp(from, alpha, seconds > 0f ? t / seconds : 1f));
                yield return null;
            }

            Set(alpha);
        }

        void Set(float alpha)
        {
            m_Alpha = alpha;
            if (m_Fader == null || m_Fader.panel == null)
            {
                // The UIDocument builds its tree in its own OnEnable, so resolve lazily.
                var root = GetComponent<WorldSpacePanel>().Root;
                if (root == null)
                    return;
                m_Fader = root.Q<VisualElement>("fader") ?? root;
                m_Fader.pickingMode = PickingMode.Ignore;
            }

            m_Fader.style.opacity = alpha;
            m_Fader.style.display = alpha <= 0.001f ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
