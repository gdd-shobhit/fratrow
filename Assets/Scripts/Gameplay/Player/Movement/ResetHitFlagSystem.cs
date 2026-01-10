using Unity.Entities;

namespace Unity.FPSSample_2
{
    // Its only job is to clean up the IsHit flag from the previous frame.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class ResetHitFlagSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // Reset IsHit flag for all players at the start of the frame.
            // This ensures any hit from the PREVIOUS frame is cleared before
            // any new hits from the CURRENT frame are processed.
            foreach (var playerGhost in SystemAPI.Query<RefRW<PredictedPlayerGhost>>())
            {
                if (playerGhost.ValueRO.ControllerState.IsHit)
                {
                    playerGhost.ValueRW.ControllerState.IsHit = false;
                }
            }
        }
    }
}