using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Updates the lobby's player slot visuals (Player1, 2, 3, 4) based on LobbyManager data.
/// Enables slots when players join, disables when they leave, and applies mesh/material when they change.
/// Add this to LobbySystems and assign the 4 player GameObjects.
/// </summary>
public class LobbyPlayerPreview : MonoBehaviour
{
    [Header("Player Slot References")]
    [Tooltip("Assign Player1–4 from the Lobby scene. If null, will try to find by name.")]
    [SerializeField] private GameObject player1;
    [SerializeField] private GameObject player2;
    [SerializeField] private GameObject player3;
    [SerializeField] private GameObject player4;

    [Header("Fallback Names (used if references are null)")]
    [SerializeField] private string player1Name = "Player1";
    [SerializeField] private string player2Name = "Player2";
    [SerializeField] private string player3Name = "Player3";
    [SerializeField] private string player4Name = "Player4";

    private GameObject[] _playerSlots;

    private void Awake()
    {
        ResolvePlayerSlots();
        for (int i = 0; i < _playerSlots.Length; i++)
        {
            if (_playerSlots[i] != null)
                _playerSlots[i].SetActive(false);
        }
    }

    private void ResolvePlayerSlots()
    {
        if (player1 == null) player1 = GameObject.Find(player1Name);
        if (player2 == null) player2 = GameObject.Find(player2Name);
        if (player3 == null) player3 = GameObject.Find(player3Name);
        if (player4 == null) player4 = GameObject.Find(player4Name);

        _playerSlots = new[] { player1, player2, player3, player4 };
    }

    private void OnEnable()
    {
        // Fallback to resolve player slots if they are not set
        if (_playerSlots == null)
            ResolvePlayerSlots();

        if (LobbyManager.Instance == null) return;

        // Subscribe to lobby manager events
        var lm = LobbyManager.Instance;
        lm.OnPlayerAdded += OnPlayerAdded;
        lm.OnPlayerRemoved += OnPlayerRemoved;
        lm.OnPlayerMeshChanged += OnPlayerMeshChanged;
        lm.OnPlayerMaterialChanged += OnPlayerMaterialChanged;

        RefreshAllSlots();
        StartCoroutine(DelayedRefreshAfterJoin());
    }

    /// <summary>
    /// Catches lobby state that arrives after we subscribe. On client, PlayerJoinedLobbyRPC can arrive
    /// after Lobby loads; this delayed refresh ensures the preview models appear shortly after join.
    /// </summary>
    private IEnumerator DelayedRefreshAfterJoin()
    {
        yield return new WaitForSeconds(0.5f);
        RefreshAllSlots();
    }

    private void OnDisable()
    {
        if (LobbyManager.Instance == null) return;

        var lm = LobbyManager.Instance;
        lm.OnPlayerAdded -= OnPlayerAdded;
        lm.OnPlayerRemoved -= OnPlayerRemoved;
        lm.OnPlayerMeshChanged -= OnPlayerMeshChanged;
        lm.OnPlayerMaterialChanged -= OnPlayerMaterialChanged;
    }

    private void OnPlayerAdded(LobbyPlayerData data)
    {
        int slot = GetSlotForClient(data.NetworkId);
        if (slot >= 0 && slot < _playerSlots.Length && _playerSlots[slot] != null)
        {
            _playerSlots[slot].SetActive(true);
            ApplyAppearance(slot, data);
        }
    }

    private void OnPlayerRemoved(ulong clientId)
    {
        // Reassign all slots so remaining players fill first N slots (slot 0, 1, ...)
        RefreshAllSlots();
    }

    private void OnPlayerMeshChanged(ulong clientId, int _)
    {
        if (LobbyManager.Instance == null) return;
        if (!LobbyManager.Instance.Players.TryGetValue(clientId, out var data)) return;

        int slot = GetSlotForClient(clientId);
        if (slot >= 0)
            ApplyAppearance(slot, data);
    }

    private void OnPlayerMaterialChanged(ulong clientId, int _)
    {
        if (LobbyManager.Instance == null) return;
        if (!LobbyManager.Instance.Players.TryGetValue(clientId, out var data)) return;

        int slot = GetSlotForClient(clientId);
        if (slot >= 0)
            ApplyAppearance(slot, data);
    }

    /// <summary>
    /// Maps clientId to slot by lobby order: first player = slot 0, second = slot 1, etc.
    /// This ensures reconnecting clients reclaim the first available slot instead of using their new clientId.
    /// </summary>
    private int GetSlotForClient(ulong clientId)
    {
        if (LobbyManager.Instance == null || _playerSlots == null) return -1;
        var sortedIds = LobbyManager.Instance.Players.Keys.OrderBy(id => id).ToList();
        int index = sortedIds.IndexOf(clientId);
        if (index < 0 || index >= _playerSlots.Length) return -1;
        return index;
    }

    private void RefreshAllSlots()
    {
        for (int i = 0; i < _playerSlots.Length; i++)
        {
            if (_playerSlots[i] != null)
                _playerSlots[i].SetActive(false);
        }

        if (LobbyManager.Instance == null) return;

        foreach (var kvp in LobbyManager.Instance.Players)
        {
            OnPlayerAdded(kvp.Value);
        }
    }

    private void ApplyAppearance(int slot, LobbyPlayerData data)
    {
        if (slot < 0 || slot >= _playerSlots.Length || _playerSlots[slot] == null) return;

        var go = _playerSlots[slot];
        var meshList = LobbyManager.Instance?.MeshList;
        var materialList = LobbyManager.Instance?.MaterialList;

        // SkinnedMeshRenderer (typical for characters)
        var skinnedRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var r in skinnedRenderers)
        {
            if (meshList != null && meshList.Count > 0)
            {
                int idx = ClampIndex(data.SelectedMeshIndex, meshList.Count);
                if (meshList[idx] != null)
                    r.sharedMesh = meshList[idx];
            }
            if (materialList != null && materialList.Count > 0)
            {
                int idx = ClampIndex(data.SelectedMaterialIndex, materialList.Count);
                if (materialList[idx] != null)
                    r.sharedMaterial = materialList[idx];
            }
        }

        // MeshFilter + MeshRenderer (fallback for non-skinned meshes)
        var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
        var meshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
        if (meshFilters.Length > 0 && meshList != null && meshList.Count > 0)
        {
            int idx = ClampIndex(data.SelectedMeshIndex, meshList.Count);
            if (meshList[idx] != null)
            {
                foreach (var mf in meshFilters)
                    mf.sharedMesh = meshList[idx];
            }
        }
        if (meshRenderers.Length > 0 && materialList != null && materialList.Count > 0)
        {
            int idx = ClampIndex(data.SelectedMaterialIndex, materialList.Count);
            if (materialList[idx] != null)
            {
                foreach (var mr in meshRenderers)
                    mr.sharedMaterial = materialList[idx];
            }
        }
    }

    private static int ClampIndex(int index, int count)
    {
        if (count <= 0) return 0;
        return ((index % count) + count) % count;
    }
}
