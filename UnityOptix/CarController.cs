using UnityEngine;
using UnityEngine.UI;

namespace World.Optix
{
    public class CarController : MonoBehaviour
    {
        public OptixController optixController;
        public Button sensorFire;

        public OptixSensor[] sensors;

        private void Start()
        {
            sensorFire.onClick.AddListener(ButtonClick);
        }

        private void ButtonClick()
        {
            optixController.RenderPointCloudFromHitPositionsContinuous(sensors);
        }
    }
}
