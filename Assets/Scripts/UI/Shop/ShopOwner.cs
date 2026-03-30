using System;
using System.Collections.Generic;
using Systems.Inventory;
using Unity.Netcode;
using UnityEngine;
using PlayerInventory = Systems.Inventory.Inventory;

namespace Systems.Shop
{
    /// <summary>
    /// Place on an NPC with a <see cref="NetworkObject"/> (spawned).
    /// Stock is replicated via <see cref="NetworkList{T}"/>; server seeds from each catalog <see cref="ItemDetails.StartingStock"/>.
    /// Setup: collider on this object (or child with forwarded <see cref="IInteractable"/>), assign <see cref="shopView"/>.
    /// With <see cref="populateCatalogFromResources"/>, catalog is filled from <see cref="ItemDatabase.ItemDetailsResourcesPath"/> in <see cref="Awake"/> (same assets as <see cref="ItemDatabase"/>).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class ShopOwner : NetworkBehaviour, IInteractable
    {
        [Tooltip("When true, Awake replaces catalog with every ItemDetails in Resources (same path as ItemDatabase — folder name \"ItemDetails\"), sorted by display name. Disable to assign catalog manually.")]
        [SerializeField]
        bool populateCatalogFromResources = true;

        [SerializeField] List<ItemDetails> catalog = new List<ItemDetails>();
        [SerializeField] ShopView shopView;

        readonly NetworkList<int> _stockRemaining = new NetworkList<int>();
        List<int> _offlineStock;

        public event Action OnStockChanged;

        public IReadOnlyList<ItemDetails> Catalog => catalog;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _stockRemaining.OnListChanged += OnNetworkStockListChanged;
            if (IsServer)
            {
                SeedNetworkStockFromCatalog();
            }
        }

        public override void OnNetworkDespawn()
        {
            _stockRemaining.OnListChanged -= OnNetworkStockListChanged;
            base.OnNetworkDespawn();
        }

        void OnNetworkStockListChanged(NetworkListEvent<int> changeEvent)
        {
            OnStockChanged?.Invoke();
        }

        void Awake()
        {
            if (populateCatalogFromResources)
            {
                LoadCatalogFromResources();
            }

            _offlineStock = null;
            EnsureOfflineStock();
        }

        /// <summary>Loads every <see cref="ItemDetails"/> from the same Resources path as <see cref="ItemDatabase"/>.</summary>
        public void LoadCatalogFromResources()
        {
            catalog.Clear();
            var loaded = Resources.LoadAll<ItemDetails>(ItemDatabase.ItemDetailsResourcesPath);
            var ordered = new List<ItemDetails>(loaded.Length);
            foreach (var item in loaded)
            {
                if (item != null)
                {
                    ordered.Add(item);
                }
            }

            ordered.Sort(CompareCatalogOrder);
            catalog.AddRange(ordered);
        }

