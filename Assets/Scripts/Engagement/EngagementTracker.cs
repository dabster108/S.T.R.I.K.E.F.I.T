// EngagementTracker.cs
// Purpose: The headset side of the engagement features, one per level scene (it adds itself to BattleSystems on every scene load).
//            - Fight start: the player's progress from this headset (PlayerProgressStore) plus, in the background, the website's
//              numbers when it answers (rank effort points, best score on this level, today's challenges); the higher of the two is
//              kept for points and bests, so an offline fight or a race with the result POST never lowers them.
//            - During the fight: today's three goals (the website's daily challenges, or three seeded local goals offline) checked
//              after hits, with a GOAL COMPLETE banner once per goal; a local goal adds its bonus XP to the fight, a website goal's
//              bonus is the website's (shown, never added twice). PERFECT CLASH: a clash just before the strike connects
//              (PerfectClashRule, window in EngagementConfig) gives bonus XP, its own flash, haptics and, once the attack is over, a
//              short stagger. The monster's damage is never changed by it.
//            - Fight end (victory / defeat from the result panel, abandoned from SessionReporter.SessionCompleting): effort points
//              (the website's formula), RANK UP once, the best on this level (a first fight is never "NEW BEST"), the next goal,
//              goal completions, the fights played today; saved at once; and one "engagement_summary" event.
//          Allocation: nothing per frame (goals are checked only after a hit; LateUpdate returns at once otherwise).
// Dependencies: BattleManager, SessionReporter, XPManager, BattleStats, HitDetection, MonsterAI, MonsterColorCompositor,
//               PlayerWeaponGlow, CombatFeedback, CombatUIController, StrikeFit.Engagement (core rules), EngagementConfig
// Attach to: nothing (adds itself to the BattleManager's GameObject)

