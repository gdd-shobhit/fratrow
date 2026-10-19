using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using FratRow.Social;
using Systems.Inventory;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>Development-only command-file driver. This is a test input adapter, not a phone UI.</summary>
public sealed class SocialDevelopmentDriver : MonoBehaviour
{
    [Serializable] public sealed class Command
    {
        public string requestId;
        public string action = "talk";
        public string npcId = "Ashley";
        public string text;
        public string channel = "text";
        public string partyId;
    }
    string lastCommand;
    bool running, loaded;
    float poll;
    readonly Queue<float> frameTimes = new Queue<float>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!(Application.isEditor || Debug.isDebugBuild) || !Environment.GetCommandLineArgs().Contains("--social-brains")) return;
        if (FindFirstObjectByType<SocialDevelopmentDriver>() != null) return;
        var go = new GameObject("SocialDevelopmentDriver"); DontDestroyOnLoad(go); go.AddComponent<SocialDevelopmentDriver>();
    }
    IEnumerator Start()
    {
        yield return null; yield return null;
        string role = NetworkedSocialConversation.Option("--social-local-role");
        if (role == null) yield break;
        if (!NetworkBootstrap.Instance.EnsureNetworkManagerReady()) yield break;
        var network = NetworkManager.Singleton;
        var transport = network.GetComponent<UnityTransport>();
        if (transport == null) throw new InvalidOperationException("UnityTransport missing");
        transport.SetConnectionData("127.0.0.1", ushort.Parse(NetworkedSocialConversation.Option("--social-port", "7781")), "127.0.0.1");
        EquippedPrefabRegistrar.RegisterAllEquippedPrefabs();
        if (role == "host") network.StartHost(); else if (role == "client") network.StartClient();
    }
    void Update()
    {
        var network = NetworkManager.Singleton;
        if (!loaded && network != null && network.IsServer && NetworkedSocialConversation.Option("--social-local-role") == "host"
            && LobbyManager.Instance != null && LobbyManager.Instance.PlayerCount >= 2)
        {
            loaded = true;
            foreach (ulong client in LobbyManager.Instance.Players.Keys.ToArray()) LobbyManager.Instance.SetPlayerReady(client, true);
            LobbyManager.Instance.StartGame();
        }
        var social = NetworkedSocialConversation.Instance;
        if (social != null && social.Enabled && social.LocalPlayerBound)
        { frameTimes.Enqueue(Time.unscaledDeltaTime); if (frameTimes.Count > 1800) frameTimes.Dequeue(); }
        if (running || social == null || !social.Enabled || !social.LocalPlayerBound) return;
        poll += Time.unscaledDeltaTime; if (poll < .25f) return; poll = 0;
        string inbox = NetworkedSocialConversation.Option("--social-inbox");
        if (string.IsNullOrEmpty(inbox) || !File.Exists(inbox)) return;
        string json;
        try { json = File.ReadAllText(inbox); } catch (IOException) { return; }
        if (lastCommand == json) return;
        lastCommand = json; RunCommand(social, json);
    }
    async void RunCommand(NetworkedSocialConversation social, string json)
    {
        running = true;
        try
        {
            var command = JsonUtility.FromJson<Command>(json);
            if (string.IsNullOrWhiteSpace(command.requestId)) throw new ArgumentException("Command needs a requestId");
            if (command.action == "talk")
            {
                var result = await social.SubmitLocalAsync(new ConversationRequest { requestId = command.requestId, npcId = command.npcId,
                    text = command.text, channel = command.channel, partyId = command.partyId });
                Write(JsonUtility.ToJson(result));
            }
            else if (!social.IsServer) throw new InvalidOperationException("World test commands require the host");
            else if (command.action == "start_party")
            { NetworkedPartyManager.Instance.StartPartyServer(); Write("{\"status\":\"party_started\"}"); }
            else if (command.action == "end_party")
            { NetworkedPartyManager.Instance.EndPartyServerRPC(); Write("{\"status\":\"party_ended\"}"); }
            else if (command.action == "dump") Write(JsonUtility.ToJson(social.World.Store.Read(), true));
            else if (command.action == "metrics")
            {
                var sorted = frameTimes.OrderBy(x => x).ToArray();
                Write(JsonUtility.ToJson(new Metrics { frameSamples = sorted.Length,
                    p95FrameSeconds = sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * .95) - 1],
                    managedAndNativeAllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                    hostWorkingSetBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64, hostPrivateBytes = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 }));
            }
            else if (command.action == "cancel") social.CancelQueuedLocal(command.text);
            else throw new ArgumentException("Unknown development command");
        }
        catch (Exception e) { Write(JsonUtility.ToJson(new ConversationResult { status = "driver_error", error = e.Message })); Debug.LogException(e); }
        finally { running = false; }
    }
    static void Write(string json)
    {
        string outbox = NetworkedSocialConversation.Option("--social-outbox");
        if (!string.IsNullOrEmpty(outbox))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outbox)));
            string temporary = outbox + ".tmp"; File.WriteAllText(temporary, json);
            if (File.Exists(outbox)) File.Replace(temporary, outbox, null); else File.Move(temporary, outbox);
        }
        Debug.Log("[SocialDriver] " + json);
    }
    [Serializable] sealed class Metrics
    {
        public int frameSamples;
        public float p95FrameSeconds;
        public long managedAndNativeAllocatedBytes, hostWorkingSetBytes, hostPrivateBytes;
    }
}
