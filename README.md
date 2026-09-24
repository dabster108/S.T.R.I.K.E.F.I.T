# S.T.R.I.K.E.F.I.T

### Design, Development, and Evaluation of a Virtual Reality-Based Gamified Fitness System

*Applying Immersive Virtual Reality and Gamification Techniques to Deliver Interactive, Movement-Based Gameplay Experiences to Promote Physical Activity and Long-Term Fitness Engagement Among Young Adults Aged 18–24*

A fitness game that only rewards you when you actually move like you mean it.

---

## The name

STRIKEFIT is not an abbreviation. It is a compound of two words: **strike** and **fit**. *Strike* is the mechanic the game is built around — a swing only registers when it has real controller velocity behind it, and the damage it deals scales with how hard it was thrown. *Fit* is the outcome: the only way to play well is to keep moving with real physical effort. The dots in S.T.R.I.K.E.F.I.T. are a stylisation, a nod to the way research and robotics systems are often named, and nothing more. The name is deliberately blunt: you strike, you get fit.

---

## The problem it solves

Most people aged 18 to 24 know exercise is good for them and still don't do it consistently. The reasons are familiar: gyms feel repetitive, home workouts get boring after a week or two, and fitness apps that just count reps or minutes give almost nothing back in the moment. The activity itself has to carry all the motivation, and for most people it doesn't, at least not for long.

Virtual reality fitness games try to fix this by wrapping exercise in something that feels like play instead of a chore. But a lot of them solve the wrong problem. They detect whether a gesture happened at all, not whether it was done with any real effort, so a player quickly learns the smallest motion that still counts, and the workout quietly disappears from the fitness game. The result looks interactive but doesn't actually demand much from the body, which defeats the point of building a fitness system in the first place.

STRIKEFIT starts from a different question: **what if the game simply couldn't be played well without real physical effort, because effort itself is the mechanic the game is scoring you on?**

---

## What the project does about it

**1. Damage is tied directly to how hard you swing, not whether you swung at all.**
The player wields a hammer in each hand, and the game tracks real controller velocity through the motion of each swing. A weak flick below a minimum speed threshold is ignored completely — it doesn't register as an attack. Above that threshold, swing speed scales the damage dealt, so a full committed swing does meaningfully more than a lazy one. There's no way to succeed at the game by moving less.

**2. The opponent fights back, so standing still isn't a viable strategy.**
The monster is not a passive target. It attacks on its own timer, with a clear wind-up before the hit lands, giving the player a real window to notice and react rather than an unavoidable scripted hit. If the player isn't in range when the attack resolves, it simply misses. This is what turns the game from repeatedly hitting a stationary object into something that asks the player to stay alert and keep moving.

**3. The fight has rhythm instead of being one repeated animation.**
The monster selects between different attacks depending on its own remaining health: a standard attack most of the time, a slower but heavier attack that becomes more common once it drops below half health, and a rare, high-commitment attack reserved for when it's nearly defeated. It also periodically raises its guard into a short defend state, during which incoming damage is reduced rather than fully applied, so a fight isn't just a flat race to zero.

**4. Every hit gives immediate feedback.**
Landing a hit on the monster's body, clashing weapon against weapon, taking a hit, and finishing the monster off all have their own distinct sound, layered against background music that's kept deliberately quiet so the combat effects stay clear. A hit on the player triggers a brief screen flash and camera shake, so feedback lands through more than one sense at once.

**5. The interface is built to sit in the room with the player, not float over their eyes.**
Health, score, timer, and experience points are shown on a panel fixed in place a short distance in front of the player when the battle starts, rather than a flat overlay stuck to the camera that would either ignore head movement or swim uncomfortably as the player looks around.

**6. Progress is scored on quality, not just contact.**
Every landed hit earns experience points on a scale tied to how full the swing actually was, so scoring reinforces the same thing the damage system does: real effort, not just touching the target.

---

## What makes it different

A lot of VR fitness titles fall into one of two patterns. Either they're closer to a rhythm game, matching a gesture to a timed cue with no real opponent behind it, or they're an arcade combat game where VR controllers were bolted on afterward with simple binary hit detection. STRIKEFIT tries to sit closer to real combat but takes the fitness half of the equation seriously as a design constraint rather than a label. The core damage mechanic is itself a measurement of physical effort, not a separate system layered over ordinary hit detection.

---

## Technical architecture

The game is built in Unity, targeting Meta Quest 2 through the Meta XR SDK and OpenXR. Player input comes entirely from tracked Touch controllers, and combat detection is based on collider overlap combined with real-time controller velocity rather than any external sensor.

Monster animation is built on Humanoid-retargeted Mixamo clips, covering idle, several attack variants, a defend pose, a hit reaction, and death, all driven through a Unity Animator Controller with health-weighted conditional transitions rather than a single repeating attack loop. Combat logic is split across separate scripts for player input and damage calculation, monster behaviour and attack selection, health and damage state for both player and monster, hit and collision detection, HUD display, and overall battle and game state management, so what counts as a hit stays separate from what the monster decides to do next.

Development used Cursor as the primary code editor, connected to a Unity MCP server so scene wiring, asset organisation, and script changes could be verified directly against the live Unity Editor session rather than relying on static code review alone.

---

## Levels

