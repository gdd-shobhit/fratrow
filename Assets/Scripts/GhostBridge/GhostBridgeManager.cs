using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.GhostBridge
{

    public class GhostBridgeManager : MonoBehaviour
    {
        public static GhostBridgeManager Instance { get; private set; } = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            DontDestroyOnLoad(this);
        }

#region Server functions
        private NetworkStreamDriver _serverNetworkStreamDriver;

        public void SetServerNetworkStreamDriver(NetworkStreamDriver serverNetworkStreamDriver)
        {
            _serverNetworkStreamDriver = serverNetworkStreamDriver;
        }
        
        public bool IsServerListening()
        {
            var driverStore = _serverNetworkStreamDriver.DriverStore; 
            if (driverStore.IsCreated && driverStore.DriversCount > 0)
            {
                int driverId = _serverNetworkStreamDriver.DriverStore.FirstDriver;
                return driverStore.GetDriverInstanceRO(driverId).driver.IsCreated &&
                       driverStore.GetDriverInstanceRO(driverId).driver.Listening;
            }
            return false;
        }

        public bool TryGetServerEntityManager(out EntityManager manager)
        {
            World serverWorld = null;
            foreach (var world in World.All)
            {
                if ((world.Flags & WorldFlags.GameServer) == WorldFlags.GameServer)
                {
                    serverWorld = world;
                    break;
                }
            }

            if (serverWorld != null)
            {
                manager = serverWorld.EntityManager;
                return true;
            }
            else
            {
                manager = default(EntityManager);
                return false;    
            }
        }
#endregion End of Server functions
        
#region Client functions
        public struct LocalPlayerInfo
        {
            public FixedString64Bytes PlayerName;
            public uint InputUserId;
        };
        
        public LocalPlayerInfo LocalPlayer;

    }
#endregion End of Client functions    
}