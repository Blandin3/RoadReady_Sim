using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>The exit button on the door: leave the car (asks first, because it ends the attempt).</summary>
    [RequireComponent(typeof(CarButton))]
    public class ExitCarButton : MonoBehaviour
    {
        CarButton m_Button;

        void OnEnable()
        {
            m_Button = GetComponent<CarButton>();
            m_Button.Pressed += OnPressed;
        }

        void OnDisable() => m_Button.Pressed -= OnPressed;

        void OnPressed()
        {
            if (RoadReadyApp.Instance != null)
                RoadReadyApp.Instance.RequestExitCar();
        }
    }
}
