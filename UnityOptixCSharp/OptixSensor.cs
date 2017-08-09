using System.Collections.Generic;
using UnityEngine;

namespace World.Optix
{
    public class OptixSensor : MonoBehaviour
    {
        [Range(1, 30)]
        public float sensorDepth = 14f;
        [Range(1, 20)]
        public float sensorHeight = 10f;
        [Range(1, 360)]
        public float radius = 30f;
        [Range(0.001f, 1)]
        public float stride = 0.2f;

        public bool IsTransformDirty
        {
            get { return transform.hasChanged; }
            set { transform.hasChanged = value; }
        }

        public bool IsComponentDirty { get; set; }

        public OptixSensorBase OptixSensorBase
        {
            get
            {
                return new OptixSensorBase
                {
                    localToWorldTranslationMatrix = transform.localToWorldMatrix,

                    sensorDepth = sensorDepth,
                    sensorHeight = sensorHeight,
                    sensorRadius = radius,
                    pointGap = stride,
                    totalPoints = GetTotalPoints()
                };
            }
        }

        private Vector3 topLeft;
        private Vector3 topRight;
        private Vector3 botRight;
        private Vector3 botLeft;

        private Vector3 centre;
        private List<Vector3> topCurvePositions = new List<Vector3>();
        private List<Vector3> botCurvePositions = new List<Vector3>();

        private void UpdateValues()
        {
            centre = ((transform.position + (transform.forward * sensorDepth)) - transform.position);

            topRight = transform.position + (Quaternion.Euler(0, +(radius / 2), 0) * centre) + new Vector3(0f, sensorHeight / 2, 0f);
            topLeft = transform.position + (Quaternion.Euler(0, -(radius / 2), 0) * centre) + new Vector3(0f, sensorHeight / 2, 0f);
            botRight = transform.position + (Quaternion.Euler(0, +(radius / 2), 0) * centre) + new Vector3(0f, -sensorHeight / 2, 0f);
            botLeft = transform.position + (Quaternion.Euler(0, -(radius / 2), 0) * centre) + new Vector3(0f, -sensorHeight / 2, 0f);

            topCurvePositions.Clear();
            botCurvePositions.Clear();

            for (float iRadius = -(radius / 2); iRadius < (radius / 2); iRadius++)
            {
                topCurvePositions.Add(transform.position + (Quaternion.Euler(0, iRadius, 0) * centre) + new Vector3(0f, sensorHeight / 2, 0f));
                botCurvePositions.Add(transform.position + (Quaternion.Euler(0, iRadius + 1, 0) * centre) + new Vector3(0f, -sensorHeight / 2, 0f));
            }
        }

        void OnDrawGizmos()
        {
            UpdateValues(); // Should this be called in this function?

            Gizmos.color = Color.blue;

            for (int iCurvePos = 0; iCurvePos < botCurvePositions.Count - 1; iCurvePos++)
            {
                Gizmos.DrawLine(botCurvePositions[iCurvePos], botCurvePositions[iCurvePos + 1]);
                Gizmos.DrawLine(topCurvePositions[iCurvePos], topCurvePositions[iCurvePos + 1]);
            }

            Gizmos.DrawLine(transform.position, topLeft);
            Gizmos.DrawLine(transform.position, topRight);
            Gizmos.DrawLine(transform.position, botRight);
            Gizmos.DrawLine(transform.position, botLeft);

            Gizmos.DrawLine(topLeft, botLeft);
            Gizmos.DrawLine(topRight, botRight);
        }

        private float GetTotalPoints()
        {
            float rows = 0;
            float columns = 0;

            rows += Mathf.Ceil(sensorHeight / stride);
            columns += Mathf.Ceil(radius / stride);

            return (rows * columns);
        }

        private void OnValidate()
        {
            IsComponentDirty = true;
        }
    }
}
