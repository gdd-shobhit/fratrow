using Unity.Collections;
using Unity.Entities;

public struct NetworkStatusSingleton : IComponentData
{
    public FixedString512Bytes Status;
}
