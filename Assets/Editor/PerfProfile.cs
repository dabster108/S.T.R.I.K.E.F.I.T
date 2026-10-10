// PerfProfile.cs
// Purpose: Play-mode render / script load per scene with this pass's features OFF (the old behaviour) vs ON, alternating 4 s windows
//          inside one run. Editor only. Job "perf": levels start a fight and fight on its own (boss active, player idle); the menu
//          just sits. Measures UnityStats triangles, draw calls, SetPass, shadow casters, realtime lights, live particles, and
//          the per-frame cost of the new scripts through their profiler markers.
//          OFF = WeaponSurfaceProxy off, MonsterGlow.powerUp / MonsterAxeFlames.chargeFlare + emberTrail off, DragonHazard off.
//          ON  = all on, plus the dragon stand-in (Level 3, 2 dragons: the hazard with no model).
// Attach to: nothing (static)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class PerfProfile
{
    static IEnumerator job;
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static string Status() => (job != null ? "RUNNING\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0; job = Perf();
        EditorApplication.update += Tick;
        return "started";
    }
    public static void Stop() { job = null; EditorApplication.update -= Tick; }
    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try { if (!job.MoveNext()) { log.AppendLine("DONE"); Stop(); } }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    static void Hide()
    {
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = pnp != null && f != null ? f.GetValue(pnp) as GameObject : null;
        if (panel != null) panel.SetActive(false);
    }

    class Acc { public double tris, dc, sp, sc, lights, parts, proxyUs, flamesUs, dragonUs; public int n; }

    static IEnumerator Perf()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool menu = scene == "MainMenu";
        MonsterController mon = null; DragonHazard hz = null; MonsterGlow glow = null; MonsterAxeFlames flames = null;
        if (!menu)
        {
            CombatVerification.frameDt = 1f / 72f; CombatVerification.Prepare(); Hide();
            var pc = PlayerCombat.Instance;
            if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
            yield return null; yield return null;
            BattleManager.Instance.StartBattleFromInspector();
            float w0 = Time.time;
            while (Time.time - w0 < 25f) { mon = BattleManager.Instance.ActiveMonster; if (mon != null && mon.isAlive) { var e = mon.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; } yield return null; }
            for (int i = 0; i < 20; i++) yield return null;
            glow = mon.GetComponent<MonsterGlow>(); flames = mon.GetComponent<MonsterAxeFlames>();
            hz = Object.FindAnyObjectByType<DragonHazard>();
            if (hz != null) { hz.dragonsEnabled = true; hz.allowStandIn = true; hz.dragonsByPhase = new[] { 2, 2, 2 }; hz.LogEvents = false;
                var h = mon.GetComponent<MonsterHealth>(); var d = h.currentHealth - h.maxHealth * 0.3f; if (d > 0f) h.TakeDamage(d, mon.transform.position + Vector3.up);   // enrage: both windows run in the enraged fight, the stand-in brings 2 dragons
                float wp = Time.time; while (Time.time - wp < 6f) yield return null; }
        }
        var proxies = menu ? new WeaponSurfaceProxy[0] : Object.FindObjectsByType<WeaponSurfaceProxy>(FindObjectsInactive.Include);
        var recP = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "WeaponSurfaceProxy.Update");
        var recF = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "MonsterAxeFlames.Update");
        var recD = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "DragonHazard.Update");
        var accOn = new Acc(); var accOff = new Acc();
        long gc0 = System.GC.GetTotalMemory(false);
        for (int round = 0; round < 8; round++)
        {
            bool on = round % 2 == 1;
            foreach (var p in proxies) if (p != null) p.proxyEnabled = on;
            if (glow != null) glow.powerUp = on;
            if (flames != null) { flames.chargeFlare = on; flames.emberTrail = on; }
            if (hz != null)
            {
                hz.enabled = on;
                if (on && hz.ActiveDragons == 0 && mon != null) { /* the stand-in enters on a phase shift: force one so 2 dragons exist */ }
            }
            var acc = on ? accOn : accOff;
            float end = Time.time + 4f;
            while (Time.time < end)
            {
                yield return null;
                acc.n++;
                acc.tris += UnityStats.triangles; acc.dc += UnityStats.drawCalls; acc.sp += UnityStats.setPassCalls; acc.sc += UnityStats.shadowCasters;
                if (recP.Valid) acc.proxyUs += recP.LastValue / 1000.0;
                if (recF.Valid) acc.flamesUs += recF.LastValue / 1000.0;
                if (recD.Valid) acc.dragonUs += recD.LastValue / 1000.0;
                if (acc.n % 12 == 0)
                {
                    int rl = 0; foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.enabled && l.intensity > 0f) rl++;
                    int pc = 0; foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude)) pc += ps.particleCount;
                    acc.lights = rl; acc.parts = Mathf.Max((float)acc.parts, pc);
                }
            }
        }
        recP.Dispose(); recF.Dispose(); recD.Dispose();
        string D(Acc a, double v) => (a.n > 0 ? v / a.n : 0).ToString("0.0");
        log.AppendLine($"== PERF {scene}{(menu ? " (menu: no features to toggle)" : "")}: windows of 4 s alternating, frames OFF {accOff.n} / ON {accOn.n}");
        log.AppendLine($"  features OFF: triangles {D(accOff, accOff.tris)}, draw calls {D(accOff, accOff.dc)}, SetPass {D(accOff, accOff.sp)}, shadow casters {D(accOff, accOff.sc)}, realtime lights {accOff.lights}, live particles (max seen) {accOff.parts}");
        log.AppendLine($"  features ON : triangles {D(accOn, accOn.tris)}, draw calls {D(accOn, accOn.dc)}, SetPass {D(accOn, accOn.sp)}, shadow casters {D(accOn, accOn.sc)}, realtime lights {accOn.lights}, live particles (max seen) {accOn.parts}");
        log.AppendLine($"  script cost per frame (us, profiler markers): proxy {D(accOn, accOn.proxyUs)} (off {D(accOff, accOff.proxyUs)}), axe flames {D(accOn, accOn.flamesUs)} (off {D(accOff, accOff.flamesUs)}), dragon hazard {D(accOn, accOn.dragonUs)} (off {D(accOff, accOff.dragonUs)})");
        log.AppendLine($"  GC memory change over the run: {(System.GC.GetTotalMemory(false) - gc0) / 1024} KB (heap, includes the Editor)");
    }
}
