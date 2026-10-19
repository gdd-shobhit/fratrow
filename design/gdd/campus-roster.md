# Campus Roster — Named NPC Reference

> **Status**: Draft v1 (2026-10-18). Data reference, not a mechanic GDD.
> **Data**: `Assets/Resources/Campus/CampusRoster.json` (schema in `Assets/Scripts/Game/Core/CampusRoster.cs`).
> **Related**: `design/gdd/core-game-loop.md` §3.6 (witnesses), §3.10 (rush), §4.7–4.8 (interest, destination choice);
> `design/gdd/campus-gossip.md` (schedules, cliques and the `gossip` trait drive who tells whom).

The roster has 32 named NPCs. It covers the 16 original campus NPCs (seed stats, gates and schedules unchanged), the 10 authored
catalog girls (persona and voice verbatim from `SocialCharacterCatalog`), and 6 new rush PNMs. Of these, 10 are PNMs and 11 are underage.
Twelve start unlocked. Archetype is `guestClass · rarity`.

| id | gender | archetype | pop | status concern | PNM | underage | tone | summary |
|---|---|---|---|---|---|---|---|---|
| Brad | boy | Jock · Common | 15 | Low | – | – | casual | Big-hearted gym regular |
| Tyler | boy | Jock · Common | 12 | Moderate | – | – | playful | Cocky trash-talker |
| Megan | girl | MeanGirl · Rare | 20 | High | – | – | playful | Drama-loving queen bee |
| Ashley | girl | MeanGirl · Common | 10 | Moderate | – | – | assertive | Confident party regular |
| Kevin | boy | Nerd · Common | 8 | Low | yes | yes | bubbly | Earnest, chatty first-year |
| Priya | girl | Nerd · Common | 10 | Low | – | yes | dry | Driven, no-nonsense student |
| Sam | boy | None · Common | 5 | Low | – | yes | laid-back | Low-key, easily overlooked first-year |
| Jordan | boy | None · Rare | 18 | High | yes | – | dry | Cool, selective trend reader |
| Alex | boy | Jock · Common | 14 | Moderate | – | – | reserved | Disciplined, steady athlete |
| Riley | girl | MeanGirl · Common | 12 | Moderate | – | – | dry | Sarcastic contrarian |
| Casey | boy | None · Legendary | 35 | Moderate | yes | – | playful | Magnetic campus wildcard |
| Morgan | girl | Nerd · Rare | 16 | Moderate | – | – | assertive | Opinionated debater |
| Jamie | girl | Jock · Common | 11 | Low | – | – | casual | Rowdy, team-first athlete |
| Avery | girl | MeanGirl · Rare | 22 | Moderate | – | – | playful | Thrill-seeking party fixture |
| Quinn | boy | None · Common | 9 | Moderate | yes | yes | casual | Eager, indecisive first-year |
| Drew | boy | Jock · Rare | 17 | High | – | – | assertive | Image-conscious athlete |
| Maya | girl | None · Rare | 32 | Moderate | – | – | bubbly | Bubbly social connector |
| Chloe | girl | MeanGirl · Rare | 35 | High | – | – | dry | Image-conscious tastemaker |
| Zoe | girl | None · Common | 22 | Low | – | – | playful | Playful music obsessive |
| Nina | girl | Nerd · Common | 12 | Low | – | yes | reserved | Quiet, observant wit |
| Sofia | girl | None · Common | 25 | Moderate | – | – | bubbly | Warm friend-group anchor |
| Tessa | girl | Jock · Common | 28 | Moderate | – | – | assertive | Competitive straight shooter |
| Brooke | girl | None · Common | 18 | Low | – | – | laid-back | Easygoing hangout friend |
| Ivy | girl | MeanGirl · Rare | 30 | High | – | – | dry | Observant social strategist |
| Leah | girl | Nerd · Common | 20 | Moderate | – | yes | casual | Organized, curious planner |
| Jade | girl | None · Common | 26 | Low | – | – | assertive | Independent status skeptic |
| Marcus | boy | Jock · Common | 22 | Moderate | yes | yes | assertive | Confident first-year athlete |
| Mateo | boy | Nerd · Common | 6 | Low | yes | yes | reserved | Careful first-gen student |
| Owen | boy | None · Common | 15 | Moderate | yes | yes | playful | First-year class clown |
| Kai | boy | None · Common | 12 | Low | yes | yes | laid-back | Laid-back first-year skater |
| Elliot | boy | None · Rare | 18 | High | yes | – | casual | Ambitious pre-law networker |
| Carter | boy | None · Rare | 24 | High | yes | yes | casual | Kappa legacy under pressure |