using System.Globalization;
using System.Text;
using StrikeFit.Engagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class EngagementTracker : MonoBehaviour
{
    public static EngagementTracker Instance { get; private set; }

    /// <summary>Everything the result screen shows about one finished fight.</summary>
    public class FightResult
    {
        public string outcome, level, levelName, player;
        public bool victory, counted;
        public int score, previousBest, bestAfter;
        public BestScore.Kind bestKind;
        public float peakSwing, fullShare;
        public int countedSwings, fullSwings, damageDealt, xp, bestCombo;
        public int effortBefore, effortAfter, effortGained, rankBefore, rankAfter;
        public bool rankUp;
        public string nextGoal;
        public GoalDef[] goals;
        public float[] goalValues;
        public bool[] goalDone, goalDoneThisFight;
        public bool goalsFromServer;
        public int goalsCompletedThisFight, goalBonusXp, perfectClashes, perfectClashXp, fightsToday;
        public bool[] goalDoneAtEnd;   // completed by the fight's end itself (a win, one more fight): announced on the result screen
    }

    public FightResult Result { get; private set; }
    public bool Running => running;
    public int PerfectClashes => perfectClashes;
    public GoalDef[] Goals => goals;
    public int EffortNow => effortBefore;
    public string Player => player;

    EngagementConfig cfg;
    BattleManager bm;
    SessionReporter rep;
    string player = "", level = "Easy";
    ProgressData data;
    bool running, finished, dirty;
    int effortBefore, bestBefore, serverEffort = -1, serverBest = -1;

    GoalDef[] goals = new GoalDef[0];
    float[] goalBase = new float[0];
    bool[] goalDone = new bool[0], goalDoneNow = new bool[0];
    bool goalsFromServer;
    int goalsCompleted, goalBonusXp;

    int perfectClashes, perfectXp, lastPerfectAttack = -1;
    MonsterAI pendingStagger;
    float staggerGiveUp;
    MonsterAI cachedAi; MonsterController cachedAiOwner;
    MonsterColorCompositor cachedColors;
    CombatFeedback feedback;

    EngagementBanner banner;
    Text hudRank;
    bool[] goalDoneAtEnd = new bool[0];
    int shownRankPoints = -1;

    // ───────────────────────── install ─────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        var b = FindAnyObjectByType<BattleManager>();
        if (b != null && b.GetComponent<EngagementTracker>() == null) b.gameObject.AddComponent<EngagementTracker>();
    }

    void Awake()
    {
        Instance = this;
        cfg = EngagementConfig.Get();
        bm = GetComponent<BattleManager>();
    }

    void OnEnable()
    {
        if (bm != null) bm.OnBattleStarted += Begin;
        HitDetection.HitLanded += OnHit;
        HitDetection.WeaponClashed += OnClash;
        SessionReporter.SessionCompleting += OnSessionCompleting;
    }

    void OnDisable()
    {
        if (bm != null) bm.OnBattleStarted -= Begin;
        HitDetection.HitLanded -= OnHit;
        HitDetection.WeaponClashed -= OnClash;
        SessionReporter.SessionCompleting -= OnSessionCompleting;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        feedback = FindAnyObjectByType<CombatFeedback>();
        // Before the fight: the remembered player's rank on the HUD and their weapon glow.
        var name = PlayerNameStore.LastUsed;
        if (!string.IsNullOrEmpty(name))
        {
            var d = PlayerProgressStore.Load(name);
            GlowCosmetics.ApplyFor(d);
            ShowHudRank(d.effortPoints);
        }
    }

    // ───────────────────────── fight start ─────────────────────────
    void Begin()
    {
        rep = SessionReporter.Instance;
        player = rep != null && !string.IsNullOrEmpty(rep.sessionPlayerName) ? rep.sessionPlayerName : PlayerNameStore.LastUsed;
        level = LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "Easy";
        data = PlayerProgressStore.Load(player);
        data.RollDay(PlayerProgressStore.Today);
        // A rank reached before this headset tracked RANK UP (or on another headset) is not celebrated again later.
        int idx = RankLadder.IndexFor(data.effortPoints);
        if (idx > data.celebratedRank) data.celebratedRank = idx;
        effortBefore = data.effortPoints;
        bestBefore = data.BestOn(level);
        serverEffort = serverBest = -1;
        running = true; finished = false; dirty = false; Result = null;
        perfectClashes = perfectXp = 0; lastPerfectAttack = -1; pendingStagger = null;
        goalsCompleted = goalBonusXp = 0;
        SetGoals(DailyGoalRules.ForDay(data.day), null, false);
        GlowCosmetics.ApplyFor(data);
        if (hudRank != null) hudRank.gameObject.SetActive(true);
        ShowHudRank(effortBefore);
        PlayerProgressStore.Save(player, data);

        if (rep != null && !string.IsNullOrEmpty(player))
        {
            string forPlayer = player, forLevel = level;
            rep.FetchRank(forPlayer, (ok, pts) => { if (this != null && ok && running && forPlayer == player) ApplyServerEffort(pts); });
            rep.FetchLevelBest(forPlayer, forLevel, (ok, best) => { if (this != null && ok && running && forPlayer == player && forLevel == level) serverBest = best; });
            if (cfg.useServerChallenges)
                rep.FetchChallenges(forPlayer, (ok, cards) => { if (this != null && ok && running && forPlayer == player) ApplyServerGoals(cards); });
        }
    }

    void ApplyServerEffort(int pts)
    {
        serverEffort = pts;
        if (pts > effortBefore)
        {
            effortBefore = pts;   // the website saw more fights (another headset, or this one before this store existed)
            int idx = RankLadder.IndexFor(pts);
            if (idx > data.celebratedRank) data.celebratedRank = idx;   // not this fight's rank up
            data.effortPoints = pts;
            PlayerProgressStore.Save(player, data);
            ShowHudRank(effortBefore);
            GlowCosmetics.ApplyFor(data);
        }
    }

    void ApplyServerGoals(SessionReporter.ChallengeCardJson[] cards)
    {
        int n = 0;
        foreach (var c in cards) if (c != null && DailyGoalRules.TryMetric(c.metric, out _)) n++;
        if (n == 0) return;
        var defs = new GoalDef[n]; var baseValues = new float[n];
        int i = 0;
        foreach (var c in cards)
        {
            if (c == null || !DailyGoalRules.TryMetric(c.metric, out var m)) continue;
            defs[i] = new GoalDef
            {
                key = c.key, title = c.title, description = c.description, metric = m, target = c.target, level = string.IsNullOrEmpty(c.level) ? null : c.level,
                maxDamagePercent = c.maxDamagePercent > 0f ? c.maxDamagePercent : 100f, bonusXp = c.bonusXp, tier = c.tier, fromServer = true,
            };
            baseValues[i] = c.value;
            if (c.completed) { var st = data.Goal(c.key, true); st.done = true; }   // completed earlier today: no popup again
            i++;
        }
        SetGoals(defs, baseValues, true);
        dirty = true;
    }

    void SetGoals(GoalDef[] defs, float[] baseValues, bool fromServer)
    {
        goals = defs; goalsFromServer = fromServer;
        goalBase = new float[defs.Length]; goalDone = new bool[defs.Length]; goalDoneNow = new bool[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var st = data.Goal(defs[i].key, !fromServer);
            goalBase[i] = baseValues != null ? baseValues[i] : (st != null ? st.value : 0f);
            goalDone[i] = st != null && st.done;
        }
    }

    // ───────────────────────── during the fight ─────────────────────────
    void OnHit(HitDetection source, float speed, bool defended) { if (running) dirty = true; }

    void LateUpdate()
    {
        if (pendingStagger != null) TryStagger();
        if (!dirty || !running) return;
        dirty = false;
        CheckGoals(false);
    }

    FightTally Tally(bool ended, bool won)
    {
        var xp = XPManager.Instance;
        float maxHp = PlayerHealth.Instance != null ? Mathf.Max(1f, PlayerHealth.Instance.maxHealth) : 1f;
        BattleStats.TryGetKcal(GamePause.ActiveRealtime, out float kcal);
        return new FightTally
        {
            fullSwings = xp != null ? xp.FullSwings : 0,
            bestCombo = xp != null ? xp.BestCombo : 0,
            ended = ended, won = won, level = level,
            damagePercent = BattleStats.DamageTaken / maxHp * 100f,
            activeMinutes = BattleStats.ActiveMinutes,
            kcal = kcal,
        };
    }

    void CheckGoals(bool ended, bool won = false)
    {
        var f = Tally(ended, won);
        for (int i = 0; i < goals.Length; i++)
        {
            if (goalDone[i]) continue;
            float v = DailyGoalRules.Combine(goals[i], goalBase[i], f);
            if (!DailyGoalRules.Met(goals[i], v)) continue;
            goalDone[i] = true; goalDoneNow[i] = true; goalsCompleted++;
            var st = data.Goal(goals[i].key, true); st.done = true;
            if (!goals[i].fromServer && goals[i].bonusXp > 0 && XPManager.Instance != null)
            {
                XPManager.Instance.AddXP(goals[i].bonusXp);   // a local goal's bonus is part of this fight's XP
                goalBonusXp += goals[i].bonusXp;
            }
            PlayerProgressStore.Save(player, data);   // at once: never twice, even if the scene changes now
            if (ended) { goalDoneAtEnd[i] = true; continue; }   // the result screen announces it (the HUD is behind the panel)
            Banner("GOAL COMPLETE  ·  " + goals[i].title + "  ·  +" + goals[i].bonusXp + " XP" + (goals[i].fromServer ? " on your profile" : ""), cfg.goalColor, 2.6f);
        }
    }

    void OnClash(HitDetection source, float speed)
    {
        if (!running || source == null || bm == null || !bm.IsBattleActive) return;
        var m = bm.ActiveMonster;
        if (m == null || !m.isAlive || source.LastClashWeapon == null || !source.LastClashWeapon.IsChildOf(m.transform)) return;
        if (cachedAiOwner != m) { cachedAiOwner = m; cachedAi = m.GetComponent<MonsterAI>(); cachedColors = m.GetComponent<MonsterColorCompositor>(); }
        var ai = cachedAi;
        if (ai == null) return;
        var atk = ai.CurrentAttack;
        bool unblockable = atk != null && atk.unblockable;
        float minSpeed = PlayerCombat.Instance != null ? PlayerCombat.Instance.minSwingVelocity : 1.2f;
        float clashAt = ai.AiTime, hitAt = ai.CurrentHitTime;
        if (!PerfectClashRule.IsPerfect(clashAt, hitAt, cfg.perfectClashWindowSeconds, ai.InStrikeWindow, unblockable, speed, minSpeed)) return;
        if (!PerfectClashRule.FirstForAttack(ai.TableAttackCount, lastPerfectAttack)) return;
        lastPerfectAttack = ai.TableAttackCount;
        perfectClashes++;
        if (XPManager.Instance != null && cfg.perfectClashXp > 0) { XPManager.Instance.AddXP(cfg.perfectClashXp); perfectXp += cfg.perfectClashXp; }
        if (cachedColors != null) cachedColors.NotifyPerfectClash();
        PlayerWeaponGlow.NotifyPerfectClash(source, cfg.perfectClashColor);
        if (feedback != null) feedback.PulseBoth(cfg.hapticAmplitude, cfg.hapticSeconds, cfg.hapticTailAmplitude, cfg.hapticTailSeconds, 0.9f, "perfect clash");
        if (cfg.perfectClashStaggerSeconds > 0f) { pendingStagger = ai; staggerGiveUp = Time.time + 2.5f; }
        Banner("PERFECT CLASH  ·  +" + cfg.perfectClashXp + " XP", cfg.perfectClashColor, 1.6f);
        if (rep != null) rep.ReportPerfectClash(hitAt - clashAt, cfg.perfectClashWindowSeconds, cfg.perfectClashXp, speed, atk != null ? atk.name : "", perfectClashes);
        dirty = true;
    }

    // The stagger waits until the attack that was clashed has fully finished (its recovery included), so it never cuts into a
    // swing that is still resolving; it is dropped if the monster goes straight into something else for 2.5 s.
    void TryStagger()
    {
        var ai = pendingStagger;
        if (ai == null || bm == null || !bm.IsBattleActive || Time.time > staggerGiveUp) { pendingStagger = null; return; }
        var c = ai.GetComponent<MonsterController>();
        if (c == null || !c.isAlive) { pendingStagger = null; return; }
        if (ai.CurrentAttack != null || ai.IsStaggered) return;
        pendingStagger = null;
        ai.BeginStagger(cfg.perfectClashStaggerSeconds, cfg.perfectClashStaggerAnimScale, cfg.perfectClashColor, 0.12f);
    }

    // ───────────────────────── fight end ─────────────────────────
    void OnSessionCompleting(string outcome) => Finish(outcome);

    /// <summary>Works out and saves the fight's result once (later calls return the same result).</summary>
    public FightResult Finish(string outcome)
    {
        if (finished) return Result;
        if (!running) return null;
        finished = true; running = false; pendingStagger = null;
        bool victory = outcome == "victory";
        rep = rep != null ? rep : SessionReporter.Instance;
        int score = bm != null ? bm.CurrentScore : 0;
        var xp = XPManager.Instance;

        goalDoneAtEnd = new bool[goals.Length];
        CheckGoals(true, victory);   // the end-of-fight goals (a win, a clear, one more fight) before the XP is read

        int counted = rep != null ? rep.hitCount : (xp != null ? xp.HitsLanded : 0);
        int full = xp != null ? xp.FullSwings : 0;
        int gained = RankLadder.SessionPoints(counted, full, victory, level == "Hard");
        int after = effortBefore + gained;
        int rankB = RankLadder.IndexFor(effortBefore), rankA = RankLadder.IndexFor(after);
        bool rankUp = RankLadder.IsRankUp(effortBefore, after, data.celebratedRank);
        if (rankA > data.celebratedRank) data.celebratedRank = rankA;
        data.effortPoints = after;

        int prevBest = Mathf.Max(bestBefore, serverBest > 0 ? serverBest : 0);
        bool counts = BestScore.Counts(outcome, score);
        var kind = BestScore.Compare(counts, prevBest, score);
        int bestAfter = BestScore.After(kind, prevBest, score);
        if (bestAfter > data.BestOn(level)) data.SetBest(level, bestAfter);

        var f = Tally(true, victory);
        var values = new float[goals.Length];
        for (int i = 0; i < goals.Length; i++)
        {
            values[i] = DailyGoalRules.Combine(goals[i], goalBase[i], f);
            if (!goals[i].fromServer) { var st = data.Goal(goals[i].key, true); st.value = values[i]; }
        }
        data.RollDay(PlayerProgressStore.Today);
        data.fightsToday++;
        PlayerProgressStore.Save(player, data);

        var r = new FightResult
        {
            outcome = outcome, level = level, levelName = LevelName(level), player = player, victory = victory, counted = counts,
            score = score, previousBest = prevBest, bestAfter = bestAfter, bestKind = kind,
            peakSwing = BattleStats.PeakSwingSpeed, countedSwings = counted, fullSwings = full,
            fullShare = counted > 0 ? Mathf.Clamp01(full / (float)counted) : 0f,
            damageDealt = bm != null ? Mathf.RoundToInt(bm.TotalDamageDealt) : 0,
            xp = xp != null ? xp.CurrentXP : 0, bestCombo = xp != null ? xp.BestCombo : 0,
            effortBefore = effortBefore, effortAfter = after, effortGained = gained, rankBefore = rankB, rankAfter = rankA, rankUp = rankUp,
            goals = goals, goalValues = values, goalDone = (bool[])goalDone.Clone(), goalDoneThisFight = (bool[])goalDoneNow.Clone(),
            goalsFromServer = goalsFromServer, goalsCompletedThisFight = goalsCompleted, goalBonusXp = goalBonusXp,
            perfectClashes = perfectClashes, perfectClashXp = perfectXp, fightsToday = data.fightsToday, goalDoneAtEnd = goalDoneAtEnd,
        };
        r.nextGoal = BestScore.NextGoal(kind, r.levelName, prevBest, score, RankLadder.PointsToNext(after), RankLadder.NextRung(after).name);
        Result = r;
        ShowHudRank(after);
        SendSummary(r);
        return r;
    }

    void SendSummary(FightResult r)
    {
        if (rep == null) return;
        var sb = new StringBuilder(640);
        Str(sb, "fightId", rep.currentSessionId ?? "", true);
        Str(sb, "level", r.level);
        Str(sb, "result", r.outcome);
        Str(sb, "mode", SessionReporter.SessionMode);
        Str(sb, "aiMode", MonsterAIModeSetting.TelemetryName);
        Num(sb, "fightScore", r.score);
        Num(sb, "previousBest", r.previousBest);
        Bool(sb, "newBest", r.bestKind == BestScore.Kind.NewBest);
        Bool(sb, "firstBest", r.bestKind == BestScore.Kind.First);
        Num(sb, "effortBefore", r.effortBefore);
        Num(sb, "effortAfter", r.effortAfter);
        Num(sb, "effortGained", r.effortGained);
        Str(sb, "rankBefore", RankLadder.Rungs[r.rankBefore].name);
        Str(sb, "rankAfter", RankLadder.Rungs[r.rankAfter].name);
        Bool(sb, "rankUp", r.rankUp);
        Str(sb, "goalsSource", r.goalsFromServer ? "server" : "local");
        Num(sb, "goalsCompleted", r.goalsCompletedThisFight);
        int met = 0; for (int i = 0; i < r.goalDone.Length; i++) if (r.goalDone[i]) met++;
        Num(sb, "goalsMetToday", met);
        Num(sb, "goalsTotal", r.goals.Length);
        sb.Append(",\"goalsCompletedKeys\":[");
        bool first = true;
        for (int i = 0; i < r.goals.Length; i++)
        {
            if (!r.goalDoneThisFight[i]) continue;
            if (!first) sb.Append(',');
            sb.Append('"').Append(SessionReporter.Escape(r.goals[i].key)).Append('"');
            first = false;
        }
        sb.Append(']');
        Num(sb, "goalBonusXp", r.goalBonusXp);
        Num(sb, "perfectClashes", r.perfectClashes);
        Num(sb, "perfectClashXp", r.perfectClashXp);
        Num(sb, "perfectClashWindowSec", cfg.perfectClashWindowSeconds);
        Num(sb, "sessionsToday", r.fightsToday);
        Num(sb, "countedSwings", r.countedSwings);
        Num(sb, "fullPowerSwings", r.fullSwings);
        Str(sb, "glowPalette", GlowPalettes.All[GlowCosmetics.Index].key);
        Bool(sb, "serverRankKnown", serverEffort >= 0);
        Str(sb, "headsetDate", System.DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        rep.ReportEngagementSummary(r.player, r.player + " " + r.outcome + " on " + r.levelName + ": engagement summary", sb.ToString());
    }

    static void Str(StringBuilder sb, string k, string v, bool first = false)
    {
        if (!first) sb.Append(',');
        sb.Append('"').Append(k).Append("\":\"").Append(SessionReporter.Escape(v ?? "")).Append('"');
    }
    static void Num(StringBuilder sb, string k, float v) =>
        sb.Append(",\"").Append(k).Append("\":").Append((float.IsNaN(v) || float.IsInfinity(v) ? 0f : v).ToString("0.###", CultureInfo.InvariantCulture));
    static void Bool(StringBuilder sb, string k, bool v) => sb.Append(",\"").Append(k).Append("\":").Append(v ? "true" : "false");

    public static string LevelName(string level) => level == "Medium" ? "Level 2" : level == "Hard" ? "Level 3" : "Level 1";

    // ───────────────────────── HUD ─────────────────────────
    Canvas CombatCanvas()
    {
        var ui = CombatUIController.Instance;
        if (ui == null) return null;
        var src = ui.victoryPanel != null ? ui.victoryPanel.transform : ui.transform;
        return src.GetComponentInParent<Canvas>(true);
    }

    public void Banner(string text, Color color, float seconds)
    {
        if (banner == null)
        {
            var c = CombatCanvas();
            if (c == null) return;
            banner = EngagementBanner.For(c, new Vector2(0f, 292f), new Vector2(700f, 66f));   // above the HP / score row
        }
        if (banner != null) banner.Show(text, color, seconds);
    }

    /// <summary>The result panel covers the HUD: its banner and rank label step aside until the next fight.</summary>
    public void HideHudExtras()
    {
        if (banner != null) banner.Clear();
        if (hudRank != null) hudRank.gameObject.SetActive(false);
    }

    /// <summary>"BRAWLER · 412" at the top left of the combat HUD (written only when the number changes).</summary>
    void ShowHudRank(int points)
    {
        if (points == shownRankPoints && hudRank != null) return;
        if (hudRank == null)
        {
            var c = CombatCanvas();
            if (c == null) return;
            hudRank = MenuUi.Label(c.transform, "HudRank", "", 24, new Color(1f, 0.88f, 0.5f), TextAnchor.MiddleLeft, new Vector2(-250f, 226f), new Vector2(380f, 34f), FontStyle.Bold);
            hudRank.raycastTarget = false;
        }
        shownRankPoints = points;
        hudRank.text = RankLadder.RankFor(points).name.ToUpperInvariant() + "  ·  " + points + " effort";
    }
}
