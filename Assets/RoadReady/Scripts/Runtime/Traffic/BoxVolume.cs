using System.Collections.Generic;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>Box volume helper shared by rule zones.</summary>
    public abstract class BoxVolume : MonoBehaviour
    {
        [SerializeField] protected Vector3 m_Size = new Vector3(8f, 3f, 20f);

        public Vector3 Size { get => m_Size; set => m_Size = value; }

        public bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            var half = m_Size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z && local.y >= -1f && local.y <= m_Size.y;
        }

        protected void DrawBox(Color color)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = color;
            Gizmos.DrawWireCube(new Vector3(0f, m_Size.y * 0.5f, 0f), m_Size);
        }
    }

}
