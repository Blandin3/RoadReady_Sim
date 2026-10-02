using System;
using RoadReady.Core;

namespace RoadReady.Data
{
    public class QuestionnaireDefinition
    {
        public QuestionnaireType type;
        public string title;
        public string instructions;
        public string[] items;
        public string[] scaleLabels;
        /// <summary>Value stored for the first scale label (SSQ 0, SUS 1).</summary>
        public int scaleMin;
    }

    /// <summary>
    /// Standardised questionnaires administered inside the headset so results are linked to the
    /// anonymised participant id automatically (proposal 3.2.4).
    /// </summary>
    public static class Questionnaires
    {
        // Kennedy, Lane, Berbaum & Lilienthal (1993) Simulator Sickness Questionnaire.
        public static readonly QuestionnaireDefinition SSQ = new QuestionnaireDefinition
        {
            type = QuestionnaireType.SSQ,
            title = "How do you feel right now?",
            instructions = "Rate how much each symptom affects you RIGHT NOW.",
            scaleMin = 0,
            scaleLabels = new[] { "None", "Slight", "Moderate", "Severe" },
            items = new[]
            {
                "General discomfort", "Fatigue", "Headache", "Eye strain", "Difficulty focusing",
                "Increased salivation", "Sweating", "Nausea", "Difficulty concentrating", "Fullness of head",
                "Blurred vision", "Dizziness (eyes open)", "Dizziness (eyes closed)", "Vertigo",
                "Stomach awareness", "Burping",
            },
        };

        // Brooke (1996) System Usability Scale.
        public static readonly QuestionnaireDefinition SUS = new QuestionnaireDefinition
        {
            type = QuestionnaireType.SUS,
            title = "What did you think of RoadReady?",
            instructions = "Rate how much you agree with each statement.",
            scaleMin = 1,
            scaleLabels = new[] { "Strongly disagree", "Disagree", "Neutral", "Agree", "Strongly agree" },
            items = new[]
            {
                "I think that I would like to use RoadReady frequently.",
                "I found RoadReady unnecessarily complex.",
                "I thought RoadReady was easy to use.",
                "I think that I would need the support of a technical person to be able to use RoadReady.",
                "I found the various functions in RoadReady were well integrated.",
                "I thought there was too much inconsistency in RoadReady.",
                "I would imagine that most people would learn to use RoadReady very quickly.",
                "I found RoadReady very cumbersome to use.",
                "I felt very confident using RoadReady.",
                "I needed to learn a lot of things before I could get going with RoadReady.",
            },
        };

        // SSQ item membership (0-based item index) for each subscale.
        static readonly int[] k_Nausea = { 0, 5, 6, 7, 8, 14, 15 };
        static readonly int[] k_Oculomotor = { 0, 1, 2, 3, 4, 8, 10 };
        static readonly int[] k_Disorientation = { 4, 7, 9, 10, 11, 12, 13 };

        public static QuestionnaireDefinition Get(QuestionnaireType type) => type == QuestionnaireType.SSQ ? SSQ : SUS;

        public static QuestionnaireResult Score(QuestionnaireType type, int[] answers, string sessionId)
        {
            var result = new QuestionnaireResult
            {
                type = type,
                sessionId = sessionId,
                completedUtc = DateTime.UtcNow.ToString("o"),
                answers = (int[])answers.Clone(),
            };

            if (type == QuestionnaireType.SSQ)
            {
                float n = Sum(answers, k_Nausea), o = Sum(answers, k_Oculomotor), d = Sum(answers, k_Disorientation);
                result.nausea = n * 9.54f;
                result.oculomotor = o * 7.58f;
                result.disorientation = d * 13.92f;
                result.total = (n + o + d) * 3.74f;
            }
            else
            {
                var sum = 0;
                for (var i = 0; i < answers.Length; i++)
                    sum += i % 2 == 0 ? answers[i] - 1 : 5 - answers[i];
                result.total = sum * 2.5f;
            }

            return result;
        }

        static float Sum(int[] answers, int[] indices)
        {
            var sum = 0;
            foreach (var i in indices)
                sum += answers[i];
            return sum;
        }
    }
}