| Level | Difficulty | Environment | Monster weapon | Status |
|---|---|---|---|---|
| Level 1 | Easy | Lite Dungeon Pack (low poly, Frindness) | Sword | In active development, core combat loop functional |
| Level 2 | Medium | Lite Dungeon Pack (low poly, Frindness) | Sword | Planned |
| Level 3 | Hard | Old Seaport Environment | Hammer | Planned |

Level 1's core loop, covering swing detection, monster attack and defend behaviour, HP and XP tracking, and victory and defeat states, is implemented and has been tested on Quest 2 hardware. Levels 2 and 3 will reuse the same underlying combat systems with new environments and rebalanced monster parameters.

---

## Combat system, real values

These reflect the current implementation. Some values are early development defaults rather than final tuned numbers, this is noted where it applies.

- **Player weapon (Level 1):** dual hammers, one per hand, each tracked and scored independently against the same monster health pool.
- **Swing velocity gate:** attacks below a minimum controller velocity do not register as hits, so tapping quickly instead of swinging properly doesn't count as combat.
- **Velocity to damage scaling:** swing speed scales the damage multiplier up to a capped maximum, so faster, fuller swings do proportionally more damage.
- **Hit cooldown:** a short cooldown after a registered hit stops one swing from being counted as multiple separate hits.
- **Monster max health:** currently a development placeholder, pending adjustment once playtesting shows what a satisfying fight length feels like.
- **Player max health:** matched to the monster's, so the fight reads as balanced rather than weighted heavily toward either side by default.
- **Monster attack telegraph:** a fixed wind-up between the attack animation starting and the hit actually landing, giving the player a genuine window to react rather than an instant, unavoidable hit.
- **Monster attack tiers:** a standard attack used most often, a heavier attack that becomes more frequent once the monster drops below roughly half health, and a rare high-commitment attack reserved for when it's nearly defeated.
- **Monster defend state:** triggers on a periodic cooldown, during which incoming player damage is substantially reduced rather than fully applied.
- **Weapon clash:** a hammer strike landing on the monster's weapon collider instead of its body counts as a parry. It triggers a distinct sound and deals no damage, rather than being treated as a normal hit.

---

## Technologies used

- **Engine:** Unity, Meta XR SDK, OpenXR
- **Target hardware:** Meta Quest 2
- **Animation:** Mixamo animation clips retargeted onto Humanoid character rigs, driven through Unity Animator Controllers with conditional, health-weighted transitions
- **Audio:** layered sound design with distinct clips for hits, weapon clashes, attack telegraphs, damage taken, and death, mixed against lowered background music
- **Environments:** Lite Dungeon Pack (low poly, by Frindness) for Levels 1 and 2, Old Seaport Environment for Level 3, both sourced from the Unity Asset Store
- **Development tooling:** Cursor with a Unity MCP server, for AI-assisted, editor-integrated development and iteration
- **Version control:** Git, with a Unity-specific `.gitignore` and `.gitattributes` to keep generated editor files and large binaries out of the tracked history

---

## Who it is for

This project was built as an undergraduate thesis at Softwarica College, under the supervision of Manoj Shrestha, examining how gamified, movement-based VR gameplay affects physical activity levels and long-term engagement with fitness among young adults aged 18 to 24. It's aimed at researchers studying motivation in exergames, VR users looking for a more physically demanding alternative to gesture-matching fitness titles, and young adults who find traditional exercise routines difficult to stick with.

---

## Current status and known limitations

Being honest about what's finished and what isn't:

- Level 1's combat loop, including swing detection, monster behaviour, HP and XP tracking, and win and lose states, is implemented and has been tested on Quest 2 hardware. Issues found during testing, including UI input wiring, audio routing, and animator transition gaps, were identified and fixed through actual on-device testing rather than assumed correct from code alone.
- Levels 2 and 3 are not yet built. They'll reuse the Level 1 combat systems with new environment art and adjusted monster parameters.
- Monster max health and some damage values are early defaults, not the result of playtesting-based balancing yet.
- A dedicated monster hit-reaction animation and additional attack variety were added late in development. Some of the animator wiring for these is still being verified against actual runtime behaviour.
- No biometric tracking, such as heart rate or calorie estimation, is currently implemented.

---

## Future prospects

- Complete and balance Levels 2 and 3, including a distinct attack set for the hammer-wielding monster in the seaport environment.
- Rebalance health, damage, and cooldown values based on actual playtesting data rather than current development defaults.
- Evaluate whether the velocity-linked damage system measurably increases physical exertion and session engagement compared to a standard, binary hit-detection exergame, which is the central question this thesis sets out to answer.
- Explore longer-term engagement, such as whether players return to the game across multiple sessions, and what that suggests about the format's potential for sustained fitness habits.

---

## Acknowledgements

Softwarica College, for the resources and environment to build this project, and Manoj Shrestha, for supervision throughout its development. Thanks also to Frindness for the Lite Dungeon Pack environment, the creators of the Old Seaport Environment pack, Adobe Mixamo for animation resources, and the Unity and Meta XR SDK communities whose documentation made a project of this scope possible for a single developer working toward a thesis deadline.

---

*This README describes the project as it stands during active thesis development. Combat balance values, level count, and feature scope are expected to change as development and playtesting continue. See the commit history for the most current implementation details.*
