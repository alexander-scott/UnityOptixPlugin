using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace World.Optix
{
    public class OptixExampleUsage : MonoBehaviour
    {
        #region Vars

        public Button sensorFire;
        public OptixSensor[] sensors;

        [Space(10)]
        [Header("Optix Config")]

        [Tooltip("Change the frequency that the ray tracing execution will occur. 60 FPS will mean it happens every frame, providing something has changed within the scene to warrant the execution.")]
        public float optixTargetFPS = 60;
        [Tooltip("Set the layer mask that will be applied at initalisation when searching the scene for valid gameobjects. Gameobjects that are not within the set layermask will not be included in Optix.")]
        public LayerMask optixTargetLayerMask;
        [Tooltip("Set the tag that all gameobjects will be compared to at initalisation when searching the scene for valid gameobjects. Gameobjects that do not have the set tag will not be included in Optix. Leave blank to allow all tags.")]
        public string optixTargetTag;
        [Tooltip("Set the transform/position of the centre of the scene. Use in conjunction with the below variable to declude objects that are too far away from the origin.")]
        public Transform optixOrigin;
        [Tooltip("Set the maximum distance that an object can be to be included in Optix. Use in conjunction with the above variable to declude objects that are too far away from the origin.")]
        public float optixMaxDistanceFromOrigin;

        [Space(10)]
        [Header("Point Cloud Config")]

        [Tooltip("A reference to the mesh that will be instanced by the shader which will represent each individual point cloud point.")]
        public Mesh instanceMesh;
        [Tooltip("A reference to the material that contains the instance shader which will render the point cloud.")]
        public Material instanceMaterial;
        [Tooltip("The size of each point in the point cloud.")]
        public float pointCloudPointSize = 0.05f;
        [Tooltip("The colour of each point in the point cloud.")]
        public Color pointCloudPointColour;

        private OptixPointCloud optixPointCloud;
        private OptixController optixController;

        #endregion

        private void Start()
        {
            optixController = gameObject.AddComponent<OptixController>();
            optixController.Init(optixTargetFPS, optixTargetLayerMask, optixTargetTag, optixOrigin, optixMaxDistanceFromOrigin);

            optixPointCloud = gameObject.AddComponent<OptixPointCloud>();
            optixPointCloud.Init(instanceMesh, instanceMaterial, pointCloudPointColour, pointCloudPointSize);

            sensorFire.onClick.AddListener(ButtonClick);
        }

        private void ButtonClick()
        {
            optixPointCloud.StartRendering();
            optixController.RenderPointCloudFromSensorsContinuous(sensors, optixPointCloud);
        }
    }
}
