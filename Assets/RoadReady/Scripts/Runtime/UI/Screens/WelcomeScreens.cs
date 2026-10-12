using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;

namespace RoadReady.UI
{
    /// <summary>Logo splash shown when the app starts; moves on to the welcome page after a few seconds.</summary>
    public class SplashScreen : UIScreen
    {
        public override string TreeName => "Splash";
        public override bool Floating => true;

        protected override void Bind()
        {
            var logo = App.Config.logo;
            var image = Q<VisualElement>("splash-logo");
            image.SetVisible(logo != null);
            Q<VisualElement>("splash-fallback").SetVisible(logo == null);
            if (logo != null)
                image.style.backgroundImage = new StyleBackground(logo);

            // Fade in after the first layout so the USS opacity transition plays.
            var splash = Q<VisualElement>("splash");
            splash.schedule.Execute(() => splash.AddToClassList("splash--visible")).StartingIn(50);

            var ms = Mathf.RoundToInt(App.Config.splashSeconds * 1000f);
            Root.schedule.Execute(() =>
            {
                // Ignore the timer if something else replaced the splash in the meantime.
                if (UI.Current == this)
                    App.ShowWelcome();
            }).StartingIn(ms);
        }
    }

    /// <summary>First page of the app: what RoadReady is, voice guide toggle, Start and How it works.</summary>
    public class WelcomeScreen : UIScreen
    {
        public override string TreeName => "Welcome";
        public override bool Floating => true;

        protected override void Bind()
        {
            var logo = App.Config.logo;
            if (logo != null)
            {
                var badge = Q<VisualElement>("welcome-logo");
                badge.style.backgroundImage = new StyleBackground(logo);
                badge.AddToClassList("logo-badge--image");
                Q<Label>("welcome-logo-text").SetVisible(false);
            }

            Btn("voice-toggle", ToggleVoice);
            RefreshVoice();

            Text("hint", XRSettings.isDeviceActive
                ? "Point with the controller and pull the trigger to select"
                : "Click with the mouse to select");

            Btn("start", App.GoToSetup);
            Btn("how-it-works", App.ShowHowItWorks);
        }

        void ToggleVoice()
        {
            var settings = App.Comfort.Clone();
            settings.voiceInstructions = !settings.voiceInstructions;
            App.SaveComfort(settings);
            RefreshVoice();
        }

        void RefreshVoice()
        {
            var on = App.Comfort.voiceInstructions;
            var button = Q<Button>("voice-toggle");
            button.text = on ? "Voice guide: ON" : "Voice guide: OFF";
            button.EnableInClassList("pill--on", on);
        }
    }
}
