using System;
using System.IO;
using UnityEngine;

namespace RoadReady.Data
{
    /// <summary>
    /// Append-only NDJSON log for one attempt. Every line is flushed immediately so a crash or the headset
    /// being taken off mid-attempt still leaves a usable trace (NFR reliability).
    /// </summary>
    public sealed class TelemetryWriter : IDisposable
    {
        StreamWriter m_Writer;

        public string Path { get; }

        public TelemetryWriter(string path)
        {
            Path = path;
            try
            {
                m_Writer = new StreamWriter(path, append: true) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoadReady] Telemetry disabled, could not open {path}: {e.Message}");
            }
        }

        public void Write(TelemetrySample sample) => WriteLine(JsonUtility.ToJson(sample));

        public void Write(TelemetryEvent evt) => WriteLine(JsonUtility.ToJson(evt));

        public void WriteEvent(string kind, float time, string id, string detail = null) =>
            Write(new TelemetryEvent { kind = kind, t = time, id = id, detail = detail });

        void WriteLine(string line)
        {
            if (m_Writer == null)
                return;
            try
            {
                m_Writer.WriteLine(line);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoadReady] Telemetry write failed: {e.Message}");
                Dispose();
            }
        }

        public void Dispose()
        {
            m_Writer?.Dispose();
            m_Writer = null;
        }
    }
}
