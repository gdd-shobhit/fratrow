# Drunk Mechanic — Drinking, Slurred Speech, Drunk Reactions

> **Status**: Draft v1 (2026-10-18). Implemented in `Assets/Scripts/Game/Core` (`GameSession.Drinking.cs`, `DrunkSpeech.cs`)
> and the NGO adapters. Not yet played in Unity.
> **Tuning**: every number below is a key under `"drunk"` in `Assets/Resources/Campus/GameLoopConfig.json`.
> **Calibrate**: `dotnet run --project Tests/GameLoopHarness -- --drunk-speech "your line" [--samples 5] [--cosmetic]`
> prints a line at every drunk level with the live JSON tuning.
> **Related**: `design/gdd/core-game-loop.md` (stash, party tick, guest `drunk`), `Docs/SocialBrains.md` (NPC backend),
> `Docs/architecture/adr-0002-host-procedural-drunk-speech.md` (why there is no second language model).

## 1. Overview

Brothers can drink from the house stash, the same kegs and liquor the party guests drink. Each serving raises the
brother's **drunk** level, and the level sobers off in real time. Past a **threshold** the brother is *drunk*: everything
he says to an NPC is rewritten before anyone hears it. Free-typed lines get slurred, typo'd, stretched and interrupted,
and they ramble into drunk tangents. The more drunk he is, the more the line falls apart. NPCs are told by the host that
the speaker is drunk. They remark on it, and their persona decides whether they find it charming or off-putting. The
whole house sees the drunk level on the HUD, and the house news feed announces it when someone gets drunk or wasted.

## 2. Player Fantasy

- *"I typed something smooth. It came out as 'shaturday... wait what was i saying'."* The comedy is the gap between
  what you meant and what your character managed to say. You watch your line get mangled on screen before the NPC
  answers.
- *"Liquid courage, real consequences."* Drinking with the guests lifts party hype. But a wasted brother botches the
  cop talk, gets caught pranking, and turns off the reserved kids he is trying to rush.
- *"Everyone knows."* "Sam is absolutely wasted." goes out to the whole house. Your brothers see it, and so does the
  NPC you are slurring at.

## 3. Detailed Rules

### 3.1 Drinking (`drink` action)

- **Where and when:** at the players' house (`playerHomeLocation`), in any phase except SemesterEnd. Drinking on a
  weekday is a pre-game. A container opened before the party stays open into party night (`BeginParty` carries over
  `openServings`).
- **Source:** one serving via the party's `TakeServing`, so brothers and guests share open containers and the stash.
  `item` may name a specific drink (`keg`, `liquor`, `soda_snacks`). With no item, alcohol is preferred.
- **Effect:** `drunk += item.strength`, capped at `max`. Non-alcoholic drinks add nothing and are allowed at the cap.
- **Pacing:** a drink is refused until `nextDrinkAt` (`drinkCooldownSeconds`). An alcoholic drink is refused at `max`.
- **During an active party:** each alcoholic drink adds `partyHype` to party hype.
- **News:** crossing *into* drunk or wasted posts a house news line, which goes to every brother.
- **Failure codes:** `wrong_location`, `wrong_phase`, `missing_item` (dry stash), `invalid_target` (not a drink),
  `cap_reached` (pacing or cap).
- **Input:** `Drink` input action (keyboard **G**, gamepad **right shoulder**), and the dev panel (F1) Drink buttons.

### 3.2 Conditions

| Condition | Range | Visible effect |
|---|---|---|
| sober | `drunk < tipsyAt` | none |
| tipsy | `tipsyAt ≤ drunk < threshold` | HUD only; speech untouched |
| drunk | `threshold ≤ drunk < wastedAt` | speech rewritten; NPCs notice; news line |
| wasted | `drunk ≥ wastedAt` | heavy rewriting; NPCs react strongly; news line |

Every brother sobers by `soberPerSecond` each real second, online or offline. The level is saved with the campaign.

### 3.3 Speech rewriting

All three conversation actions run through `DrunkSpeech.Rewrite` on the **host**: typed `talk`, button `invite` and
`rush_talk`, in person or by text ("drunk texting"). The rewrite happens before the line reaches the social service.
The NPC hears and remembers the rewritten line, and the speaker is shown it.

