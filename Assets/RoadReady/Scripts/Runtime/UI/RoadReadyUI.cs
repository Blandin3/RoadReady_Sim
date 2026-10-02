using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoadReady.UI
{
    [Serializable]
    public struct NamedTree
    {
        public string name;
        public VisualTreeAsset tree;
    }

    /// <summary>
    /// Owns the world-space menu panel: swaps screens into its content area, shows the confirm modal and keeps
    /// the panel in a comfortable spot in front of the learner.
    /// </summary>
    public class RoadReadyUI : MonoBehaviour
    {
        [SerializeField] WorldSpacePanel m_MenuPanel;
        [SerializeField] List<NamedTree> m_Screens = new List<NamedTree>();
        [SerializeField] float m_StandingDistance = 1.7f;
        [SerializeField] float m_SeatedDistance = 1.1f;
        [SerializeField] float m_SeatedScale = 0.65f;

        RoadReadyApp m_App;
        VisualElement m_Content;
        VisualElement m_ModalLayer;
        Action m_ModalConfirm;
        Action m_ModalCancel;
        UIScreen m_Current;

        public UIScreen Current => m_Current;
        public bool MenuVisible => m_MenuPanel != null && m_MenuPanel.Visible;
        public WorldSpacePanel MenuPanel => m_MenuPanel;

        public void Initialize(RoadReadyApp app)
        {
            m_App = app;
            var root = m_MenuPanel.Root;
            m_Content = root.Q<VisualElement>("content") ?? root;
            m_ModalLayer = root.Q<VisualElement>("modal-layer");
            root.Btn("modal-confirm", () => CloseModal(true));
            root.Btn("modal-cancel", () => CloseModal(false));
        }

        public VisualTreeAsset GetTree(string name)
        {
            foreach (var entry in m_Screens)
                if (entry.name == name)
                    return entry.tree;
            Debug.LogWarning($"[RoadReady] No UXML registered for screen '{name}'.");
            return null;
        }

        public T Show<T>(T screen, bool reposition = true) where T : UIScreen
        {
            m_Current?.OnHide();
            m_ModalLayer.SetVisible(false);
            m_Content.Clear();
            m_Current = screen;
            screen.Mount(m_App, this, m_Content, GetTree(screen.TreeName));
            SetMenuVisible(true, reposition);
            return screen;
        }

        public void HideMenu()
        {
            m_Current?.OnHide();
            m_Current = null;
            m_Content?.Clear();
            SetMenuVisible(false, false);
        }

        public void SetMenuVisible(bool visible, bool reposition)
        {
            if (visible && reposition)
                Reposition();
            m_MenuPanel.SetVisible(visible);
            m_App.Rig.SetPointers(visible || m_App.State != Core.AppState.InScenario);
        }

        public void Reposition()
        {
            var seated = m_App.Rig.IsSeated;
            m_MenuPanel.SetScaleMultiplier(seated ? m_SeatedScale : 1f);
            m_MenuPanel.PlaceInFront(m_App.Rig.HeadTransform, seated ? m_SeatedDistance : m_StandingDistance, seated ? -0.05f : -0.15f);
        }

        public void ApplyTextScale(float scale) => m_MenuPanel.SetTextScale(scale);

        public void Confirm(string title, string body, string confirmText, Action onConfirm, Action onCancel = null, string cancelText = "Cancel")
        {
            var root = m_MenuPanel.Root;
            root.SetText("modal-title", title);
            root.SetText("modal-body", body);
            var confirm = root.Q<Button>("modal-confirm");
            var cancel = root.Q<Button>("modal-cancel");
            confirm.text = confirmText;
            cancel.text = cancelText;
            cancel.SetVisible(!string.IsNullOrEmpty(cancelText));
            m_ModalConfirm = onConfirm;
            m_ModalCancel = onCancel;
            m_ModalLayer.SetVisible(true);
            if (!MenuVisible)
                SetMenuVisible(true, true);
        }

        void CloseModal(bool confirmed)
        {
            m_ModalLayer.SetVisible(false);
            var action = confirmed ? m_ModalConfirm : m_ModalCancel;
            m_ModalConfirm = m_ModalCancel = null;
            action?.Invoke();
        }
    }
}
