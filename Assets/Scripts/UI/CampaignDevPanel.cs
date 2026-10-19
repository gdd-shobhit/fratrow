using System;
using System.Collections.Generic;
using FratRow.Game;
using FratRow.Social;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Development-only IMGUI action console for the campaign loop (design/gdd/core-game-loop.md §3.4). Toggle with F1.
/// Sends every <see cref="ActionIds"/> action through <see cref="NetworkedCampaign.RequestAction"/> /
/// <see cref="NetworkedCampaign.RequestTalk"/> so testers can drive the backend without the final UI, and shows the last
/// <see cref="NetworkedCampaign.OnActionResult"/> and <see cref="NetworkedCampaign.OnConversation"/> reply.
/// Self-installs (DontDestroyOnLoad) in the editor and development builds only; inert in release builds.
/// Usage: enter Play Mode, press F1, pick an item, press "Buy". Hidden = no layout pass and no input capture.
/// </summary>
public sealed class CampaignDevPanel : MonoBehaviour
{
    static readonly string[] ItemIds = { "keg", "liquor", "soda_snacks", "speakers", "decorations", "security", "prank_kit", "stink_bomb", "cleanup_crew" };
    static readonly string[] ThemeIds = { "toga", "neon", "casino", "beach", "game_night" };
    static readonly string[] RoleIds = { "president", "social_chair", "rush_chair", "treasurer", "risk_manager", "brother" };
    static readonly string[] FallbackRivalIds = { "kappa", "theta", "lambda" };
    static readonly string[] DayShort = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    NetworkedCampaign _campaign;
    bool _visible, _showAll, _cursorTaken, _rivalsDirty = true;
    Vector2 _scroll, _rosterScroll;
    int _itemIndex, _qty = 1, _themeIndex, _roleIndex, _rivalIndex, _cover;
    string _npcId = "", _npcText = "";
    string _lastResult = "(none)", _lastConversation = "(none)";
    string[] _rivalIds = FallbackRivalIds;
    string[] _rosterIds, _rosterLabels;
    string _rosterError;
    GUIStyle _wrap, _header;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!(Application.isEditor || Debug.isDebugBuild)) return;
        if (FindAnyObjectByType<CampaignDevPanel>() != null) return;
        var go = new GameObject("[CampaignDevPanel]");
        DontDestroyOnLoad(go);
        go.AddComponent<CampaignDevPanel>();
    }

    void Awake() => useGUILayout = false;

    void Update()
    {
        if (!(Application.isEditor || Debug.isDebugBuild)) return;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) SetVisible(!_visible);
        PollCampaign();
    }

    void OnDisable()
    {
        if (_visible) SetVisible(false);
        Unbind();
    }

    // ---------------- Campaign binding ----------------

    void PollCampaign()
    {
        var current = NetworkedCampaign.Instance;
        if (current == null || !current.IsSpawned) current = null;
        if (ReferenceEquals(current, _campaign)) return;
        Unbind();
        if (current == null) return;
        _campaign = current;
        current.OnActionResult += HandleActionResult;
        current.OnConversation += HandleConversation;
        current.OnHousesChanged += HandleHousesChanged;
        _rivalsDirty = true;
    }

    void Unbind()
    {
        if (ReferenceEquals(_campaign, null)) return;
        _campaign.OnActionResult -= HandleActionResult;
        _campaign.OnConversation -= HandleConversation;
        _campaign.OnHousesChanged -= HandleHousesChanged;
        _campaign = null;
    }

    void HandleHousesChanged() => _rivalsDirty = true;

    void HandleActionResult(ActionResultNet r) =>
        _lastResult = (r.Ok ? "OK " : "FAIL ") + r.Action.ToString() + " [" + r.Code.ToString() + "] " + r.Message.ToString();

    void HandleConversation(string npcId, ConversationResult r)
    {
        if (r == null) { _lastConversation = npcId + ": (no result)"; return; }
        _lastConversation = npcId + " [" + r.status + "]: " + r.reply + "\ndecision: " + r.decision + " · commitment: " + r.commitment +
                            (string.IsNullOrEmpty(r.error) ? "" : " · error: " + r.error);
    }

    // ---------------- Visibility / cursor ----------------

    void SetVisible(bool visible)
    {
        _visible = visible;
        useGUILayout = visible;
        var inputs = FindLocalInputs();
        if (visible)
        {
            if (inputs != null) inputs.SetInventoryUIMode(true);
            else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            _cursorTaken = true;
            return;
        }
        if (!_cursorTaken) return;
        _cursorTaken = false;
        var phone = FindAnyObjectByType<Systems.Phone.PhoneView>();
        if (phone != null && phone.IsOpen) return; // the phone still needs the cursor
        if (inputs != null) inputs.SetInventoryUIMode(false);
    }

    static StarterAssetsInputs FindLocalInputs()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.GetComponent<StarterAssetsInputs>();
    }

    // ---------------- IMGUI ----------------

    void OnGUI()
    {
        if (!(Application.isEditor || Debug.isDebugBuild)) return;
        if (!_visible) return;
        if (_wrap == null)
        {
            _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        }
        float scale = Mathf.Max(1f, Screen.height / 1080f);
        var previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        var rect = new Rect(10f, 10f, 500f, Screen.height / scale - 20f);
        GUI.Box(rect, GUIContent.none);
        GUI.Box(rect, GUIContent.none);
        GUILayout.BeginArea(rect);
        _scroll = GUILayout.BeginScrollView(_scroll);
        DrawContents();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.matrix = previous;
    }

    void DrawContents()
    {
        GUILayout.Label("CAMPAIGN DEV PANEL  (F1 to close)", _header);
        var c = _campaign;
        if (c == null) { GUILayout.Label("NetworkedCampaign not spawned. Host or join a session first.", _wrap); return; }
        if (!c.IsCampaignActive) GUILayout.Label("Campaign not active yet (host is still starting the loop).", _wrap);
        if (_rivalsDirty) RefreshRivals(c);

        var h = c.Hud;
        var phase = (Phase)h.Phase;
        int me = LocalBrotherIndex(c);
        var nm = NetworkManager.Singleton;
        float left = h.PhaseRemaining - (nm != null ? (float)(nm.ServerTime.Time - h.AnchorServerTime) : 0f);
        int minute = (int)(c.CurrentClockHour * 60f);
        GUILayout.Label("Phase: " + phase + " · Week " + (h.Week + 1) + "/" + h.SemesterWeeks + " · " + DayShort[Mathf.Clamp(h.Day, 0, 6)] + " " +
                        (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00") + " · " + Mathf.CeilToInt(Mathf.Max(0f, left)) + "s left", _wrap);
        bool localReady = false;
        if (me >= 0)
        {
            var b = c.Brothers[me];
            localReady = b.Ready;
            string location = b.Location.ToString();
            GUILayout.Label("You: " + b.Name.ToString() + " · role " + b.Role.ToString() + " · location " + (location.Length > 0 ? location : "(none)") +
                            " · ready " + b.Ready + (b.DetainedRemaining > 0f ? " · DETAINED " + Mathf.CeilToInt(b.DetainedRemaining) + "s" : "") +
                            " · drunk " + Mathf.RoundToInt(b.Drunk * 100f) + "% (tier " + b.DrunkTier + ")", _wrap);
        }
        else GUILayout.Label("You: not bound to a brother yet (location unknown).", _wrap);
        for (int i = 0; i < c.Houses.Count; i++)
        {
            var house = c.Houses[i];
            if (!house.IsPlayer) continue;
            GUILayout.Label("House: $" + house.Cash + " · pop " + house.Popularity.ToString("0.0") + " · heat " + house.Heat.ToString("0") + " · dean " +
                            house.Dean.ToString("0") + " · cup " + house.CupPoints + " · rank " + house.Rank + " · police alert " + h.PoliceAlert, _wrap);
        }

        GUILayout.Label("Drinks (at the house, any phase; G / right shoulder in game):");
        GUILayout.BeginHorizontal();
        Button(c, ActionIds.Drink, "Drink");
        if (GUILayout.Button("Keg")) Send(c, ActionIds.Drink, item: "keg");
        if (GUILayout.Button("Liquor")) Send(c, ActionIds.Drink, item: "liquor");
        if (GUILayout.Button("Soda")) Send(c, ActionIds.Drink, item: "soda_snacks");
        GUILayout.EndHorizontal();

        _showAll = GUILayout.Toggle(_showAll, "Show every phase's actions (to test wrong_phase)");
        if (phase == Phase.Weekday || _showAll) DrawWeekday(c, localReady);
        if (phase == Phase.Party || _showAll) DrawParty(c, h);
        if (phase == Phase.Debrief || phase == Phase.SemesterEnd || _showAll) DrawDebrief(c, phase);
        DrawNpc(c);
        DrawResults(c);
    }

    void DrawWeekday(NetworkedCampaign c, bool localReady)
    {
        GUILayout.Label("WEEKDAY", _header);
        GUILayout.Label("Buy (at Shop):");
        _itemIndex = GUILayout.SelectionGrid(_itemIndex, ItemIds, 3);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-", GUILayout.Width(28))) _qty = Mathf.Max(1, _qty - 1);
        GUILayout.Label("qty " + _qty, GUILayout.Width(50));
        if (GUILayout.Button("+", GUILayout.Width(28))) _qty = Mathf.Min(99, _qty + 1);
        if (GUILayout.Button("Buy " + ItemIds[_itemIndex])) Send(c, ActionIds.Buy, item: ItemIds[_itemIndex], amount: _qty);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        Button(c, ActionIds.WorkShift, "Work shift (Shop)");
        Button(c, ActionIds.Study, "Study (Library)");
        Button(c, ActionIds.Workout, "Workout (Gym)");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Button(c, ActionIds.Volunteer, "Volunteer (Quad)");
        Button(c, ActionIds.MeetDean, "Meet Dean (Quad)");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Button(c, ActionIds.CleanHouse, "Clean house");
        Button(c, ActionIds.GuardHouse, "Guard house (17h+)");
        GUILayout.EndHorizontal();

        GUILayout.Label("Theme (Social Chair / President, at the house):");
        _themeIndex = GUILayout.SelectionGrid(_themeIndex, ThemeIds, 5);
        if (GUILayout.Button("Set theme " + ThemeIds[_themeIndex])) Send(c, ActionIds.SetTheme, item: ThemeIds[_themeIndex]);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Cover $" + _cover, GUILayout.Width(70));
        if (GUILayout.Button("-5")) _cover -= 5;
        if (GUILayout.Button("-1")) _cover -= 1;
        if (GUILayout.Button("+1")) _cover += 1;
        if (GUILayout.Button("+5")) _cover += 5;
        _cover = Mathf.Clamp(_cover, -5, 99); // out-of-range values allowed on purpose to test validation
        if (GUILayout.Button("Set cover")) Send(c, ActionIds.SetCover, amount: _cover);
        GUILayout.EndHorizontal();

        DrawRoles(c);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(localReady ? "Unready" : "Ready")) Send(c, ActionIds.Ready, amount: localReady ? 0 : 1);
        Button(c, ActionIds.StartWeekend, "Start weekend (President)");
        GUILayout.EndHorizontal();

        GUILayout.Label("Sabotage (at the rival's house unless noted):");
        DrawRivalPicker();
        string rival = _rivalIds[_rivalIndex];
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Prank " + rival)) Send(c, ActionIds.Prank, target: rival);
        if (GUILayout.Button("Steal mascot " + rival)) Send(c, ActionIds.StealMascot, target: rival);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Spread rumor about " + rival + " to npc '" + _npcId.Trim() + "'"))
            Send(c, ActionIds.SpreadRumor, target: _npcId.Trim(), rival: rival);
    }

    void DrawParty(NetworkedCampaign c, in CampaignHudState h)
    {
        GUILayout.Label("PARTY NIGHT", _header);
        GUILayout.Label("Guests " + h.Guests + "/" + h.Capacity + " · fun " + h.Fun.ToString("0") + " · hype " + h.Hype.ToString("0") + " · noise " +
                        h.Noise.ToString("0") + " · trouble " + h.Trouble.ToString("0") + " · attention " + h.Attention.ToString("0") +
                        " · card check " + (h.CardCheck ? "ON" : "OFF") + " · underage " + h.UnderageDrinkers, _wrap);
        GUILayout.BeginHorizontal();
        Button(c, ActionIds.ServeDrinks, "Serve drinks");
        Button(c, ActionIds.Dj, "DJ");
        Button(c, ActionIds.TurnDownMusic, "Turn down music");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Card check ON")) Send(c, ActionIds.CardCheck, amount: 1);
        if (GUILayout.Button("Card check OFF")) Send(c, ActionIds.CardCheck, amount: 0);
        Button(c, ActionIds.KickOut, "Kick out");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Button(c, ActionIds.BeerRun, "Beer run");
        Button(c, ActionIds.TalkToPolice, "Talk to police");
        Button(c, ActionIds.GuardHouse, "Guard house");
        GUILayout.EndHorizontal();

        var incidents = c.Incidents;
        GUILayout.Label("Open incidents: " + incidents.Count);
        for (int i = 0; i < incidents.Count; i++)
        {
            var inc = incidents[i];
            string kind = inc.Kind.ToString();
            string action = IncidentAction(kind);
            GUILayout.BeginHorizontal();
            GUILayout.Label("#" + inc.Id + " " + kind + " (guest " + inc.GuestId + ")", GUILayout.Width(250));
            if (action == null) GUILayout.Label("(no direct fix)");
            else if (GUILayout.Button(action)) Send(c, action, target: inc.Id.ToString());
            GUILayout.EndHorizontal();
        }

        GUILayout.Label("Rival sabotage:");
        DrawRivalPicker();
        string rival = _rivalIds[_rivalIndex];
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Call cops on " + rival)) Send(c, ActionIds.CallCops, target: rival);
        if (GUILayout.Button("Stink bomb " + rival)) Send(c, ActionIds.StinkBomb, target: rival);
        if (GUILayout.Button("Steal keg " + rival)) Send(c, ActionIds.StealKeg, target: rival);
        GUILayout.EndHorizontal();
    }

    void DrawDebrief(NetworkedCampaign c, Phase phase)
    {
        GUILayout.Label("DEBRIEF / SEMESTER END", _header);
        GUILayout.BeginHorizontal();
        Button(c, ActionIds.ContinueDebrief, "Continue (President)");
        Button(c, ActionIds.StartSemester, "Start next semester (President)");
        GUILayout.EndHorizontal();
        if (phase == Phase.Debrief && !_showAll)
        {
            Button(c, ActionIds.CleanHouse, "Clean house");
            DrawRoles(c);
        }
        if (!c.IsServer) return;
        GUILayout.Label("Host shortcuts (skip role checks):");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Host: advance debrief/semester")) { c.AdvanceServer(); _lastResult = "Host advanced the phase."; }
        if (GUILayout.Button("Host: start weekend now")) _lastResult = "Host start weekend: " + (c.StartWeekendServer() ? "ok" : "refused");
        GUILayout.EndHorizontal();
    }

    void DrawRoles(NetworkedCampaign c)
    {
        GUILayout.Label("Claim role (weekday / debrief):");
        _roleIndex = GUILayout.SelectionGrid(_roleIndex, RoleIds, 3);
        if (GUILayout.Button("Claim " + RoleIds[_roleIndex])) Send(c, ActionIds.ClaimRole, target: RoleIds[_roleIndex]);
    }

    void DrawNpc(NetworkedCampaign c)
    {
        GUILayout.Label("NPC", _header);
        GUILayout.BeginHorizontal();
        GUILayout.Label("npcId", GUILayout.Width(45));
        _npcId = GUILayout.TextField(_npcId, 64);
        GUILayout.EndHorizontal();
        _npcText = GUILayout.TextArea(_npcText, SocialRules.InputCharacters, GUILayout.MinHeight(44));
        string npc = _npcId.Trim();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Talk"))
        {
            if (npc.Length == 0 || string.IsNullOrWhiteSpace(_npcText)) _lastResult = "Talk needs an npcId and some text.";
            else { _lastResult = "... sent talk"; c.RequestTalk(npc, _npcText); }
        }
        if (GUILayout.Button("Invite")) Send(c, ActionIds.Invite, target: npc);
        if (GUILayout.Button("Rush")) Send(c, ActionIds.RushTalk, target: npc);
        if (GUILayout.Button("Ask number")) Send(c, ActionIds.AskNumber, target: npc);
        if (GUILayout.Button("Extend bid")) Send(c, ActionIds.ExtendBid, target: npc);
        GUILayout.EndHorizontal();

        if (_rosterIds == null)
        {
            if (GUILayout.Button("Load NPC ids from CampusRoster")) LoadRoster();
            if (_rosterError != null) GUILayout.Label("Roster: " + _rosterError, _wrap);
            return;
        }
        GUILayout.Label("Pick an NPC (* = rushing PNM):");
        _rosterScroll = GUILayout.BeginScrollView(_rosterScroll, GUILayout.Height(110));
        int pick = GUILayout.SelectionGrid(-1, _rosterLabels, 4);
        if (pick >= 0) _npcId = _rosterIds[pick];
        GUILayout.EndScrollView();
    }

    void DrawResults(NetworkedCampaign c)
    {
        GUILayout.Label("LAST RESULTS", _header);
        GUILayout.Label("Action: " + _lastResult, _wrap);
        GUILayout.Label("Conversation: " + _lastConversation, _wrap);
        var news = c.RecentNews;
        for (int i = news.Count - 1; i >= 0 && i >= news.Count - 4; i--) GUILayout.Label("news: " + news[i].Text.ToString(), _wrap);
    }

    // ---------------- Helpers ----------------

    void Button(NetworkedCampaign c, string action, string label)
    {
        if (GUILayout.Button(label)) Send(c, action);
    }

    void Send(NetworkedCampaign c, string action, string target = "", string item = "", int amount = 0, string rival = "")
    {
        _lastResult = "... sent " + action + " (waiting for host)";
        c.RequestAction(action, target, item, amount, rival);
    }

    void DrawRivalPicker()
    {
        _rivalIndex = Mathf.Clamp(_rivalIndex, 0, _rivalIds.Length - 1);
        _rivalIndex = GUILayout.SelectionGrid(_rivalIndex, _rivalIds, _rivalIds.Length);
    }

    void RefreshRivals(NetworkedCampaign c)
    {
        _rivalsDirty = false;
        var ids = new List<string>();
        for (int i = 0; i < c.Houses.Count; i++)
            if (!c.Houses[i].IsPlayer) ids.Add(c.Houses[i].Id.ToString());
        _rivalIds = ids.Count > 0 ? ids.ToArray() : FallbackRivalIds;
    }

    void LoadRoster()
    {
        try
        {
            var roster = NetworkedCampaign.LoadRoster();
            var ids = new List<string>();
            var labels = new List<string>();
            foreach (var npc in roster.npcs)
            {
                if (npc == null || string.IsNullOrEmpty(npc.id)) continue;
                ids.Add(npc.id);
                labels.Add(npc.isPnm ? npc.id + "*" : npc.id);
            }
            _rosterIds = ids.ToArray();
            _rosterLabels = labels.ToArray();
            _rosterError = null;
        }
        catch (Exception e)
        {
            _rosterError = e.Message;
        }
    }

    static int LocalBrotherIndex(NetworkedCampaign c)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return -1;
        ulong local = nm.LocalClientId;
        int best = -1;
        for (int i = 0; i < c.Brothers.Count; i++)
        {
            var b = c.Brothers[i];
            if (b.ClientId != local) continue;
            if (b.Connected) return i; // unbound rows also replicate ClientId 0; prefer the connected one
            if (best < 0) best = i;
        }
        return best;
    }

    static string IncidentAction(string kind)
    {
        switch (kind)
        {
            case "fight": return ActionIds.BreakUpFight;
            case "passed_out": return ActionIds.HelpGuest;
            case "property_damage": return ActionIds.FixDamage;
            case "noise_complaint": return ActionIds.TurnDownMusic;
            case "keg_dry": return ActionIds.BeerRun;
            default: return null;
        }
    }
}
