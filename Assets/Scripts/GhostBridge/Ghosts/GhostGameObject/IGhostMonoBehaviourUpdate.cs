public interface IGhostMonoBehaviourUpdate {}

public interface IEarlyUpdateServer : IGhostMonoBehaviourUpdate
{
    void EarlyUpdateServer(float deltaTime);
}

public interface IUpdateServer : IGhostMonoBehaviourUpdate
{
    void UpdateServer(float deltaTime);
}

public interface IPhysicsUpdateServer : IGhostMonoBehaviourUpdate
{
    void PhysicsUpdateServer(float deltaTime);
}

public interface IEarlyUpdateClient : IGhostMonoBehaviourUpdate
{
    void EarlyUpdateClient(float deltaTime);
}

public interface IUpdateClient : IGhostMonoBehaviourUpdate
{
    void UpdateClient(float deltaTime);
}

public interface ILateUpdateClient : IGhostMonoBehaviourUpdate
{
    void LateUpdateClient(float deltaTime);
}
