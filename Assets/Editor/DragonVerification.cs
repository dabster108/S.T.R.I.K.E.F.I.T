// DragonVerification.cs
// Purpose: Play-mode probes for the Level 3 dragon fire hazard (Editor only, never in a build). Driven over Unity MCP.
//          The shipped scene has the hazard switched OFF (no dragon model within the asset budget), so every job turns on the
//          invisible stand-in emitter (allowStandIn) - the mechanic, fairness rules, telemetry, culling and cost are real; only the
//          model is absent. "cost:model" temporarily uses the imported source dragon (Assets/_DragonProbe) to show what the
//          model itself would cost.
//          Jobs: "unit"      scripted phases: 0 dragons, phase 1 (1 dragon) and phase 2 (2 dragons); a stay-in, a step-out-during-the-
//                            marker and a step-out-after-tick-2 event; gaps, marker length, damage per tick, zone drift, culling.
//                "fight"     100 s fight (AdaptiveGuardVerification.Fight, varied swings in bursts of 2-3) with the HP staged into
//                            phase 1 at 25 s and the enrage at 60 s; fairness against the boss's must-dodge attacks, phase shifts, guards.
//                "stress"    60 s with the boss's must-dodge weight x40 to force overlaps; the gate must keep every event 2.5 s clear.
//                "cost"      per-frame script cost, particles, draw calls with the hazard on vs off during events.
// Dependencies: DragonHazard, BossPhaseController, MonsterAI, PlayerHealth, AdaptiveGuardVerification, CombatVerification
// Attach to: nothing (static)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class DragonVerification
{
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string jobName = "";

    public static string Status() => (job != null ? $"RUNNING {jobName} t={Time.time:0.0}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0; jobName = name;
        if (name == "unit") job = Unit();
        else if (name == "fight") job = Fight(false);
        else if (name == "stress") job = Fight(true);
        else if (name == "cost") job = Cost(false);
        else if (name == "cost:model") job = Cost(true);
        else return "unknown job " + name;
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear(); EditorApplication.update -= Tick;
        DragonHazard.FireResolved -= OnFire; PlayerHealth.DamageTaken -= OnDamage;
        AdaptiveGuardVerification.frameHook = null; AdaptiveGuardVerification.reportHook = null;
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            while (true)
            {
                var top = stack.Count > 0 ? stack.Peek() : job;
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } break; }
                if (stack.Count > 0) { stack.Pop(); continue; }
                log.AppendLine("DONE"); Stop(); break;
            }
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    // ───────────────────────── shared setup ─────────────────────────
    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
    static DragonHazard hz;
    static OVRCameraRig rig;
    static Transform Head => rig.centerEyeAnchor;

    static void HideNameBoard()
    {
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = pnp != null && f != null ? f.GetValue(pnp) as GameObject : null;
        if (panel != null) panel.SetActive(false);
    }

    static void EnableHazard(bool on)
    {
        hz = Object.FindAnyObjectByType<DragonHazard>();
        hz.dragonsEnabled = on; hz.allowStandIn = on; hz.LogEvents = true;
    }

    static IEnumerator StartFight(bool startBattle)
    {
        CombatVerification.frameDt = 1f / 72f;
        CombatVerification.Prepare();
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        HideNameBoard();
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        if (!startBattle) yield break;
        if (M == null || !M.isAlive) BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 25f)
        {
            var m = M;
            if (m != null && m.isAlive) { var e = m.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        yield return Frames(5);
    }

    static void ParkBoss(MonsterController mon)
    {
        var ai = mon.GetComponent<MonsterAI>();
        mon.attackCooldown = 9999f;
        var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (baseCd != null) baseCd.SetValue(ai, 9999f);
        ai.chainChanceByPhase = Vector3.zero;
        var def = mon.GetComponent<MonsterDefense>(); if (def != null) { def.reactiveGuard = false; def.timedGuard = false; }
    }

    static void HitTo(MonsterController mon, float fraction)
    {
        var h = mon.GetComponent<MonsterHealth>();
        float dmg = h.currentHealth - h.maxHealth * fraction;
        if (dmg > 0f) h.TakeDamage(dmg, mon.transform.position + Vector3.up * 1.2f);
    }

    // ───────────────────────── events ─────────────────────────
    static readonly List<DragonHazard.FireReport> reports = new List<DragonHazard.FireReport>();
    static readonly List<float> reportTimes = new List<float>();
    static float damageFromDragon; static int dragonDamageEvents; static readonly List<float> tickAmounts = new List<float>();
    static void OnFire(DragonHazard.FireReport r) { reports.Add(r); reportTimes.Add(Time.time); }
    static void OnDamage(float amount, bool blocked, float after)
    {
        if (DragonHazard.DamageSourceOverride == null) return;
        damageFromDragon += amount; dragonDamageEvents++; tickAmounts.Add(amount);
    }

    
    /// <summary>Per-frame monitor of the hazard: builds the event list (marker / fire / end), zone drift, flame distance.</summary>
    static int violationsOverlap, violationsShift, violationsEntrance; static float minDrawnCentre = 99f, minDrawnSurface = 99f;
    static bool mustDodgeNow; static float mustStart; static readonly List<Vector2> mustDodges = new List<Vector2>();
    static ParticleSystem.Particle[] pbuf = new ParticleSystem.Particle[256];
    static MonsterAI bossAi; static BossPhaseController bossPhases;
    static int phaseSeen = -1; static readonly List<string> phaseLog = new List<string>();
    static float startT;

    static float driftMax; static readonly Dictionary<int, float> driftByEvent = new Dictionary<int, float>();
    static void Monitor()
    {
        if (hz.EventRunning && hz.Records.Count > 0)
        {
            int i = hz.Records.Count - 1; float d = Vector3.Distance(hz.ZoneCentre, hz.Records[i].zone);
            driftByEvent[i] = Mathf.Max(driftByEvent.TryGetValue(i, out var o) ? o : 0f, d);
        }
        // boss must-dodge intervals
        bool md = bossAi != null && bossAi.CurrentAttack != null && bossAi.CurrentAttack.unblockable;
        if (md && !mustDodgeNow) mustStart = Time.time;
        if (!md && mustDodgeNow) mustDodges.Add(new Vector2(mustStart, Time.time));
        mustDodgeNow = md;
        if (hz.EventRunning && md) violationsOverlap++;
        if (hz.EventRunning && bossPhases != null && bossPhases.InPhaseShift) violationsShift++;
        if (hz.EventRunning && M != null) { var e = M.GetComponent<MonsterEntrance>(); if (e != null && !e.Complete) violationsEntrance++; }
        if (bossPhases != null && bossPhases.Phase != phaseSeen) { phaseSeen = bossPhases.Phase; phaseLog.Add($"{Time.time - startT:0.0} s phase {phaseSeen}, dragons {hz.ActiveDragons}"); }
        // flame particles as they were drawn last frame (post culling): distance to the head
        if (hz.EventRunning && hz.StateName == "Fire")
        {
            foreach (var ps in hz.FlameSystems)
            {
                if (ps == null) continue;
                if (pbuf.Length < ps.main.maxParticles) pbuf = new ParticleSystem.Particle[ps.main.maxParticles + 8];
                int n = ps.GetParticles(pbuf);
                Vector3 h = Head.position;
                for (int i = 0; i < n; i++)
                {
                    float d = (pbuf[i].position - h).magnitude;
                    float surf = d - 0.5f * pbuf[i].GetCurrentSize(ps);
                    if (d < minDrawnCentre) minDrawnCentre = d;
                    if (surf < minDrawnSurface) minDrawnSurface = surf;
                }
            }
        }
    }

    static void ResetMonitors()
    {
        reports.Clear(); reportTimes.Clear(); damageFromDragon = 0f; dragonDamageEvents = 0; tickAmounts.Clear();
        driftByEvent.Clear(); hz.Records.Clear(); hz.EventLog.Clear();
        violationsOverlap = violationsShift = violationsEntrance = 0; minDrawnCentre = minDrawnSurface = 99f;
        mustDodgeNow = false; mustDodges.Clear(); phaseSeen = -1; phaseLog.Clear(); startT = Time.time;
        DragonHazard.FireResolved -= OnFire; PlayerHealth.DamageTaken -= OnDamage;
        DragonHazard.FireResolved += OnFire; PlayerHealth.DamageTaken += OnDamage;
        var mon = M; bossAi = mon != null ? mon.GetComponent<MonsterAI>() : null; bossPhases = mon != null ? mon.GetComponent<BossPhaseController>() : null;
    }

    static string EventTable()
    {
        var sb = new StringBuilder();
        sb.AppendLine("  # | dragon | phase | marker at | marker s | fire s | zone hit | ticks hit | dmg | zone drift | gap since previous end");
        float prevEnd = -1f;
        for (int i = 0; i < hz.Records.Count; i++)
        {
            var e = hz.Records[i];
            bool done = e.end > 0f && !e.aborted && e.fire > 0f;
            string gap = prevEnd > 0f ? (e.marker - prevEnd).ToString("0.00") : "-";
            driftByEvent.TryGetValue(i, out float drift);
            sb.AppendLine($"  {i + 1,2} | {e.dragon,6} | {e.phase,5} | {e.marker - startT,8:0.00} | {(e.fire > 0 ? e.fire - e.marker : 0f),8:0.00} | {(done ? e.end - e.fire : 0f),6:0.00} | {(done ? (e.ticksHit > 0).ToString() : (e.aborted ? "aborted" : "-")),8} | {(done ? e.ticksHit + "/" + e.ticks : "-"),9} | {(done ? e.damage.ToString("0.0") : "-"),4} | {drift * 100f,7:0.0} cm | {gap}");
            if (e.end > 0f) prevEnd = e.end;
        }
        return sb.ToString();
    }

    static string Invariants(float phase1Gap, float phase2Gap)
    {
        var sb = new StringBuilder();
        var recs = hz.Records;
        float minMarker = 99f, maxDrift = 0f, minGap1 = 99f, minGap2 = 99f; int simultaneous = 0, repeatedDragon = 0, resolved = 0, aborted = 0;
        for (int i = 0; i < recs.Count; i++)
        {
            var e = recs[i];
            if (e.fire > 0f) minMarker = Mathf.Min(minMarker, e.fire - e.marker);
            if (driftByEvent.TryGetValue(i, out float dr)) maxDrift = Mathf.Max(maxDrift, dr);
            if (e.aborted) aborted++; else if (e.end > 0f) resolved++;
            if (i > 0 && recs[i - 1].end > 0f)
            {
                float gap = e.marker - recs[i - 1].end;
                if (gap < 0f) simultaneous++;
                if (e.phase >= 2) minGap2 = Mathf.Min(minGap2, gap); else minGap1 = Mathf.Min(minGap1, gap);
                if (e.phase >= 2 && e.dragon == recs[i - 1].dragon && hz.ActiveDragons > 1) repeatedDragon++;
            }
        }
        // distance to the boss's must-dodge intervals (before or after) for every event: wind-up .. recovery of the attack vs marker .. end of flame
        float minToMust = 99f;
        foreach (var e in recs)
        {
            if (e.end <= 0f) continue;
            foreach (var m in mustDodges) minToMust = Mathf.Min(minToMust, Mathf.Max(e.marker - m.y, m.x - e.end));
        }
        sb.AppendLine($"  invariants: events {recs.Count} (resolved {resolved}, aborted {aborted}); shortest marker before flame {(minMarker < 99f ? minMarker.ToString("0.00") : "-")} s (rule >= 1.4); max zone drift while an event ran {maxDrift * 100f:0.0} cm (rule 0: fixed once shown); overlapping events {simultaneous} (rule 0)");
        sb.AppendLine($"  gaps (end -> next marker, the hazard's own clock): phase 1 min {(minGap1 < 99f ? minGap1.ToString("0.00") : "-")} s (rule >= {phase1Gap}), phase 2 min {(minGap2 < 99f ? minGap2.ToString("0.00") : "-")} s (rule >= {phase2Gap}); same dragon twice in a row in phase 2 {repeatedDragon}");
        sb.AppendLine($"  must-dodge boss attacks seen {mustDodges.Count}: closest event to one (marker..end of flame vs the attack's wind-up..recovery) {(minToMust < 99f ? minToMust.ToString("0.00") : "-")} s (rule >= {hz.mustDodgeGuardSeconds}); frames with an event running during a must-dodge {violationsOverlap}, during a phase shift {violationsShift}, during the entrance {violationsEntrance} (all must be 0)");
        sb.AppendLine($"  flames: closest particle centre to the head {(minDrawnCentre < 99f ? minDrawnCentre.ToString("0.00") : "-")} m, closest particle surface {(minDrawnSurface < 99f ? minDrawnSurface.ToString("0.00") : "-")} m (rule: centre >= {hz.flameCullRadius}); culled {hz.CulledParticles}; live max seen {hz.LiveFlameParticles}; dragons never closer than {hz.MinDragonHeadDistance:0.0} m (rule >= {hz.minHeadDistance}); circle height {hz.circleHeight} m");
        sb.AppendLine($"  damage to the player from dragon fire: {damageFromDragon:0.0} in {dragonDamageEvents} tick(s); every tick {(tickAmounts.Count > 0 ? string.Join("/", new SortedSet<string>(tickAmounts.ConvertAll(x => x.ToString("0.0")))) : "-")}; rule = {hz.tickDamageFraction * 100f:0.0}% of {PlayerHealth.Instance.maxHealth:0} = {hz.tickDamageFraction * PlayerHealth.Instance.maxHealth:0.0} per tick");
        sb.AppendLine($"  phases: {string.Join(" | ", phaseLog)}");
        return sb.ToString();
    }

    // ───────────────────────── unit ─────────────────────────
    static IEnumerator WaitState(string state, float timeout)
    {
        float t0 = Time.time;
        while (hz.StateName != state && Time.time - t0 < timeout) { Monitor(); yield return null; }
    }

    static IEnumerator WaitIdleAfterEvent(float timeout)
    {
        float t0 = Time.time;
        while (hz.StateName != "Idle" && Time.time - t0 < timeout) { Monitor(); yield return null; }
    }

    static IEnumerator Unit()
    {
        EnableHazard(true);
        yield return StartFight(true);
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        ParkBoss(mon);
        var def = mon.GetComponent<MonsterDefense>(); if (def != null) def.enabled = false;
        ResetMonitors();
        var ph = PlayerHealth.Instance; ph.FullHeal();
        var ts = rig.trackingSpace; Vector3 tsRest = ts.position;
        log.AppendLine($"== DRAGON unit (stand-in emitter, boss parked): player max HP {ph.maxHealth:0}, boss HP {mon.GetComponent<MonsterHealth>().maxHealth:0}, phase thresholds {bossPhases.phaseThresholds[0]}/{bossPhases.phaseThresholds[1]}");

        // phase 0: no dragons, no events
        float t0 = Time.time; while (Time.time - t0 < 8f) { Monitor(); yield return null; }
        log.AppendLine($"phase 0 for 8 s: dragons {hz.ActiveDragons} (rule: {hz.dragonsByPhase[0]}), events {hz.Records.Count}");

        // phase 1: one dragon enters during the shift
        HitTo(mon, 0.62f);
        float shiftStart = Time.time;
        while (!bossPhases.InPhaseShift && Time.time - shiftStart < 3f) { Monitor(); yield return null; }
        float t1 = Time.time; var dragonDuringShift = 0; float firstDragonAt = -1f;
        while (bossPhases.InPhaseShift && Time.time - t1 < 6f) { Monitor(); if (hz.ActiveDragons > 0 && firstDragonAt < 0f) firstDragonAt = Time.time - shiftStart; if (hz.EventRunning) dragonDuringShift++; yield return null; }
        log.AppendLine($"phase 1: phase shift lasted {Time.time - shiftStart:0.00} s; dragons {hz.ActiveDragons} (rule {hz.dragonsByPhase[1]}), first appeared {(firstDragonAt >= 0f ? firstDragonAt.ToString("0.00") : "-")} s after the shift began; events during the shift {dragonDuringShift}");
        yield return Frames(30);

        // A: stay in the zone -> 4 ticks
        yield return WaitState("Marker", 20f);
        int a0 = reports.Count; float before = ph.currentHealth;
        yield return WaitIdleAfterEvent(10f); yield return Frames(3);
        log.AppendLine($"A stay in the zone: report {(reports.Count > a0 ? $"hit {reports[a0].zoneHit}, ticks {reports[a0].ticksHit}/{reports[a0].ticks}, damage {reports[a0].damage:0.0}" : "none")}; player HP {before:0} -> {ph.currentHealth:0}");
        ph.FullHeal();

        // B: step out (2.4 m sideways) 0.5 s into the marker, back after the event
        yield return WaitState("Marker", 25f);
        int b0 = reports.Count; float mStart = Time.time;
        while (Time.time - mStart < 0.5f) { Monitor(); yield return null; }
        ts.position = tsRest + Vector3.right * 2.4f;
        yield return WaitIdleAfterEvent(10f); ts.position = tsRest; yield return Frames(3);
        log.AppendLine($"B step out during the marker: report {(reports.Count > b0 ? $"hit {reports[b0].zoneHit}, ticks {reports[b0].ticksHit}/{reports[b0].ticks}, damage {reports[b0].damage:0.0}" : "none")}");

        // C: stay for ticks 1-2, step out after tick 2 (0.9 s into the flame)
        yield return WaitState("Fire", 30f);
        int c0 = reports.Count; float fStart = Time.time;
        while (Time.time - fStart < 0.9f) { Monitor(); yield return null; }
        ts.position = tsRest + Vector3.right * 2.4f;
        yield return WaitIdleAfterEvent(10f); ts.position = tsRest; yield return Frames(3);
        log.AppendLine($"C stay for 2 ticks, then step out: report {(reports.Count > c0 ? $"hit {reports[c0].zoneHit}, ticks {reports[c0].ticksHit}/{reports[c0].ticks}, damage {reports[c0].damage:0.0}" : "none")}");
        ph.FullHeal();

        // D: the zone does not follow the player: step out at the marker, back in before the flame, still takes the full strip? (no: zone fixed where the head WAS)
        yield return WaitState("Marker", 25f);
        int d0 = reports.Count; float dm = Time.time;
        ts.position = tsRest + Vector3.right * 2.4f;                      // leaves at once...
        while (Time.time - dm < 1.2f) { Monitor(); yield return null; }
        ts.position = tsRest;                                             // ...and comes back before the flame starts (marker 1.5 s): inside again
        yield return WaitIdleAfterEvent(10f); yield return Frames(3);
        log.AppendLine($"D leave and come back before the flame: report {(reports.Count > d0 ? $"hit {reports[d0].zoneHit}, ticks {reports[d0].ticksHit}/{reports[d0].ticks}" : "none")} (the zone stays where the head was when the marker appeared)");
        ph.FullHeal();

        // phase 2: enrage, two dragons alternate
        HitTo(mon, 0.30f);
        shiftStart = Time.time;
        while (!bossPhases.InPhaseShift && Time.time - shiftStart < 3f) { Monitor(); yield return null; }
        float tt = Time.time;
        while (bossPhases.InPhaseShift && Time.time - tt < 8f) { Monitor(); yield return null; }
        log.AppendLine($"phase 2 (enrage): shift lasted {Time.time - shiftStart:0.00} s; dragons {hz.ActiveDragons} (rule {hz.dragonsByPhase[2]})");
        int p2first = hz.Records.Count;
        float tw = Time.time;
        while (hz.Records.Count < p2first + 4 && Time.time - tw < 60f) { Monitor(); ph.FullHeal(); yield return null; }
        yield return WaitIdleAfterEvent(10f); yield return Frames(3);
        log.AppendLine($"phase 2: {hz.Records.Count - p2first} events in {Time.time - tw:0.0} s");

        // the battle ends: the flames and the marker stop at once, dragons fly off
        { var mh = mon.GetComponent<MonsterHealth>(); mh.currentHealth = 1f; mon.OnTakeDamage(200f, mon.transform.position + Vector3.up, 9f, false); }
        float te = Time.time; while (Time.time - te < 3.5f) { Monitor(); yield return null; }
        log.AppendLine($"after the boss died: boss alive {mon.isAlive}, battle active {BattleManager.Instance.IsBattleActive}, hazard state {hz.StateName}, dragons left {hz.ActiveDragons} (they fly off over 2 s), marker showing {(hz.zoneIndicator != null && hz.zoneIndicator.IsShowing)}, last event aborted {(hz.Records.Count > 0 && hz.Records[hz.Records.Count - 1].aborted)}");
        log.AppendLine(EventTable());
        log.Append(Invariants(hz.gapPhase1, hz.gapPhase2));
        log.AppendLine($"  event log: {string.Join(" | ", hz.EventLog.GetRange(0, Mathf.Min(hz.EventLog.Count, 60)))}");
        ts.position = tsRest;
    }

    // ───────────────────────── fight / stress ─────────────────────────
    static int stage;
    static IEnumerator Fight(bool stress)
    {
        EnableHazard(true);
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        stage = 0;
        // the AdaptiveGuardVerification fight starts the battle itself; the monitors start with it
        float t0 = 0f;
        System.Action<float> hook = t =>
        {
            var mon = M; if (mon == null) return;
            if (bossAi == null || bossAi.gameObject != mon.gameObject) ResetMonitors();
            float t1 = stress ? 6f : 25f, t2 = stress ? 24f : 60f;
            if (stage == 0 && t >= t1) { stage = 1; HitTo(mon, 0.62f); }
            else if (stage == 1 && t >= t2) { stage = 2; HitTo(mon, 0.30f); }
            if (stress && stage == 0 && bossAi != null) bossAi.unblockableWeightByPhase = new Vector3(40f, 40f, 40f);
            Monitor();
        };
        AdaptiveGuardVerification.frameHook = hook;
        AdaptiveGuardVerification.seconds = stress ? 60f : 100f;
        AdaptiveGuardVerification.reportHook = () =>
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== DRAGONS in this fight ({(stress ? "stress: must-dodge weight x40" : "normal weights")}), stand-in emitter, HP staged into phase 1 at {(stress ? 6 : 25)} s and the enrage at {(stress ? 24 : 60)} s");
            sb.AppendLine(EventTable());
            sb.Append(Invariants(hz.gapPhase1, hz.gapPhase2));
            sb.AppendLine($"  dragon_fire reports {reports.Count}: zone hit {reports.FindAll(r => r.zoneHit).Count}, dodged {reports.FindAll(r => !r.zoneHit).Count}");
            return sb.ToString();
        };
        bossAi = null;
        AdaptiveGuardVerification.Run("fight:varied");
        yield return Frames(2);
        while (AdaptiveGuardVerification.Running) yield return null;
        log.AppendLine(AdaptiveGuardVerification.Status());
        AdaptiveGuardVerification.frameHook = null; AdaptiveGuardVerification.reportHook = null;
    }

    // ───────────────────────── cost ─────────────────────────
    static IEnumerator Cost(bool useModel)
    {
        EnableHazard(true);
        if (useModel)
        {
            hz.dragonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_DragonProbe/dragon.fbx");
            hz.dragonScale = 0.05f; hz.allowStandIn = false;
            if (hz.dragonPrefab == null) { log.AppendLine("no probe model"); yield break; }
        }
        yield return StartFight(true);
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        ParkBoss(mon);
        var def = mon.GetComponent<MonsterDefense>(); if (def != null) def.enabled = false;
        ResetMonitors();
        PlayerHealth.Instance.FullHeal();
        HitTo(mon, 0.30f);                                   // enrage: two dragons
        float s0 = Time.time; while (!bossPhases.InPhaseShift && Time.time - s0 < 3f) yield return null;
        while (bossPhases.InPhaseShift) yield return null;
        yield return Frames(120);
        var renderers = new List<Renderer>();
        foreach (var d in GameObject.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)) if (d.gameObject.name.StartsWith("Dragon") || d.transform.root.name.StartsWith("Dragon_")) renderers.Add(d);
        long tris = 0; foreach (var r in renderers) { var smr = r as SkinnedMeshRenderer; if (smr != null && smr.sharedMesh != null) tris += smr.sharedMesh.triangles.Length / 3; }
        log.AppendLine($"== DRAGON cost ({(useModel ? "SOURCE MODEL as-is, scale 0.05, 2 dragons" : "stand-in emitter, 2 dragons")}): dragons {hz.ActiveDragons}, renderers {renderers.Count}, skinned triangles {tris}");
        // alternate on / off every 2 s of frames; measure frame time (ms), draw calls, scripts: DragonHazard.Update marker, GC
        var marker = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "DragonHazard.Update");
        long gc0 = System.GC.GetTotalMemory(false);
        var onMs = new List<float>(); var offMs = new List<float>(); var onDc = new List<float>(); var offDc = new List<float>(); var onSet = new List<float>(); var offSet = new List<float>();
        float fireSeen = 0; int maxLive = 0; long scriptNs = 0; int scriptFrames = 0;
        for (int round = 0; round < 8; round++)
        {
            bool on = round % 2 == 0;
            hz.enabled = on;
            foreach (var d in hz.FlameSystems) if (d != null) d.gameObject.SetActive(on);
            foreach (var r in renderers) if (r != null) r.enabled = on;
            // each round lasts 5 s: includes a marker + flame in the "on" rounds when the scheduler allows it
            var wait = Time.time + 5f;
            while (Time.time < wait)
            {
                yield return null;
                float ms = Time.unscaledDeltaTime * 1000f;
                (on ? onMs : offMs).Add(ms);
                (on ? onDc : offDc).Add(UnityEditor.UnityStats.drawCalls);
                (on ? onSet : offSet).Add(UnityEditor.UnityStats.setPassCalls);
                if (on && hz.StateName == "Fire") fireSeen += Time.deltaTime;
                if (on) { maxLive = Mathf.Max(maxLive, hz.LiveFlameParticles); if (marker.Valid && marker.LastValue > 0) { scriptNs += marker.LastValue; scriptFrames++; } }
            }
        }
        marker.Dispose();
        float Mean(List<float> l) { float s = 0f; foreach (var v in l) s += v; return l.Count > 0 ? s / l.Count : 0f; }
        log.AppendLine($"  hazard ON : frame {Mean(onMs):0.00} ms (n={onMs.Count}), draw calls {Mean(onDc):0.0}, SetPass {Mean(onSet):0.0}, flame seconds seen {fireSeen:0.0}, max live flame particles {maxLive}");
        log.AppendLine($"  hazard OFF: frame {Mean(offMs):0.00} ms (n={offMs.Count}), draw calls {Mean(offDc):0.0}, SetPass {Mean(offSet):0.0}");
        log.AppendLine($"  DragonHazard.Update script cost: mean {(scriptFrames > 0 ? scriptNs / (double)scriptFrames / 1000.0 : 0):0.0} us per frame over {scriptFrames} frames (profiler marker)");
        log.AppendLine("  note: Editor frame times are noisy (the Editor runs the Game view and the harness); the draw-call and triangle numbers are exact.");
        hz.enabled = true;
    }
}
