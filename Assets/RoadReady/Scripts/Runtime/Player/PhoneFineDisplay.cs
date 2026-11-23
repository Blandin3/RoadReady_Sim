using System;
using System.Globalization;
using RoadReady.Core;
using RoadReady.Data;
using TMPro;
using UnityEngine;

namespace RoadReady.Player
{
    /// <summary>
    /// The phone mounted on the dashboard. Whenever the driver commits a punishable mistake (a rule violation) it shows a
    /// Rwanda National Police style traffic-fine message for a few seconds, then goes back to a dark screen.
    /// Wired by RoadReady > Add Phone To Player Car.
    /// </summary>
    public class PhoneFineDisplay : MonoBehaviour
    {
        [SerializeField] TMP_Text m_Text;
        [Tooltip("Lit while a fine is showing (the screen glow); the dark screen quad stays on all the time.")]
        [SerializeField] GameObject m_Glow;
        [SerializeField] float m_ShowSeconds = 12f;
        [SerializeField] string m_DefaultLocation = "Nyarugenge-Amizero";

        string m_Plate;
        float m_HideAt = -1f;

        public void Configure(TMP_Text text, GameObject glow)
        {
            m_Text = text;
            m_Glow = glow;
        }

        void Awake()
        {
            m_Plate = MakePlate(new System.Random());
            Clear();
        }

        void OnEnable() => RoadReadyEvents.TrafficFineIssued += OnFine;

        void OnDisable() => RoadReadyEvents.TrafficFineIssued -= OnFine;

        void Update()
        {
            if (m_HideAt > 0f && Time.unscaledTime >= m_HideAt)
                Clear();
        }

        void OnFine(DecisionRecord decision, string location)
        {
            if (m_Text == null || decision == null)
                return;
            m_Text.text = Compose(decision, string.IsNullOrWhiteSpace(location) ? m_DefaultLocation : location, m_Plate, DateTime.Now);
            if (m_Glow != null)
                m_Glow.SetActive(true);
            m_HideAt = Time.unscaledTime + m_ShowSeconds;
        }

        void Clear()
        {
            if (m_Text != null)
                m_Text.text = string.Empty;
            if (m_Glow != null)
                m_Glow.SetActive(false);
            m_HideAt = -1f;
        }

        /// <summary>Fine in RWF for each severity of violation.</summary>
        public static int FeeFor(ViolationSeverity severity) => severity switch
        {
            ViolationSeverity.Critical => 50000,
            ViolationSeverity.Major => 25000,
            _ => 10000,
        };

        /// <summary>The traffic-fine text, in the fixed format of the Rwanda Traffic Fine SMS.</summary>
        public static string Compose(DecisionRecord decision, string location, string plate, DateTime now)
        {
            var ticket = "TK" + new DateTimeOffset(now).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var fee = FeeFor(decision.severity).ToString("N0", CultureInfo.InvariantCulture);
            return "Traffic Fine:\n" +
                   $"Ticket:{ticket}\n" +
                   $"Speed:{Mathf.RoundToInt(decision.speedKmh)} km/h\n" +
                   $"Location:{location}\n" +
                   $"Fees:{fee} RWF\n" +
                   $"Plate No:{plate}\n" +
                   $"Issue Date:{now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}\n" +
                   $"Pay Before:{now.AddDays(30).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}\n" +
                   "Pay using IremboApp or dial *909*2*1#\n" +
                   "Late fees apply after the due date.";
        }

        /// <summary>A Rwandan-style plate, e.g. RAI505J.</summary>
        public static string MakePlate(System.Random random) =>
            "RA" + (char)('A' + random.Next(26)) + random.Next(100, 1000).ToString(CultureInfo.InvariantCulture) + (char)('A' + random.Next(26));
    }
}
