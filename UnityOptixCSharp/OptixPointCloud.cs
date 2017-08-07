using System.Collections;
using UnityEngine;

namespace World.Optix
{
    /// <summary>
    /// Renders an array of positions to a point cloud using the low-level function, Graphics.DrawMeshInstancedIndirect().
    /// </summary>
    [AddComponentMenu("")]
    public class OptixPointCloud : MonoBehaviour
    {
        #region Vars

        private int instanceCount = -1;
        private ComputeBuffer positionBuffer;
        private ComputeBuffer argsBuffer;

        private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

        private Mesh _instancedMesh;
        private Material _instancedMaterial;
        private Color _pointCloudPointColour;
        private float _pointCloudPointSize;

        #endregion

        public void Init(Mesh instancedMesh, Material instancedMaterial, Color pointCloudPointColour, float pointCloudPointSize)
        {
            _instancedMesh = instancedMesh;
            _instancedMaterial = instancedMaterial;
            _pointCloudPointColour = pointCloudPointColour;
            _pointCloudPointSize = pointCloudPointSize;
        }

        /// <summary>
        /// Set the position data that is used to render the point cloud
        /// </summary>
        /// <param name="positionData"></param>
        /// <param name="positionDataCount"></param>
        public unsafe void UpdatePositions(Vector3* positionData, int positionDataCount)
        {
            instanceCount = positionDataCount;

            if (instanceCount == 0)
                return; // Do not attempt to update buffers if there is no data to update them with. Prevents error.

            if (positionBuffer != null)
                positionBuffer.Release();

            positionBuffer = new ComputeBuffer(instanceCount, 16);

            Vector4[] positions = new Vector4[instanceCount];

            for (int iPositionData = 0; iPositionData < positionDataCount; iPositionData++)
            { 
                positions[iPositionData] = new Vector4(positionData[iPositionData].x, positionData[iPositionData].y, positionData[iPositionData].z, _pointCloudPointSize);
            }

            positionBuffer.SetData(positions);

            _instancedMaterial.SetBuffer("positionBuffer", positionBuffer);
            _instancedMaterial.SetColor("_PointColor", _pointCloudPointColour);

            // indirect args
            uint numIndices = (_instancedMesh != null) ? (uint)_instancedMesh.GetIndexCount(0) : 0;
            args[0] = numIndices;
            args[1] = (uint)instanceCount;

            argsBuffer.SetData(args);
        }

        /// <summary>
        /// Instruct the point cloud to start the render loop
        /// </summary>
        public void StartRendering()
        {
            instanceCount = 0;
            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            StartCoroutine(RenderPointCloudCoroutine());
        }

        /// <summary>
        /// End the point cloud render loop and release the buffers
        /// </summary>
        public void StopRendering()
        {
            if (positionBuffer != null) positionBuffer.Release();
            positionBuffer = null;

            if (argsBuffer != null) argsBuffer.Release();
            argsBuffer = null;

            instanceCount = -1;
        }

        private bool Update()
        {
            if (instanceCount < 0 || argsBuffer == null)
                return false;

            // Render
            Graphics.DrawMeshInstancedIndirect(_instancedMesh, 0, _instancedMaterial, new Bounds(Vector3.zero, new Vector3(1000.0f, 1000.0f, 1000.0f)), argsBuffer, 0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 9);
            return true;
        }

        // Loop that deals with rendering the point cloud. This must happen every frame because if it doesn't then one frame the point cloud won't get rendered and it will look all jittery
        private IEnumerator RenderPointCloudCoroutine()
        {
            while (Update())
            {
                yield return null;
            }

            StopRendering();
        }

        private void OnApplicationQuit()
        {
            StopRendering();
        }
    }
}
