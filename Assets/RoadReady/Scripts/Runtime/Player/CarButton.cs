using System;
using System.Collections.Generic;
using UnityEngine;
using RoadReady.Core;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace RoadReady.Player
{
    /// <summary>
    /// A push button in the cabin (the ON button, the door's exit button). Press it with a controller or hand
    /// (grip or trigger while the hand is on it), or click it with the mouse when there is no headset.
    /// It only raises <see cref="Pressed"/>; what it does is up to the component that listens
    /// (<see cref="EngineStartButton"/>, <see cref="ExitCarButton"/>).
    /// </summary>
    public class CarButton : XRBaseInteractable
    {
        static readonly List<CarButton> s_All = new List<CarButton>();

        [Tooltip("Lamp that lights while the button is 'on' (optional).")]
        [SerializeField] Renderer m_Lamp;
        [SerializeField] int m_LampMaterialIndex;
        [SerializeField] Color m_LampOn = new Color(0.2f, 1f, 0.35f);
        [Tooltip("The part of the button that dips when it is pressed (optional).")]
        [SerializeField] Transform m_Cap;
        [SerializeField] float m_PressDepth = 0.004f;

        MaterialPropertyBlock m_Block;
        Vector3 m_CapRest;
        float m_PressedUntil;

        public event Action Pressed;

        public void Configure(Renderer lamp, int lampMaterialIndex, Transform cap)
        {
            m_Lamp = lamp;
            m_LampMaterialIndex = lampMaterialIndex;
            m_Cap = cap;
        }

        protected override void Awake()
        {
            base.Awake();
            selectMode = InteractableSelectMode.Multiple;
            if (m_Cap != null)
                m_CapRest = m_Cap.localPosition;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            s_All.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            s_All.Remove(this);
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            Press(args.interactorObject);
        }

        protected override void OnActivated(ActivateEventArgs args)
        {
            base.OnActivated(args);
            Press(args.interactorObject);
        }

        public void Press(IXRInteractor by = null)
        {
            if (by is XRBaseInputInteractor input)
                input.SendHapticImpulse(0.6f, 0.06f);
            m_PressedUntil = Time.unscaledTime + 0.15f;
            Pressed?.Invoke();
        }

        /// <summary>Lights or darkens the button's lamp.</summary>
        public void SetLit(bool lit)
        {
            if (m_Lamp == null)
                return;
            m_Block ??= new MaterialPropertyBlock();
            m_Lamp.GetPropertyBlock(m_Block, m_LampMaterialIndex);
            m_Block.SetColor("_EmissionColor", lit ? m_LampOn * 3f : Color.black);
            m_Block.SetColor("_BaseColor", lit ? m_LampOn : m_LampOn * 0.25f);
            m_Lamp.SetPropertyBlock(m_Block, m_LampMaterialIndex);
        }

        /// <summary>Lights the lamp in any colour at a strength 0..1 (used to make a button breathe).</summary>
        public void SetLamp(Color color, float strength)
        {
            if (m_Lamp == null)
                return;
            m_Block ??= new MaterialPropertyBlock();
            m_Lamp.GetPropertyBlock(m_Block, m_LampMaterialIndex);
            m_Block.SetColor("_EmissionColor", color * (3f * strength));
            m_Block.SetColor("_BaseColor", color * Mathf.Lerp(0.25f, 1f, strength));
            m_Lamp.SetPropertyBlock(m_Block, m_LampMaterialIndex);
        }

        void Update()
        {
            if (m_Cap != null)
                m_Cap.localPosition = m_CapRest + Vector3.down * (Time.unscaledTime < m_PressedUntil ? m_PressDepth : 0f);
        }

        /// <summary>Flat-screen use: presses the button under a mouse position. Returns true if one was pressed.</summary>
        public static bool ClickAt(Vector2 screenPosition)
        {
            var app = RoadReadyApp.Instance;
            var camera = app != null && app.Rig != null ? app.Rig.Head : Camera.main;
            if (camera == null)
                return false;
            var ray = camera.ScreenPointToRay(screenPosition);
            var best = float.MaxValue;
            CarButton target = null;
            foreach (var hit in Physics.RaycastAll(ray, 6f, ~0, QueryTriggerInteraction.Collide))
            {
                var button = hit.collider.GetComponentInParent<CarButton>();
                if (button != null && button.isActiveAndEnabled && hit.distance < best)
                {
                    best = hit.distance;
                    target = button;
                }
            }

            if (target == null)
                return false;
            target.Press();
            return true;
        }
    }
}
