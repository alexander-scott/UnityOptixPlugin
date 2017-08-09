using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace World.Optix
{
    public struct OptixSensorBase
    {
        public Matrix4x4 localToWorldTranslationMatrix;

        public float sensorDepth;
        public float sensorHeight;
        public float sensorRadius;

        public float pointGap;
        public float totalPoints;
    }

    /// <summary>
    /// The interface for accessing the Optix library. Should only be used through the OptixController.
    /// </summary>
    public class OptixLibraryInterface : SafeHandleZeroOrMinusOneIsInvalid
    {
        #region Init Optix Functions

        [DllImport("unityOptixPlugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetAllObjectsFromUnity
        (
            int totalMeshes, int[] vertexCount, IntPtr[] sourceVertices, Matrix4x4[] transformationMatrices, int[] transformEnabled
        );

        [DllImport("unityOptixPlugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetAllSensorsFromUnity
        (
            int totalSensors, OptixSensorBase[] sensors
        );

        #endregion

        #region Update Optix Functions

        [DllImport("unityOptixPlugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern void TranslateAllSensorsFromUnity
        (
            int totalSensors, OptixSensorBase[] sensors
        );

        [DllImport("unityOptixPlugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern void UpdateGameObjectMatrixFromUnity
        (
            int matrixCount, int[] matrixIndices, Matrix4x4[] transformationMatrices
        );

        [DllImport("unityOptixPlugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern void UpdateGameObjectEnabledFromUnity
        (
            int transformCount, int[] transformIndices, int[] transformEnabled
        );

        #endregion

        #region Sensor fire

        [DllImport("unityOptixPlugin", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static unsafe extern bool SensorFireAndReturnHitPositions
        (
            out OptixLibraryInterface optixLibraryHandle, out Vector3* hitPositions, out int hitPositionCount
        );

        [DllImport("unityOptixPlugin", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SensorFireAndReturnHitCount
        (
            out int hitPositionCount
        );

        #endregion

        #region Single ray fire

        [DllImport("unityOptixPlugin", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool CheckSingleRayHit
        (
            Vector3 origin, Vector3 direction, float depth
        );

        [DllImport("unityOptixPlugin", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern Vector3 ReturnSingleRayHit
        (
            Vector3 origin, Vector3 direction, float depth
        );

        #endregion

        [DllImport("unityOptixPlugin", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static unsafe extern bool ReleaseItems
        (
            IntPtr optixLibraryHandle
        );

        [DllImport("unityOptixPlugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetRenderEventFunc();

        [DllImport("unityOptixPlugin", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetTimeFromUnity(float t);

        public OptixLibraryInterface()
            : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return ReleaseItems(handle);
        }

        // This function is unsafe because memory as been allocated on the plugin side. When you have finished with the results from the function,
        // the ReleaseHandle function automatically passes the handle back to the plugin side which deallocates it.
        public static unsafe OptixLibraryInterface SensorFireAndReturnHitPositions(out Vector3* hitPositions, out int hitPositionCount)
        {
            OptixLibraryInterface optixLibraryHandle;

            if (!SensorFireAndReturnHitPositions(out optixLibraryHandle, out hitPositions, out hitPositionCount))
            {
                throw new InvalidOperationException();
            }

            return optixLibraryHandle;
        }
    }
}