- **Free text (`talk`), full mode.** The steps run in this order:
  1. *Trail-off.* Lines of 8+ words may be cut to 50–80% of their length, ending with a trail-off phrase.
  2. *Per word.* Lowercased once intensity ≥ `lowercaseAt`. Then slur (s→sh, closing -ing→-in'), swap two inner
     letters, and per letter: typo (neighbouring QWERTY key), drop, double. Then stretch the last letter, SHOUT, and
     repeat the word.
  3. *Between words.* Interjections ("*hic*", "bro", "uhh").
  4. *End.* A drunk tangent ("i love you man", "where's my phone").
- **Button lines (`invite`, `rush_talk`), cosmetic mode.** Only stretch-last-letter, shout, repeat, interjections,
  lowercase and tangents run, at `cosmeticScale` × intensity. No letter is altered, so keyword intent ("party",
  "rush", "brother") still reads, and a drunk invite is still an invite.
- **Limits.** The output never exceeds the 800-character social input limit, never splits an emoji surrogate pair, and
  is never empty.
- **Sober speech** is returned untouched and draws no randomness, so existing seeds and replays are unchanged.

### 3.4 NPC reaction

- The host attaches the condition to the conversation request (`speakerCondition`: tipsy / drunk / wasted). Clients
  can never set it. The legacy dev submit path clears it.
- **Model provider:** every prompt projection gets a host-observed `SPEAKER_CONDITION` line. It tells the NPC to react
  in character, and not to decode typos as hidden meaning.
- **Grounded reply composer:** a drunk or wasted speaker gets an aside after the opening ("Someone's had a few." /
  "Wow, you are wasted."). The aside is dropped if the reply would exceed its 300-character budget.
- **Heuristic provider:** a persona-dependent bias on the NPC's warmth score (see §4).

### 3.5 Other gameplay effects

- `talk_to_police` success drops by `Drunk01 × policeTalkPenalty`.
- Crime detection rises by `Drunk01 × crimeDetect`.

### 3.6 Multiplayer

- **The host is authoritative for everything.** It owns the drunk level, validation, sobering, the rewrite and the
  randomness. The rewrite uses the campaign seed stream (`GameRng`, purpose `drunk-speech`).
- **Clients never re-run it.** They receive the result, so no cross-client determinism is needed, and a modified client
  cannot skip the slurring.
- **Replication:** `BrotherNetState.Drunk` (0..1 of `max`) and `DrunkTier` (0–3) travel in the existing reliable
  brothers `NetworkList`. It is polled at 4 Hz and re-sent only when the value moves ≥ 1% (about every 10 s per drunk
  brother) or the tier changes. Every brother's level is visible to every client.
- **Speaker echo:** `SpeechClientRpc` is reliable and targeted at the speaker only, once per accepted talk turn. It
  sends the line that actually came out (≤ 800 chars) before the NPC reply, which arrives 0–30 s later. The talk panel
  replaces the typed line with it and tags "(You're slurring.)" / "(You're wasted.)".
- **Schema:** `CampaignHudState.CurrentVersion` is 2 for the new brother fields.

## 4. Formulas

- `Drunk01 = clamp(drunk / max, 0, 1)`
- Speech intensity `k`:
  - `k = 0` when `drunk < threshold`
  - otherwise `k = startIntensity + (1 − startIntensity) × (min(1, (drunk − threshold) / (max − threshold)))^curve`
- Each speech effect fires with probability `rate × k` (cosmetic mode uses `rate × k × cosmeticScale`).
  - Per letter: `typo`, `dropLetter`, `doubleLetter`.
  - Per word: `slur`, `swapLetters`, `stretch`, `shout`, `repeatWord`.
  - Per word gap: `interjection`.
  - Per message: `tangent`, `trailOff`.
- Sobering: `drunk ← max(0, drunk − soberPerSecond × dt)`.
- Heuristic NPC warmth bias:

  | Condition | bubbly / playful / laid-back | reserved / dry | other tones |
  |---|---|---|---|
  | drunk | +0.03 | −0.08 | −0.03 |
  | wasted | −0.04 | −0.18 | −0.10 |

  For scale, an insult is −0.5 and a compliment is +0.05 on the same score.
- Default pacing (keg 0.10 and liquor 0.25 per serving; sobering 0.0015/s):
  - *drunk:* 4 keg cups or 2 shots.
  - *wasted:* 10 cups or 4–5 shots.
  - *sober again from wasted:* about 11 minutes.

## 5. Edge Cases

- **Drinking at the cap:** refused for alcohol; soda is still allowed.
- **Dry stash:** `missing_item`. During a party, guests drinking can empty the keg a brother wanted.
- **Disconnecting while wasted:** the level persists and keeps sobering. On reconnect it replicates as usual.
- **Exactly at the threshold:** intensity is `startIntensity` (10%), so becoming drunk is noticeable at once.
- **An 800-character line at max intensity:** clamped at a word boundary to ≤ 800 chars.
- **Emoji and non-Latin text:** only letters are altered, and surrogate pairs are never split. QWERTY typos apply to
  ASCII letters only.
- **A garbled free-text line loses its keywords** ("shaturday" no longer matches "saturday"). With the heuristic
  provider, a drunk typed invite can be misread as small talk. That is intended: use the Invite button, which stays
  keyword-safe.
- **A typo can create an insult** (for example "duck" → "suck"). That is emergent and intended.
- **The speech echo arrives after the NPC reply:** cannot happen on the reliable ordered channel. If it did, the panel
  ignores an echo once the turn has finished.
- **Older saves and journals:** load as sober. `speakerCondition` defaults to empty.

## 6. Dependencies

- **Core game loop:** stash, `TakeServing`, party hype, news, police talk, crime detection
  (`design/gdd/core-game-loop.md` §3.4–3.6, §3.11).
- **Social backend:** `ConversationRequest.speakerCondition`, `ModelContext.SpeakerCondition`, `SocialMemory` prompt
  lines, `GroundedSocialTurn.Compose` aside, `HeuristicSocialModelProvider` bias.
- **Unity adapters:**
  - `NetworkedCampaign`: replication and `SpeechClientRpc`.
  - `NpcTalkPanel`: spoken line display.
  - `GameplayUIManager.SetDrunkLevel`: HUD.
  - `FR_FirstPersonCharacter.DrinkCheck`: input to `RequestAction(drink)`.
  - `CampaignDevPanel`: drink buttons and the drunk readout.

## 7. Tuning Knobs (`GameLoopConfig.json` → `drunk`)

| Key | Default | Meaning |
|---|---|---|
| `tipsyAt` / `threshold` / `wastedAt` / `max` | 0.15 / 0.4 / 1.0 / 1.5 | condition bands and cap |
| `soberPerSecond` | 0.0015 | real-time sobering |
| `drinkCooldownSeconds` | 5 | pacing between drinks |
| `partyHype` | 2 | hype per alcoholic drink during the party |
| `policeTalkPenalty` / `crimeDetect` | 0.3 / 0.1 | penalties at `max` |
| `speech.startIntensity` / `speech.curve` | 0.1 / 1 | intensity ramp |
| `speech.typo` / `dropLetter` / `doubleLetter` | 0.05 / 0.03 / 0.04 | per letter at k = 1 |
| `speech.swapLetters` / `slur` / `stretch` / `repeatWord` / `shout` | 0.08 / 0.45 / 0.15 / 0.06 / 0.05 | per word at k = 1 |
| `speech.interjection` | 0.08 | per word gap at k = 1 |
| `speech.lowercaseAt` | 0.25 | intensity at which the line goes lowercase |
| `speech.tangent` / `trailOff` | 0.35 / 0.15 | per message at k = 1 |
| `speech.cosmeticScale` | 0.7 | button-line share of intensity |
| `speech.interjections` / `tangents` / `trailOffs` | lists | the words themselves; an empty list restores the defaults |

`Validate()` rejects bands out of order, negative rates or rates above 1, and a non-positive curve.

## 8. Acceptance Criteria

Automated in `Tests/GameLoopHarness/DrunkTests.cs` and `Tests/SocialHarness/Program.cs`:

1. Below the threshold, speech is untouched and draws no randomness. At the threshold, intensity is at least `startIntensity`.
2. The rewrite is deterministic per seed. Average edit damage strictly increases from drunk to wasted to cap, and is above 25% at the cap.
3. An 800-character or emoji input stays a valid social input: ≤ 800 chars, no split surrogates, not empty.
4. Wasted invite and rush button lines still match the invite and rush cues in 300 of 300 seeds, and the heuristic NPC answers a wasted invite with an RSVP.
5. Drinking at the house uses the shared stash. Five shots reach wasted, and the house is told exactly twice (drunk, wasted).
6. Wrong place, dry stash, a non-drink, the cooldown and the cap are each refused with their codes. Soda is allowed at the cap.
7. A brother sobers at `soberPerSecond` and never goes below 0. The drunk state survives a save round trip.
8. A wasted brother's typed line is journaled rewritten. Every prompt projection carries the condition, and the reply remarks on it within 300 chars for every tone, mood and decision.
9. An invented condition is rejected, and a pre-feature journal request replays as sober.
10. A keg opened on a weekday is still open on party night.
11. **Manual (Unity, host and one client):**
    - Press G at the house: the HUD % rises on both machines and the news line appears for both.
    - Type to an NPC while wasted: your line is replaced by the slurred one, then the NPC remarks on it.
    - The other client's HUD shows its own level, unaffected.
    - Screenshots go in `production/qa/evidence/`.
