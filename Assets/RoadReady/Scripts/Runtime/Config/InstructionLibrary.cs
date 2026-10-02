using System.Collections.Generic;

namespace RoadReady.Config
{
    /// <summary>
    /// On-screen instruction text (FR12). Each key can have a matching voice clip in
    /// <see cref="RoadReadyConfig.voiceLines"/>; the text is always shown, the voice is optional.
    /// </summary>
    public static class InstructionLibrary
    {
        public const string Welcome = "welcome";
        public const string ControlsDriver = "controls_driver";
        public const string ControlsPedestrian = "controls_pedestrian";
        public const string AttemptStart = "attempt_start";
        public const string AttemptComplete = "attempt_complete";
        public const string Collision = "collision";
        public const string TimeUp = "time_up";
        public const string TutDriverThrottle = "tut_driver_throttle";
        public const string TutDriverBrake = "tut_driver_brake";
        public const string TutDriverSteer = "tut_driver_steer";
        public const string TutDriverIndicator = "tut_driver_indicator";
        public const string TutDriverMirror = "tut_driver_mirror";
        public const string TutPedLook = "tut_ped_look";
        public const string TutPedWalk = "tut_ped_walk";
        public const string TutPedCross = "tut_ped_cross";
        public const string TutSpotHazard = "tut_spot_hazard";
        public const string TutDone = "tut_done";
        public const string SessionEnd = "session_end";

        static readonly Dictionary<string, string> s_Text = new Dictionary<string, string>
        {
            { Welcome, "Welcome to RoadReady. You will practise road-safety decisions as a driver and as a pedestrian in streets modelled on Kimironko. Nothing here can hurt you - mistakes are how you learn." },
            { ControlsDriver, "Right trigger: accelerate.  Left trigger: brake.  Left thumbstick: steer.  Grip buttons: indicators.  X: I see a hazard.  B: horn.  Menu: pause." },
            { ControlsPedestrian, "Left thumbstick: walk.  Right thumbstick: turn.  A: cross now.  B: stop / step back.  X: I see a hazard.  Look around by turning your head.  Menu: pause." },
            { AttemptStart, "Get ready. Drive or walk as you would in real life." },
            { AttemptComplete, "Scenario complete. Let's look at how you did." },
            { Collision, "Collision. In real life this could have been fatal. Let's see what happened." },
            { TimeUp, "Time is up for this attempt." },
            { TutDriverThrottle, "Gently pull the RIGHT trigger to move forward." },
            { TutDriverBrake, "Now pull the LEFT trigger to brake to a stop." },
            { TutDriverSteer, "Use the LEFT thumbstick to steer left and right." },
            { TutDriverIndicator, "Squeeze a GRIP button to switch on the indicator on that side." },
            { TutDriverMirror, "Turn your head over your shoulder to check your blind spot." },
            { TutPedLook, "Look LEFT, then RIGHT, then LEFT again. In Rwanda traffic drives on the right, so the nearest lane comes from your left." },
            { TutPedWalk, "Use the LEFT thumbstick to walk to the kerb marker." },
            { TutPedCross, "When the road is clear, press A to cross." },
            { TutSpotHazard, "Whenever you notice something dangerous developing, press X. It helps you train your hazard awareness." },
            { TutDone, "Tutorial complete. You are ready for your first scenario." },
            { SessionEnd, "Thank you. Please take the headset off carefully and answer a few quick questions." },
        };

        public static string Get(string key) => key != null && s_Text.TryGetValue(key, out var text) ? text : key;
    }
}