        /// <summary>Sort by <see cref="ItemDetails.Type"/> (enum order), then display name, then asset name. Used for Resources catalog and shop UI rows.</summary>
        internal static int CompareCatalogOrder(ItemDetails a, ItemDetails b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }
            if (a == null)
            {
                return 1;
            }
            if (b == null)
            {
                return -1;
            }
            var byType = ((int)a.Type).CompareTo((int)b.Type);
            if (byType != 0)
            {
                return byType;
            }
            var byName = string.CompareOrdinal(a.DisplayName, b.DisplayName);
            return byName != 0 ? byName : string.CompareOrdinal(a.name, b.name);
        }

        [ContextMenu("Shop: Reload catalog from Resources")]
        void EditorReloadCatalogFromResources()
        {
            LoadCatalogFromResources();
            _offlineStock = null;
            EnsureOfflineStock();
        }

        void EnsureOfflineStock()
        {
            if (_offlineStock != null)
            {
                return;
            }
            _offlineStock = new List<int>();
            foreach (var def in catalog)
            {
                _offlineStock.Add(def != null ? def.StartingStock : 0);
            }
        }

        void SeedNetworkStockFromCatalog()
        {
            _stockRemaining.Clear();
            foreach (var def in catalog)
            {
                _stockRemaining.Add(def != null ? def.StartingStock : 0);
            }
        }

        public int GetStock(int index)
        {
            if (index < 0)
            {
                return 0;
            }
            if (IsSpawned && _stockRemaining.Count > index)
            {
                return _stockRemaining[index];
            }
            EnsureOfflineStock();
            return index < _offlineStock.Count ? _offlineStock[index] : 0;
        }

        public void Interact()
        {
            if (shopView == null)
            {
                Debug.LogWarning("[ShopOwner] ShopView not assigned.");
                return;
            }
            shopView.Show(this);
        }

        public void RequestPurchase(int itemIndex)
        {
            if (!IsSpawned)
            {
                TryPurchaseOffline(itemIndex);
                return;
            }
            TryPurchaseServerRpc(itemIndex);
        }

        void TryPurchaseOffline(int itemIndex)
        {
            EnsureOfflineStock();
            if (itemIndex < 0 || itemIndex >= catalog.Count || itemIndex >= _offlineStock.Count)
            {
                return;
            }
            var def = catalog[itemIndex];
            if (def == null)
            {
                return;
            }
            if (_offlineStock[itemIndex] <= 0)
            {
                return;
            }
            var buyerId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
            TryGetInventoryForLocalClient(out var inv);
            if (inv == null || !inv.TryAddItem(def, 1))
            {
                Debug.LogWarning("[ShopOwner] Could not add item to inventory (missing Inventory or full).");
                return;
            }
            if (!TryChargeTeamCash(def.ShopCost, out var cashFailReason))
            {
                inv.TryRemoveOne(def);
                if (cashFailReason != null)
                {
                    Debug.LogWarning($"[ShopOwner] Purchase blocked: {cashFailReason}");
                }
                return;
            }
            _offlineStock[itemIndex]--;
            OnStockChanged?.Invoke();
            ShopPurchaseEvents.RaiseServerPurchase(itemIndex, def, buyerId);
            Debug.Log($"[ShopOwner] Offline purchase: {def.DisplayName}");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void TryPurchaseServerRpc(int itemIndex, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }
            if (itemIndex < 0 || itemIndex >= catalog.Count)
            {
                return;
            }
            if (itemIndex >= _stockRemaining.Count)
            {
                return;
            }
            var def = catalog[itemIndex];
            if (def == null)
            {
                return;
            }
            var current = _stockRemaining[itemIndex];
            if (current <= 0)
            {
                return;
            }
            var buyerId = rpcParams.Receive.SenderClientId;
            GrantPurchasedItemClientRpc(buyerId, itemIndex);
        }

        /// <summary>
        /// Grants the item using <see cref="catalog"/> on the buyer's machine so we do not depend on
        /// <see cref="ItemDatabase"/> matching RPC-split GUIDs (same scene SO refs as server).
        /// </summary>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Everyone)]
        void GrantPurchasedItemClientRpc(ulong buyerClientId, int itemIndex)
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }
            if (NetworkManager.Singleton.LocalClientId != buyerClientId)
            {
                return;
            }
            if (itemIndex < 0 || itemIndex >= catalog.Count)
            {
                return;
            }
            var details = catalog[itemIndex];
            if (details == null)
            {
                Debug.LogWarning($"[ShopOwner] Grant purchase: catalog index {itemIndex} has no ItemDetails.");
                return;
            }
            if (!TryGetInventoryForLocalClient(out var inv))
            {
                Debug.LogWarning(
                    "[ShopOwner] No Inventory on local player. Is PlayerObject assigned? Try GetPlayerGameObject fallback — see TryGetInventoryForLocalClient.");
                return;
            }
            if (!inv.TryAddItem(details, 1))
            {
                Debug.LogWarning("[ShopOwner] Inventory full — item not added. No charge.");
                return;
            }
            ConfirmShopPurchaseServerRpc(itemIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ConfirmShopPurchaseServerRpc(int itemIndex, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }
            var sender = rpcParams.Receive.SenderClientId;
            if (itemIndex < 0 || itemIndex >= catalog.Count)
            {
                return;
            }
            if (itemIndex >= _stockRemaining.Count)
            {
                return;
            }
            var def = catalog[itemIndex];
            if (def == null)
            {
                return;
            }
            var current = _stockRemaining[itemIndex];
            if (current <= 0)
            {
                RollbackPurchasedItemClientRpc(sender, itemIndex);
                return;
            }
            if (!TryChargeTeamCash(def.ShopCost, out var cashFailReason))
            {
                if (cashFailReason != null)
                {
                    Debug.LogWarning($"[ShopOwner] Purchase finalize blocked: {cashFailReason}");
                }
                RollbackPurchasedItemClientRpc(sender, itemIndex);
                return;
            }
            _stockRemaining[itemIndex] = current - 1;
            ShopPurchaseEvents.RaiseServerPurchase(itemIndex, def, sender);
            Debug.Log($"[ShopOwner] Purchase finalized index {itemIndex} by client {sender} (cost {def.ShopCost} from team cash)");
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Everyone)]
        void RollbackPurchasedItemClientRpc(ulong buyerClientId, int itemIndex)
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }
            if (NetworkManager.Singleton.LocalClientId != buyerClientId)
            {
                return;
            }
            if (itemIndex < 0 || itemIndex >= catalog.Count)
            {
                return;
            }
            var details = catalog[itemIndex];
            if (details == null)
            {
                return;
            }
            if (!TryGetInventoryForLocalClient(out var inv))
            {
                return;
            }
            if (!inv.TryRemoveOne(details))
            {
                Debug.LogWarning("[ShopOwner] Could not roll back item after failed purchase finalize.");
            }
        }

        /// <summary>
        /// Resolves inventory for this machine's client. Includes inactive objects (inventory UI may sit on an inactive child).
        /// Falls back to <see cref="NetworkedGameManager.GetPlayerGameObject"/> when <see cref="NetworkClient.PlayerObject"/> is not set yet.
        /// </summary>
        static bool TryGetInventoryForLocalClient(out PlayerInventory inv)
        {
            inv = null;
            if (NetworkManager.Singleton == null)
            {
                return false;
            }
            var localId = NetworkManager.Singleton.LocalClientId;
            var playerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (playerObject != null)
            {
                inv = playerObject.GetComponentInChildren<PlayerInventory>(true);
                if (inv != null)
                {
                    return true;
                }
            }
            var gm = NetworkedGameManager.Instance;
            var go = gm != null ? gm.GetPlayerGameObject(localId) : null;
            if (go == null)
            {
                return false;
            }
            inv = go.GetComponentInChildren<PlayerInventory>(true);
            return inv != null;
        }

        /// <summary>
        /// Charges <see cref="NetworkedGameManager"/> team pool on the server. <paramref name="failureReason"/> is set when returning false (for logging).
        /// </summary>
        static bool TryChargeTeamCash(int cost, out string failureReason)
        {
            failureReason = null;
            if (cost <= 0)
            {
                return true;
            }
            var gm = NetworkedGameManager.Instance;
            if (gm == null)
            {
                failureReason = "No NetworkedGameManager.";
                return false;
            }
            if (!gm.IsServer)
            {
                failureReason = "Team cash can only be spent on the server.";
                return false;
            }
            if (gm.TotalCashValue < cost)
            {
                failureReason = $"Need {cost} cash; team has {gm.TotalCashValue:F2}.";
                return false;
            }
            return gm.TrySpendTeamCash(cost);
        }
    }

    /// <summary>
    /// Raised after a successful purchase (network server or offline). Subscribe for inventory / currency.
    /// </summary>
    public static class ShopPurchaseEvents
    {
        public static event Action<int, ItemDetails, ulong> OnServerPurchaseCompleted;

        public static void RaiseServerPurchase(int itemIndex, ItemDetails def, ulong buyerClientId)
        {
            OnServerPurchaseCompleted?.Invoke(itemIndex, def, buyerClientId);
        }
    }
}