## Design intent

**Popularity and status concern.** NPC popularity runs from 5 to 35, with a median of about 18. Houses run from 5 (the players) to 60
(the top rival). At the start, every NPC except Sam is more popular than the players' house, and the least popular rival (25) still
outranks most of campus. The status-concern mix is 11 Low, 14 Moderate and 7 High. With `popularityWeight` of 0.4, 0.8 or 1.2 (§4.8),
the Low-concern NPCs (Brad, Kevin, Priya, Sam, Jamie, Zoe, Nina, Brooke, Jade, Mateo, Kai) are the market the players can win in week 1.
The Moderate group swings with conversation and party quality. The High group (Megan, Jordan, Drew, Chloe, Ivy, Elliot, Carter) leans
toward the rivals until the house climbs. Unlock gates form a matching ladder: Megan 25, Jordan 30, Drew 28, Avery 35, Chloe 40,
Casey 60/B. Higher-popularity NPCs are reached later and pay off as notable guests.

**Rival-house regulars are the witnesses.** There are 31 weekday slots at a rival house, held by 24 NPCs: Kappa 11 slots (8 NPCs),
Theta 11 (11) and Lambda 9 (8). An NPC's slots line up with their starting `rivalAffinity`. Chloe, Drew, Jordan, Carter and Megan hang
out at Kappa; Tyler, Brad, Riley, Avery and Marcus at Theta; Nina, Morgan, Priya, Kevin and Mateo at Lambda. Sabotaging a house is
therefore usually seen by people who like it, so the grievance lands where it hurts most (§3.6). Every rival-house slot falls between
13:00 and 20:00. Mornings are unwatched, which trades fewer witnesses for daytime patrols. The only `steal_mascot` window (17–20) with no
witness is Lambda on Monday. Nina is at Lambda on Tuesday and Thursday afternoons, which supports the "Nina saw you trash the Lambda
house" beat in the core loop GDD.

**PNM roster.** There are 10 PNMs, all boys: Kevin, Jordan, Casey and Quinn from the original seeds, plus 6 new ones. Recruitable
originals that are girls (Priya, Morgan) remain party guests only. Starting leans are spread so that every rival has a natural pick:
Carter +30 and Jordan +15 toward Kappa, Owen +20 and Marcus +15 toward Theta, Mateo +20 and Kevin +15 toward Lambda. Elliot is split
between Kappa and Lambda, and Quinn and Kai are open. Each persona states what would make him pledge, so conversation (`fraternityRegard`)
is the lever that turns a pick. GPA runs from 2.4 to 3.8. Recruiting therefore trades popularity and party energy (Casey, Owen) against
the Dean's GPA check (Mateo, Kevin, Elliot). Four PNMs start unlocked: Kevin, Marcus, Mateo and Owen. Casey is the Legendary late-game
prize (60/B). Seven of the 10 PNMs are underage, so courting them at a party carries real card-check risk.

**Who gossips (`gossip`, 0–1).** Each authored NPC has a talkativeness that follows their persona (gossip GDD §3.1). The loud end
of campus is Megan 0.95 (drama-loving queen bee), Maya 0.9 (social connector), Ivy 0.85 (social strategist), Avery 0.8, Casey 0.75,
Ashley and Sofia 0.7. The quiet end is Mateo 0.1, Priya and Alex 0.15, Sam and Jade 0.2, and Nina 0.25 (she notices everything and tells
almost no one). Archetypes give ranges: campus influencers 0.8–0.95, theater kids and party critics 0.7–0.85, gamers 0.1–0.25.
High talkers tell more people per round and exaggerate more. The three most talkative (Megan, the generated influencer Kayla, and Maya
with the shipped seed) are the **hubs** who hear police-blotter and Dean news first. Schedules matter as much as the trait. Two
NPCs who share ≥ 4 scheduled hours a week at the same places can trade nasty stories when together, and ≥ 12 hours makes them close
friends who text when apart. Among the authored 32, that is 349 and 72 pairs respectively.
