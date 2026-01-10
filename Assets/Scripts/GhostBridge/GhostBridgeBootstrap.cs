#if UNITY_EDITOR
using Unity.Scenes.Editor;
#endif
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

[ResetOnPlayMode(resetMethod: "ResetStaticState")]
[UnityEngine.Scripting.Preserve]
public class GhostBridgeBootstrap : ClientServerBootstrap
{
#pragma warning disable UDR0001
    public static GhostBridgeBootstrap Instance; // This is reset from ResetOnPlayMode attribute
#pragma warning restore UDR0001
    public new World ServerWorld { get; private set; }
    public new World ClientWorld { get; private set; }

    private GameObject m_ClientGameObjectHierarchy;
    public GameObject ClientGameObjectHierarchy
    {
        get
        {
            if (m_ClientGameObjectHierarchy == null)
            {
                // create top level gameobject
                m_ClientGameObjectHierarchy = new GameObject();
                m_ClientGameObjectHierarchy.name = "Client";
            }

            return m_ClientGameObjectHierarchy;
        }
    }

    private GameObject m_ServerGameObjectHierarchy;
    public GameObject ServerGameObjectHierarchy
    {
        get
        {
            if (m_ServerGameObjectHierarchy == null)
            {
                // create top level gameobject
                m_ServerGameObjectHierarchy = new GameObject();
                m_ServerGameObjectHierarchy.name = "Server";
            }

            return m_ServerGameObjectHierarchy;
        }
    }

    private Hash128 m_ClientGuid;
    public Hash128 ClientGuid => m_ClientGuid;

    public static bool TryGetInstance(out GhostBridgeBootstrap instance)
    {
        instance = Instance;
        return instance != null;
    }

    protected static void ResetStaticState()
    {
        // we need to ensure the static driver constructor override
        // is cleared, otherwise we might end up with a relay network driver
        // when in Ip mode
        NetworkStreamReceiveSystem.DriverConstructor = null;

        if (Instance != null)
        {
            Instance.DestroyMultiplayerWorlds();
            Instance = null;
        }
        // GhostGameObject.UpdateEntityCommandBuffer = default;
    }

    public override bool Initialize(string defaultWorldName)
    {
        Instance = this;

#if UNITY_SERVER
        // turn off stack trace for logs (it clutters the console window)
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
#endif

#if UNITY_EDITOR
        LiveConversionEditorSettings.LiveConversionEnabled = false;
#endif

        var world = new World(defaultWorldName, WorldFlags.Game);
        World.DefaultGameObjectInjectionWorld = world;

        bool createServerWorld = false;

#if UNITY_SERVER
        createServerWorld = true;
#elif UNITY_EDITOR
        // if we are in editor we only want a server on boot if we are simulating being the server
        // otherwise we'll defer the server world creation until we need it
        createServerWorld = RequestedPlayType == PlayType.Server;
#endif

        if (createServerWorld)
        {
            CreateServerWorld();
        }

        return true;
    }

    public void StartMultiplayerWorlds(bool asHost)
    {
        Debug.Log($"StartMultiplayerWorlds asHost {asHost}");

        GhostGameObjectUpdateSystem.Reset();

        // we no longer need the default world
        // so we can dispose it now. This unclutters the editor world selection UI
        // but also ensures we aren't loading our entities into the default world
        // which occasionally triggers the sporadic-and-unrecoverable "Loading Entity Scene failed" error
        if (World.DefaultGameObjectInjectionWorld != null)
        {
            World.DefaultGameObjectInjectionWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;
        }

        if (asHost)
        {
            var serverWorld = CreateServerWorld();
            GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
        }

        var clientWorld = CreateClientWorld();
        GhostGameObjectUpdateSystem.GatherWorldSystems(clientWorld);
    }

    private void SafelyQuitWorld(World world)
    {
        if (world != null)
        {
            world.QuitUpdate = true;
            world.EntityManager.CompleteAllTrackedJobs();
            ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
        }
    }

    public void DestroyMultiplayerWorlds()
    {
        if (ClientWorld != null)
        {
            DestroyWorld(ClientWorld);
        }

        if (ServerWorld != null)
        {
            DestroyWorld(ServerWorld);
        }
        
        if (m_ClientGameObjectHierarchy != null)
        {
            Object.Destroy(m_ClientGameObjectHierarchy);
            m_ClientGameObjectHierarchy = null;
        }

        if (m_ServerGameObjectHierarchy != null)
        {
            Object.Destroy(m_ServerGameObjectHierarchy);
            m_ServerGameObjectHierarchy = null;
        }

        GhostSingleton.InvokeAndClearStaticCallbacks();
    }

    public void QuitMultiplayerWorlds()
    {
        if (ClientWorld != null)
        {
            SafelyQuitWorld(ClientWorld);
        }

        if (ServerWorld != null)
        {
            SafelyQuitWorld(ServerWorld);
        }
    }

    private void DestroyWorld(World world)
    {
        if (world != null)
        {
            world.QuitUpdate = true;
            world.EntityManager.CompleteAllTrackedJobs();
            if (ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(world))
            {
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
            }
            world.Dispose();
        }
    }

    private World CreateClientWorld()
    {
        ClientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");

        // create unique client guid
        var hash128 = new UnityEngine.Hash128();
        hash128.Append(System.Guid.NewGuid().ToString());
        m_ClientGuid = hash128;

        return ClientWorld;
    }

    private World CreateServerWorld()
    {
        ServerWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
        return ServerWorld;
    }
}
