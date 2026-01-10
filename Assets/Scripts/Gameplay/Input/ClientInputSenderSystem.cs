using System.Globalization;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[UpdateInGroup(typeof(GhostInputSystemGroup)), UpdateAfter(typeof(ClientInputReaderSystem))]
public partial class ClientInputSenderSystem : SystemBase
{
    private uint m_PreviouslySentTick;
    private ClientMovementInput m_InProgressCommandInput;
    private float m_DeltaTimeSinceLastTick;

    protected override void OnCreate()
    {
        RequireForUpdate<NetworkTime>();
    }

    protected override void OnUpdate()
    {
        var networkTime = SystemAPI.GetSingleton<NetworkTime>();
        var tick = networkTime.ServerTick;

        if (tick.IsValid)
        {
            var previousTick = m_PreviouslySentTick;
            var inProgressCommandInput = m_InProgressCommandInput;
            var deltaTimeSinceLastTick = m_DeltaTimeSinceLastTick;
            var deltaTime = World.Time.DeltaTime;
            var bufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>();

            foreach (var (input, entity)
                     in SystemAPI.Query<RefRO<ClientMovementInput>>()
                         .WithEntityAccess())
            {
                var buffer = bufferLookup[entity];
                if (tick.TickIndexForValidTick > previousTick)
                {
                    // this is a new tick.
                    var commandInput = new ClientCommandInput
                        { Tick = tick, ClientInterpolationTick = networkTime.InterpolationTick, };

                    commandInput.SetFrom(in input.ValueRO);
                    commandInput.UpdateFrom(inProgressCommandInput);
                    buffer.AddCommandData(commandInput);

                    // we can now start a new inprogress input
                    inProgressCommandInput = default;

                    previousTick = tick.TickIndexForValidTick;
                    deltaTimeSinceLastTick = 0f;
                }
                else
                {
                    deltaTimeSinceLastTick += deltaTime;

                    if (deltaTimeSinceLastTick >= 1 / 30f)
                    {
                        Debug.LogWarning(
                            $"[{UnityEngine.Time.frameCount.ToString()}] Overdue new command. dt is {deltaTimeSinceLastTick.ToString(CultureInfo.InvariantCulture)}");
                    }

                    // we've already sent this tick
                    // but we've still got client frames happening
                    // so let's record our input for sending when the tick next changes
                    inProgressCommandInput.UpdateFrom(in input.ValueRO);

                    // even though it's been sent, we add it again, so that the prediction
                    // system has the very latest data for this tick
                    if (buffer.GetDataAtTick(tick, out var existingCommandData)
                        && existingCommandData.Tick == tick)
                    {
                        existingCommandData.UpdateFrom(in input.ValueRO);
                        buffer.AddCommandData(existingCommandData);
                    }
                    else
                    {
                        Debug.LogError(
                            $"[ClientInputSenderSystem] Has processed this server tick, but it isn't in the command buffer");
                    }
                }
            }

            m_PreviouslySentTick = previousTick;
            m_InProgressCommandInput = inProgressCommandInput;
        }
    }
}