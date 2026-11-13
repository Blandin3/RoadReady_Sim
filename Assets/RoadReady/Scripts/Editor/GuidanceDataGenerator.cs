using System;
using System.IO;
using RoadReady.Guidance;
using UnityEditor;
using UnityEngine;

namespace RoadReady.EditorTools
{
    /// <summary>
    /// RoadReady > Guidance > Generate Synthetic Training Data: writes tools/ml/data/synthetic_guidance.csv.
    /// The labels come from the same rule function the game uses, and every label gets the same number of rows.
    /// Also callable from a batch build: Unity.exe -batchmode -quit -projectPath ... -executeMethod RoadReady.EditorTools.GuidanceDataGenerator.GenerateFromCommandLine
    /// </summary>
    public static class GuidanceDataGenerator
    {
        const int k_RowsPerClass = 2000;
        const int k_Seed = 20261108;

        [MenuItem("RoadReady/Guidance/Generate Synthetic Training Data", priority = 60)]
        public static void GenerateFromMenu()
        {
            var report = Generate(k_RowsPerClass, k_Seed);
            EditorUtility.DisplayDialog("Synthetic guidance data", report, "OK");
        }

        public static void GenerateFromCommandLine()
        {
            Debug.Log(Generate(k_RowsPerClass, k_Seed));
        }

        static string Generate(int perClass, int seed)
        {
            var rows = SyntheticGuidanceData.Generate(perClass, seed, new GuidanceSettings(), out var draws);
            var counts = new int[Enum.GetValues(typeof(GuidanceLabel)).Length];
            foreach (var row in rows)
                counts[(int)row.label]++;

            // The project folder is the parent of Assets.
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var folder = Path.Combine(projectRoot, "tools", "ml", "data");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "synthetic_guidance.csv");
            File.WriteAllText(path, SyntheticGuidanceData.ToCsv(rows));

            var summary = $"Wrote {rows.Count} synthetic rows to {path}\nSeed {seed}, {draws} random candidates drawn.\nRows per label:";
            foreach (GuidanceLabel label in Enum.GetValues(typeof(GuidanceLabel)))
                summary += $"\n  {label}: {counts[(int)label]}";
            summary += "\nReal rows: 0 (real rows come from the developer logger and are marked source=real).";
            return summary;
        }
    }
}
