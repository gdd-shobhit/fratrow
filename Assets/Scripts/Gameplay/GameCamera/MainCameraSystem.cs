using Unity.Entities;
using Unity.Transforms;

namespace Unity.FPSSample_2
{
    /// <summary>
    /// Updates the <see cref="MainGameObjectCamera"/> postion to match the current player <see cref="MainCamera"/> component position if it exists.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MainCameraSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MainCamera>();
        }

        protected override void OnUpdate()
        {
            if (MainCameraSingleton.Instance != null)
            {
                EntityManager.CompleteAllTrackedJobs();
                Entity mainEntityCameraEntity = SystemAPI.GetSingletonEntity<MainCamera>();
                MainCamera mainCamera = SystemAPI.GetSingleton<MainCamera>();
                LocalToWorld targetLocalToWorld = SystemAPI.GetComponent<LocalToWorld>(mainEntityCameraEntity);
                MainCameraSingleton.Instance.transform.SetPositionAndRotation(targetLocalToWorld.Position,targetLocalToWorld.Rotation);
                MainCameraSingleton.Instance.fieldOfView = mainCamera.CurrentFov = mainCamera.BaseFov;
            }
        }
    }
}