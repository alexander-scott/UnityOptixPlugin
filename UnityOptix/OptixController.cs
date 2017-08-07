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
    [AddComponentMenu("")]
    public class OptixController : MonoBehaviour
    {
        #region Vars

        private OptixTransform[] optixTransforms;
        private OptixSensor[] optixSensors;

        private bool isRaytracing;
        private bool sceneChanged;

        private float _optixTargetFPS;
        private LayerMask _optixTargetLayerMask;
        private string _optixTargetTag;
        private Transform _optixOrigin;
        private float _optixMaxDistanceFromOrigin;

        #endregion

        #region Init

        public void Init(float optixTargetFPS, LayerMask optixTargetLayerMask, string optixTargetTag, Transform optixOrigin, float optixMaxDistanceFromOrigin)
        {
            _optixTargetFPS = optixTargetFPS;
            _optixTargetLayerMask = optixTargetLayerMask;
            _optixTargetTag = optixTargetTag;
            _optixOrigin = optixOrigin;
            _optixMaxDistanceFromOrigin = optixMaxDistanceFromOrigin;

            CacheAllObjects();

            if (optixTransforms.Length == 0)
            {
                Debug.LogWarning("No gameobjects found in scene. Ending OptixController execution.");
                return;
            }

            SendAllObjectsToOptix();

            StartCoroutine(CallPluginAtEndOfFrames());
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
            if (_optixTargetLayerMask != (_optixTargetLayerMask | (1 << gameObject.layer))) // Checks to see if this gameobject is outside of the specified layermask
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_optixTargetTag) && !gameObject.CompareTag(_optixTargetTag)) // Checks to see if this gameobject doesn't have the specified tag
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

            for (int iGCHandle = 0; iGCHandle < meshGCHandles.Length; iGCHandle++) // Free the GCHandles as we no longer want the GC to avoid removing our verts
            {
                meshGCHandles[iGCHandle].Free();
            }
        }


        #endregion

        #region Update

        // Update loop that is used by continous executions such as raytracing with point cloud rendering. Deals with updating transform matrices, distance culling and point cloud updating, all at a user-defined frame rate
        private IEnumerator RaytracingCoroutine(OptixPointCloud optixPointCloud)
        {
            while (isRaytracing)
            {
                CheckOptixSensorTranslation();
                CheckEnabledStatusChanged();
                CheckMatrixChanged();

                if (sceneChanged)
                {
                    sceneChanged = false;
                    FireSensorAndUpdatePointCloud(optixPointCloud);
                }

                if (_optixTargetFPS == 0) // Prevents dividing by 0 error
                    yield return null;
                else
                    yield return new WaitForSeconds(1 / _optixTargetFPS);
            }
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
            if (_optixOrigin == null)
            {
                return 1;
            }

            if (Vector3.Distance(transform.position, _optixOrigin.position) < _optixMaxDistanceFromOrigin)
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
        public void RenderPointCloudFromSensorsContinuous(OptixSensor[] optixSensors, OptixPointCloud optixPointCloud)
        {
            this.optixSensors = optixSensors;
            OptixLibraryInterface.SetAllSensorsFromUnity(optixSensors.Length, GetBaseValuesFromSensors(optixSensors));

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

            StartCoroutine(RaytracingCoroutine(optixPointCloud));
        }

        /// <summary>
        /// Stops any continuous processes
        /// </summary>
        public void EndRaytracing()
        {
            isRaytracing = false;
        }

        private unsafe void FireSensorAndUpdatePointCloud(OptixPointCloud optixPointCloud)
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
