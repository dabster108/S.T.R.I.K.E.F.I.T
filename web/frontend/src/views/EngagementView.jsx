import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "../lib/api.js";
import { shortDay } from "../lib/fitness.js";
import { formatNumber } from "../lib/format.js";
import { useApi } from "../hooks/useApi.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader, SectionRule } from "../components/ui/Card.jsx";
import { Select } from "../components/ui/Select.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { GenericSkeleton } from "../components/ui/Skeletons.jsx";
import { FigureStrip } from "../components/ui/StatTile.jsx";
import Icon from "../components/ui/Icon.jsx";
import { BarChart } from "../components/charts/Charts.jsx";
import PlayerEngagement from "../components/fitness/Engagement.jsx";
import { SiteCalendar } from "../components/engage/Calendar.jsx";

function SiteSection({ site }) {
  const days = site.sessionsPerDay;
  const total = days.reduce((a, d) => a + d.sessions, 0);
  const single = site.totalPlayers === 1;
  const returnValue = site.totalPlayers < 2 ? "—" : `${site.returnRatePercent}%`;
  const returnSub =
    site.totalPlayers < 2 ? "needs at least 2 players" : `${site.returningPlayers} of ${site.totalPlayers} played more than once`;
  return (
    <div className="space-y-6">
      <FigureStrip
        items={[
          { label: "Sessions, last 30 days", value: formatNumber(total), sub: single ? "from the one player so far" : `across ${formatNumber(site.activePlayersThisMonth)} active players`, lead: true },
          { label: "Active this week", value: formatNumber(site.activePlayersThisWeek), sub: `of ${formatNumber(site.totalPlayers)} player${single ? "" : "s"} · last 7 days` },
          { label: "Active this month", value: formatNumber(site.activePlayersThisMonth), sub: "last 30 days" },
          { label: "Return rate", value: returnValue, sub: returnSub },
        ]}
        caption={single ? "Only one player has played so far, so these figures describe that player alone." : site.definitions.returnRate}
      />
      <Card className="overflow-hidden">
        <CardHeader title="Sessions per day, all players" subtitle="Last 30 days" />
        <div className="px-3 pb-4 pt-4 sm:px-5">
          {total ? (
            <BarChart
              caption="Sessions per day across all players, last 30 days"
              categories={days.map((d) => shortDay(d.day))}
              series={[{ key: "n", label: "Sessions", color: "var(--accent)", values: days.map((d) => d.sessions) }]}
              yFormat={(v) => formatNumber(v)}
            />
          ) : (
            <EmptyState icon="calendar" title="No sessions in the last 30 days" />
          )}
        </div>
      </Card>
    </div>
  );
}

function PlayerSection({ name }) {
  const { data, error, loading } = useApi(() => api.playerEngagement(name), [name]);
  const mine = data && data.playerName === name ? data : null;
  if (error && !mine) {
    return (
      <Card>
        <EmptyState icon="players" title={error.status === 404 ? `No sessions for ${name} yet` : "Engagement will appear here"} description={error.status === 404 ? "Names must match exactly, including capitals." : "It loads as soon as the server answers."} />
      </Card>
    );
  }
  if (!mine) return loading ? <GenericSkeleton /> : null;
  return <PlayerEngagement data={mine} />;
}

export default function EngagementView() {
  const [params, setParams] = useSearchParams();
  const site = useApi(() => api.overview(), []);
  const players = useApi(() => api.players(), []);
  useLiveMessages((type) => {
    if (type === "player_renamed") players.reload();
  });
  const names = useMemo(() => (players.data || []).map((p) => p.playerName).sort((a, b) => a.localeCompare(b)), [players.data]);
  // With no choice made, open the player with the most sessions (the only player, when there is one).
  const busiest = useMemo(() => [...(players.data || [])].sort((a, b) => (b.totalSessions ?? b.sessions ?? 0) - (a.totalSessions ?? a.sessions ?? 0))[0]?.playerName || "", [players.data]);
  const chosen = params.get("player") || (names.length === 1 ? names[0] : busiest);
  const options = names.map((n) => ({ id: n, label: n }));
  const setPlayer = (v) => setParams(v ? { player: v } : {}, { replace: true });

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Research"
        title="Engagement"
        description="Whether people come back: sessions over time for everyone, then the same for one player. Days follow your browser's time zone."
      />

      <SectionRule index="01" title="All players" />
      <SiteCalendar />
      {site.data?.engagement ? (
        <SiteSection site={site.data.engagement} />
      ) : site.error ? (
        <Card>
          <EmptyState icon="calendar" title="Engagement will appear here" description="It loads as soon as the server answers." />
        </Card>
      ) : (
        <GenericSkeleton />
      )}

      <SectionRule
        index="02"
        title="One player"
        aside={options.length ? <Select label="Player" value={chosen} onChange={setPlayer} options={options} /> : null}
      />
      {chosen ? (
        <PlayerSection key={chosen} name={chosen} />
      ) : (
        <Card>
          <EmptyState icon="players" title={players.loading ? "Loading players…" : "No players yet"} description="Pick a player once someone has finished a session." />
        </Card>
      )}

      <p className="flex items-start gap-2 text-xs font-medium text-[var(--muted)]">
        <Icon name="info" className="mt-px h-3.5 w-3.5 shrink-0" />
        Counts every recorded session, including abandoned ones, the same way the rest of the dashboard does.
      </p>
    </div>
  );
}
