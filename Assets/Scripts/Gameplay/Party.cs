using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents the state of a single party.
/// </summary>
[System.Serializable]
public class Party
{
    [Header("Party Information")]
    public int dayNumber;
    public int guestLimit;
    public List<NPCGuest> currentGuests = new List<NPCGuest>();
    
    // Each party stats
    public float popularity;
    public float troubleMaker;
    public float cashValue;
    
    [Header("Party State")]
    public PartyState state = PartyState.NotStarted;
    public float partyStartTime; // Real-time when party started
    public float partyEndTime;   // Real-time when party should end
    
    // In-game time tracking (10pm = 22:00, 2am = 02:00)
    public float inGameTimeStart = 22.0f; // 10:00 PM
    public float inGameTimeEnd = 2.0f;    // 2:00 AM (next day)
    
    public Party(int day, int limit)
    {
        dayNumber = day;
        guestLimit = limit;
        state = PartyState.NotStarted;
        currentGuests = new List<NPCGuest>();
    }
    
    /// <summary>
    /// Get the current in-game time based on elapsed real-time.
    /// Returns time in hours (22.0 = 10pm, 2.0 = 2am)
    /// </summary>
    public float GetCurrentInGameTime()
    {
        if (state != PartyState.Active)
            return inGameTimeStart;
            
        float elapsedRealTime = Time.time - partyStartTime;
        // 10 minutes real-time = 4 hours in-game (240 minutes)
        // So 1 second real-time = 0.4 minutes in-game = 0.00667 hours in-game
        float inGameHoursElapsed = (elapsedRealTime / 60f) * 4f; // Convert real minutes to in-game hours
        float currentTime = inGameTimeStart + inGameHoursElapsed;
        
        // Handle wrap-around past midnight (2am = 26:00 in 24h format, but we want 2.0)
        if (currentTime >= 24.0f)
            currentTime -= 24.0f;
            
        return currentTime;
    }
    
    /// <summary>
    /// Check if party time has ended
    /// </summary>
    public bool IsPartyOver()
    {
        if (state != PartyState.Active)
            return false;

        bool isOver = Time.time >= partyEndTime;
        if (isOver)
        {
            Debug.Log($"Party is over! Current Time: {Time.time:F2}, End Time: {partyEndTime:F2}, Duration: {partyEndTime - partyStartTime:F2} seconds");
        }
        return isOver;
    }

    /// <summary>
    /// Distribute the rewards after the party
    /// </summary>
    public void DistributePartyEndRewards()
    {
        if(state != PartyState.Ended)
        {
            Debug.LogWarning($"Distributing rewards when party state was: {state}");
            return;
        }

        // Rewards
        GameManager.Instance.AddTotalCashValue(cashValue);
        GameManager.Instance.AddTotalPopularityValue(popularity);

        Debug.Log($"Party rewards distributed: +{cashValue} Cash, +{popularity} Popularity");

        // TODO: Achievements hookups

        // TODO: Leaderboard implementation. Distribution of everything else
    }

    /// <summary>
    /// Add a guest to the party
    /// </summary>
    public bool AddGuest(NPCGuest guest)
    {
        if (currentGuests.Count >= guestLimit)
        {
            // TODO: Show a UI popup to the player that the party is at capacity and show UI for Guest Limit button
            Debug.LogWarning($"Party is at capacity ({guestLimit}). Cannot add more guests.");
            return false;
        }
        
        if (guest != null && !currentGuests.Contains(guest))
        {
            currentGuests.Add(guest);
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Remove a guest from the party
    /// </summary>
    public bool RemoveGuest(NPCGuest guest)
    {
        return currentGuests.Remove(guest);
    }
}

/// <summary>
/// States a party can be in
/// </summary>
public enum PartyState
{
    NotStarted,  // Party hasn't begun
    Active,       // Party is currently happening
    Ended        // Party has finished
}
