using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace World.Optix
{
    // TODO -- Do not regenerate rays if sensor transform has changed. Rather translate them using their localToWorld matrix
    // TODO -- Investigate rendering point cloud within C++ Plugin using D3D11 buffers
    // TODO -- Investigate CUDA implementation

    /// <summary>
    /// Simple data structure that caches an objects transform and mesh
    /// </summary>
    public class OptixTransform
    {
        public Transform transform;
        public Mesh mesh;
        public int isEnabled;

        public bool IsDirty
        {
            get { return transform.hasChanged; }
            set { transform.hasChanged = value; }
        }
    }

    /// <summary>
    /// Controller that allows you to modify settings and control Optix execution
    /// </summary>
    public class OptixController : MonoBehaviour
    {
        #region Vars

        [Header("Optix Config")]

        [Tooltip("Change the frequency that the ray tracing execution will occur. 60 FPS will mean it happens every frame, providing something has changed within the scene to warrant the execution.")]
        public float optixTargetFPS;
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
        public Color pointCloudPointColor;

        private OptixPointCloud optixPointCloud;
        private OptixTransform[] optixTransforms;
        private OptixSensor[] optixSensors;

        private bool isRaytracing;
        private bool sceneChanged;

        #endregion

        #region Init

        private void Start()
        {
            CacheAllObjects();

            if (optixTransforms.Length == 0)
            {
                Debug.LogWarning("No gameobjects found. Cancelling");
                return;
            }

            SendAllObjectsToOptix();

            StartCoroutine(CallPluginAtEndOfFrames());

            optixPointCloud = new OptixPointCloud(instanceMesh, instanceMaterial, this);
        }

        // Finds and caches all gameobjects in the scene which have a MeshFilter attached to them. TODO: Selectively cache gameobjects based on layer or tag or component.
        private void CacheAllObjects()
        {
            MeshFilter[] meshFilters = FindObjectsOfType<MeshFilter>();
            List<OptixTransform> optixTransformList = new List<OptixTransform>();

            for (int iMeshFilter = 0; iMeshFilter < meshFilters.Length; iMeshFilter++) // For each object in the scene that has a MeshFilter component attached to it
            {
                if (CheckGameObjectMeetsRequirements(meshFilters[iMeshFilter].gameObject))
                {
                    optixTransformList.Add(new OptixTransform
                    {
                        mesh = meshFilters[iMeshFilter].mesh,
                        transform = meshFilters[iMeshFilter].transform
                    });
                }
            }

            optixTransforms = optixTransformList.ToArray();
        }

        // Checks to see if the gameobjects found by FindObjectsOfType adhere to the specified requirements such as layermask and tag
        private bool CheckGameObjectMeetsRequirements(GameObject gameObject)
        {
            if (optixTargetLayerMask != (optixTargetLayerMask | (1 << gameObject.layer))) // Checks to see if this gameobject is outside of the specified layermask
            {
                return false;
            }

            if (!string.IsNullOrEmpty(optixTargetTag) && !gameObject.CompareTag(optixTargetTag)) // Checks to see if this gameobject doesn't have the specified tag
            {
                return false;
            }

            return true;
        }

        // Sends the address of every cached meshes vertex buffer as well as localToWorld transform matrix to the Optix library
        private void SendAllObjectsToOptix()
        {
            GCHandle[] meshGCHandles = new GCHandle[optixTransforms.Length]; 
            IntPtr[] meshVertexAddresses = new IntPtr[optixTransforms.Length]; 
            Matrix4x4[] meshTranslationMatrices = new Matrix4x4[optixTransforms.Length]; 
            int[] meshVertexCounts = new int[optixTransforms.Length]; 
            int[] meshEnabledStatus = new int[optixTransforms.Length]; 

            for (int iTransform = 0; iTransform < optixTransforms.Length; iTransform++)
            {
                // GCHandle is needed to find the memory address of the meshes vertices without doing an expensive copy
                meshGCHandles[iTransform] = GCHandle.Alloc(optixTransforms[iTransform].mesh.vertices, GCHandleType.Pinned);

                // The array pointer to the memory address of the verts
                meshVertexAddresses[iTransform] = meshGCHandles[iTransform].AddrOfPinnedObject();

                // The local to world matrix of each OptixTransform
                meshTranslationMatrices[iTransform] = optixTransforms[iTransform].transform.localToWorldMatrix;

                // The number of vertices in the mesh
                meshVertexCounts[iTransform] = optixTransforms[iTransform].mesh.vertexCount;

                // The enabled status of each gameobject (is it within the required distance or not)
                optixTransforms[iTransform].isEnabled = CheckIfTransformIsEnabled(optixTransforms[iTransform].transform);
                meshEnabledStatus[iTransform] = optixTransforms[iTransform].isEnabled;
            }

            OptixLibraryInterface.SetAllObjectsFromUnity(optixTransforms.Length, meshVertexCounts, meshVertexAddresses, meshTranslationMatrices, meshEnabledStatus);

            for (int iGCHandle = 0; iGCHandle < meshGCHandles.Length; iGCHandle++) // Free the GCHandles as we no longer have a use for them
            {
                meshGCHandles[iGCHandle].Free();
            }
        }

        // Release the compute buffers in the point cloud
        private void OnApplicationQuit()
        {
            if (optixPointCloud != null)
                optixPointCloud.StopRendering();
        }

        #endregion

        #region Update

        // Update loop that is used by continous executions such as raytracing with point cloud rendering. Deals with updating transform matrices, distance culling and point cloud updating, all at a user-defined frame rate
        private IEnumerator RaytracingCoroutine()
        {
            while (isRaytracing)
            {
                CheckOptixSensorTranslation();
                CheckEnabledStatusChanged();
                CheckMatrixChanged();

                if (sceneChanged)
                {
                    sceneChanged = false;
                    FireSensorAndUpdatePointCloud();
                }

                if (optixTargetFPS == 0) // Prevents dividing by 0 error
                    yield return null;
                else
                    yield return new WaitForSeconds(1 / optixTargetFPS);
            }
        }

        // Loop that deals with rendering the point cloud. This must happen every frame because if it doesn't then one frame the point cloud won't get rendered and it will look all jittery
        private IEnumerator RenderPointCloudCoroutine()
        {
            optixPointCloud.StartRendering();

            while (isRaytracing)
            {
                optixPointCloud.Update();

                yield return null;
            }

            optixPointCloud.StopRendering();
        }

        // Check if a transforms enabled status has changed and if it has update it in Optix
        private void CheckEnabledStatusChanged()
        {
            List<int> updateTransformEnabled = new List<int>();
            List<int> updateIndices = new List<int>();

            for (int iTransform = 0; iTransform < optixTransforms.Length; iTransform++)
            {
                int transformEnabled = CheckIfTransformIsEnabled(optixTransforms[iTransform].transform);

                if (optixTransforms[iTransform].isEnabled != transformEnabled) // Check if the transforms enabled status has changed
                {
                    optixTransforms[iTransform].isEnabled = transformEnabled;
                    updateIndices.Add(iTransform);
                    updateTransformEnabled.Add(transformEnabled); 
                }               
            }

            if (updateTransformEnabled.Count > 0)
            {
                OptixLibraryInterface.UpdateTransformEnabled(updateTransformEnabled.Count, updateIndices.ToArray(), updateTransformEnabled.ToArray());
                sceneChanged = true;
            }  
        }

        // Check if a transforms local to world matrix has changed and if it has update it in Optix
        private void CheckMatrixChanged()
        {
            List<Matrix4x4> updateMatricies = new List<Matrix4x4>();
            List<int> updateIndices = new List<int>();

            for (int iTransform = 0; iTransform < optixTransforms.Length; iTransform++)
            {
                if (optixTransforms[iTransform].IsDirty) // isDirty is set to true if any value within the object's transform has changed
                {
                    optixTransforms[iTransform].IsDirty = false;

                    updateMatricies.Add(optixTransforms[iTransform].transform.localToWorldMatrix);
                    updateIndices.Add(iTransform); // Pass the index of the transform so we know which matrix to update in Optix
                }
            }

            if (updateMatricies.Count > 0)  // If no enabled status' have changed, just update the matrices
            {
                OptixLibraryInterface.UpdateMatrices(updateMatricies.Count, updateIndices.ToArray(), updateMatricies.ToArray());
                sceneChanged = true;
            }
        }

        // Check if any of the sensors have had their transform changed or a value in the optix sensor component changed
        private void CheckOptixSensorTranslation()
        {
            bool updateSensors = false;

            for (int iSensor = 0; iSensor < optixSensors.Length; iSensor++)
            {
                if (optixSensors[iSensor].IsDirty)
                {
                    optixSensors[iSensor].IsDirty = false; 
                    updateSensors = true;
                }
            }

            if (updateSensors)
            {
                OptixLibraryInterface.SetAllSensorsFromUnity(optixSensors.Length, GetBaseValuesFromSensors(optixSensors));
                sceneChanged = true;
            }
        }

        // Checks that a transform is within a certain distance of the optix origin and returns true or false (using ints to make data interop debugging easier)
        private int CheckIfTransformIsEnabled(Transform transform)
        {
            if (optixOrigin == null)
            {
                return 1;
            }

            if (Vector3.Distance(transform.position, optixOrigin.position) < optixMaxDistanceFromOrigin)
            {
                return 1;
            }

            return 0;
        }

        #endregion

        #region Single execution

        /// <summary>
        /// Checks a ray to see if it intersects with any gameobjects. Runs once.
        /// </summary>
        public bool CheckSingleRayHit(Vector3 origin, Vector3 direction, float depth)
        {
            CheckEnabledStatusChanged();
            CheckMatrixChanged();

            if (OptixLibraryInterface.CheckSingleRayHit(origin, direction, depth))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks a ray to see if it intersects with any gameobjects. Runs once.
        /// </summary>
        public bool CheckSingleRayHit(Ray ray, float depth)
        {
            CheckEnabledStatusChanged();
            CheckMatrixChanged();

            if (OptixLibraryInterface.CheckSingleRayHit(ray.origin, ray.direction, depth))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the hit position of a ray. If the ray doesn't intersect with any gameobject the returned position will be equal to Vector3.zero. Runs once.
        /// </summary>
        public Vector3 ReturnHitPositionFromSingleRay(Ray ray, float depth)
        {
            CheckEnabledStatusChanged();
            CheckMatrixChanged();

            return OptixLibraryInterface.ReturnSingleRayHit(ray.origin, ray.direction, depth);
        }

        /// <summary>
        /// Returns the hit position of a ray. If the ray doesn't intersect with any gameobject the returned position will be equal to Vector3.zero. Runs once.
        /// </summary>
        public Vector3 ReturnHitPositionFromSingleRay(Vector3 origin, Vector3 direction, float depth)
        {
            CheckEnabledStatusChanged();
            CheckMatrixChanged();

            return OptixLibraryInterface.ReturnSingleRayHit(origin, direction, depth);
        }

        /// <summary>
        /// Debug.Logs the number of hit positions from single/multiple optix sensors. Runs once.
        /// </summary>
        /// <param name="optixSensors"></param>
        public int ReturnNumberOfHitPositionsSingle(OptixSensor[] optixSensors)
        {
            CheckEnabledStatusChanged();
            CheckMatrixChanged();

            this.optixSensors = optixSensors;
            OptixLibraryInterface.SetAllSensorsFromUnity(optixSensors.Length, GetBaseValuesFromSensors(optixSensors));

            int returnHitPositionCount;
            OptixLibraryInterface.SensorFireAndReturnHitCount(out returnHitPositionCount);
            return returnHitPositionCount;
        }

        #endregion

        #region Continuous execution

        /// <summary>
        /// Renders the hit positions from single/multiple optix sensors into a point cloud. Runs continually until stopped by EndRaytracing().
        /// </summary>
        public void RenderPointCloudFromHitPositionsContinuous(OptixSensor[] optixSensors)
        {
            this.optixSensors = optixSensors;
            OptixLibraryInterface.SetAllSensorsFromUnity(optixSensors.Length, GetBaseValuesFromSensors(optixSensors)); 

            StartRaytracing();
        }

        /// <summary>
        /// Stops any continuous processes
        /// </summary>
        public void EndRaytracing()
        {
            isRaytracing = false;
        }

        private void StartRaytracing()
        {
            if (isRaytracing)
            {
                Debug.LogWarning("Attempting to start raytracing even though it is already executing.");
                return;
            }

            if (optixSensors == null)
            {
                Debug.LogWarning("Cannot have optixSensors set to null. Cancelling raytracing.");
                return;
            }

            isRaytracing = true;

            StartCoroutine(RenderPointCloudCoroutine());
            StartCoroutine(RaytracingCoroutine());
        }

        private unsafe void FireSensorAndUpdatePointCloud()
        {
            Vector3* returnHitPositions;
            int returnHitPositionCount;
            using (OptixLibraryInterface.SensorFireAndReturnHitPositions(out returnHitPositions, out returnHitPositionCount))
            {
                optixPointCloud.UpdatePositions(returnHitPositions, returnHitPositionCount);
            }
        }
#endregion

#region Helpers

        private OptixSensorBase[] GetBaseValuesFromSensors(OptixSensor[] sensors)
        {
            OptixSensorBase[] baseSensors = new OptixSensorBase[sensors.Length];

            for (int iSensor = 0; iSensor < sensors.Length; iSensor++)
            {
                baseSensors[iSensor] = sensors[iSensor].OptixSensorBase;
            }

            return baseSensors;
        }

#endregion

        private IEnumerator CallPluginAtEndOfFrames()
        {
            while (true)
            {
                // Wait until all frame rendering is done
                yield return new WaitForEndOfFrame();

                // Set time for the plugin
                OptixLibraryInterface.SetTimeFromUnity(Time.realtimeSinceStartup);

                // Issue a plugin event with arbitrary integer identifier.
                // The plugin can distinguish between different
                // things it needs to do based on this ID.
                // For our simple plugin, it does not matter which ID we pass here.
                GL.IssuePluginEvent(OptixLibraryInterface.GetRenderEventFunc(), 1);
            }
        }

    }
}
