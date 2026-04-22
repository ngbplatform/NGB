<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import {
  buildAccountingPeriodClosingPath,
  formatDashboardCount,
  formatDashboardMoneyCompact,
  formatDashboardPercent,
  NgbBadge,
  NgbDashboardAsOfToolbar,
  NgbDashboardStatusBanner,
  NgbIcon,
  NgbPageHeader,
  NgbTrendChart,
  useDashboardPageState,
} from 'ngb-ui-framework'

import { loadHomeDashboard, type HomeDashboardData } from '../home/homeData'

type Tone = 'neutral' | 'warn' | 'danger' | 'success'

const router = useRouter()

const {
  asOf,
  dashboard,
  error,
  loading,
  refresh,
  warnings,
} = useDashboardPageState<HomeDashboardData>({
  load: loadHomeDashboard,
  resolveWarnings: (value) => value?.warnings ?? [],
})

const monthLabel = computed(() => dashboard.value?.monthLabel ?? '')
const headerSummary = computed(() => {
  const data = dashboard.value
  if (!data) return null
  return `${data.portfolio.buildingCount} buildings · ${data.portfolio.totalUnits} units · ${data.portfolio.occupiedUnits} occupied`
})
const asOfBadge = computed(() => `As of ${dashboard.value?.asOf ?? asOf.value}`)

function openRoute(target: string | null | undefined) {
  if (!target) return
  void router.push(target)
}

const collectionsRate = computed(() => {
  const data = dashboard.value
  if (!data) return 0
  const billed = data.receivables.currentMonthBilled
  if (billed <= 0) return 0
  return (data.receivables.currentMonthCollected / billed) * 100
})

const currentYear = computed(() => Number.parseInt(asOf.value.slice(0, 4), 10))
const reconciliationBaseRoute = computed(() => {
  const data = dashboard.value
  if (!data) return '/receivables/reconciliation'
  return `/receivables/reconciliation?fromMonth=${encodeURIComponent(data.monthKey)}&toMonth=${encodeURIComponent(data.monthKey)}&mode=Balance`
})

function toneLabel(tone: Tone): string {
  return {
    neutral: 'FYI',
    success: 'Stable',
    warn: 'Watch',
    danger: 'Urgent',
  }[tone]
}

const attentionCards = computed(() => {
  const data = dashboard.value
  if (!data) return []

  const openArTone: Tone = data.receivables.totalOpenItemsNet > 0 ? 'danger' : 'success'
  const mismatchTone: Tone = data.receivables.mismatchRowCount > 0 ? 'danger' : 'success'
  const expirationTone: Tone = data.leases.expiring30Count > 0 ? 'warn' : 'success'
  const vacancyTone: Tone = data.portfolio.vacantUnits > 0 ? 'warn' : 'success'
  const maintenanceTone: Tone = data.maintenance.overdueCount > 0 ? 'danger' : 'success'
  const closeTone: Tone = data.periods.pendingCloseCount > 0 ? 'warn' : 'success'

  return [
    {
      title: 'Open receivables',
      value: formatDashboardMoneyCompact(data.receivables.totalOpenItemsNet),
      meta: `${formatDashboardCount(data.receivables.rowCount)} active lease balances`,
      description: `Balance-mode open items for ${data.monthLabel}.`,
      tone: openArTone,
      route: reconciliationBaseRoute.value,
    },
    {
      title: 'Reconciliation mismatches',
      value: formatDashboardCount(data.receivables.mismatchRowCount),
      meta: `Net diff ${formatDashboardMoneyCompact(Math.abs(data.receivables.totalDiff))}`,
      description: 'AR and open items rows that still require attention.',
      tone: mismatchTone,
      route: `${reconciliationBaseRoute.value}&status=mismatch`,
    },
    {
      title: 'Lease expirations in 30 days',
      value: formatDashboardCount(data.leases.expiring30Count),
      meta: `${formatDashboardCount(data.leases.upcomingMoveOutCount)} move-outs in the next 14 days`,
      description: 'Renewals and turn planning that need to be worked now.',
      tone: expirationTone,
      route: '/documents/pm.lease',
    },
    {
      title: 'Vacant units and turns',
      value: formatDashboardCount(data.portfolio.vacantUnits),
      meta: `${formatDashboardCount(data.leases.upcomingMoveInCount)} move-ins scheduled`,
      description: 'Portfolio vacancy posture and near-term turn pressure.',
      tone: vacancyTone,
      route: data.charts.occupancy.route,
    },
    {
      title: 'Overdue maintenance',
      value: formatDashboardCount(data.maintenance.overdueCount),
      meta: `${formatDashboardCount(data.maintenance.openItemCount)} total open queue items`,
      description: 'Open work that is already past the expected due date.',
      tone: maintenanceTone,
      route: data.charts.maintenanceAging.route,
    },
    {
      title: 'Open periods not closed',
      value: formatDashboardCount(data.periods.pendingCloseCount),
      meta: data.periods.lastClosedPeriod ? `Last closed: ${data.periods.lastClosedPeriod}` : 'No closed month yet',
      description: 'Accounting close gaps that can block clean reporting.',
      tone: closeTone,
      route: buildAccountingPeriodClosingPath({ year: currentYear.value }),
    },
  ]
})

const kpis = computed(() => {
  const data = dashboard.value
  if (!data) return []

  return [
    {
      label: 'Occupancy',
      value: formatDashboardPercent(data.portfolio.occupancyPercent),
      context: `${formatDashboardCount(data.portfolio.occupiedUnits)} of ${formatDashboardCount(data.portfolio.totalUnits)} units occupied`,
    },
    {
      label: 'Future occupancy',
      value: formatDashboardPercent(data.portfolio.futureOccupancyPercent),
      context: `${formatDashboardCount(data.portfolio.futureOccupiedUnits)} units projected in 30 days`,
    },
    {
      label: 'Open AR',
      value: formatDashboardMoneyCompact(data.receivables.totalOpenItemsNet),
      context: `${data.monthLabel} balance-mode snapshot`,
    },
    {
      label: 'Collected vs billed',
      value: formatDashboardPercent(collectionsRate.value),
      context: `${monthLabel.value} cash receipts against billed charges`,
    },
    {
      label: 'Vacant units',
      value: formatDashboardCount(data.portfolio.vacantUnits),
      context: `${formatDashboardCount(data.leases.expiring30Count)} leases expire in the next 30 days`,
    },
    {
      label: 'Overdue work orders',
      value: formatDashboardCount(data.maintenance.overdueCount),
      context: `${formatDashboardCount(data.maintenance.openItemCount)} open maintenance items`,
    },
  ]
})

function actionCardClass(tone: Tone): string {
  return {
    neutral: 'home-tone-neutral',
    success: 'home-tone-success',
    warn: 'home-tone-warn',
    danger: 'home-tone-danger',
  }[tone]
}

function snapshotBadgeTone(kind: string): Tone {
  const normalized = kind.trim().toLowerCase()
  if (normalized.includes('overdue') || normalized.includes('mismatch') || normalized.includes('glonly') || normalized.includes('openitemsonly')) return 'danger'
  if (normalized.includes('requested') || normalized.includes('move-out')) return 'warn'
  if (normalized.includes('move-in') || normalized.includes('matched')) return 'success'
  return 'neutral'
}
</script>

<template>
  <div data-testid="home-page" class="flex h-full min-h-0 flex-col">
    <NgbPageHeader title="Home">
      <template #secondary>
        <div class="flex min-w-0 items-center gap-2 overflow-x-auto whitespace-nowrap pb-px">
          <span class="text-sm text-ngb-muted">Operations overview for your portfolio</span>
          <NgbBadge v-if="headerSummary" tone="neutral">{{ headerSummary }}</NgbBadge>
          <NgbBadge tone="neutral">{{ asOfBadge }}</NgbBadge>
        </div>
      </template>

      <template #actions>
        <NgbDashboardAsOfToolbar v-model="asOf" :loading="loading" @refresh="refresh" />
      </template>
    </NgbPageHeader>

    <div class="flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-[1680px] flex-col gap-5 p-6">
        <NgbDashboardStatusBanner :error="error" :warnings="warnings" error-title="Home data failed to load" />

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">Requires Attention</div>
              <h2 class="home-section-title">What needs action today</h2>
            </div>
            <div class="text-sm text-ngb-muted">
              {{ loading ? 'Refreshing live portfolio signals…' : `Operational focus for ${monthLabel || 'the selected period'}` }}
            </div>
          </div>

          <div data-testid="home-attention-grid" class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            <button
              v-for="card in attentionCards"
              :key="card.title"
              type="button"
              class="home-panel home-attention-card"
              :class="actionCardClass(card.tone)"
              @click="openRoute(card.route)"
            >
              <div class="flex items-start justify-between gap-4">
                <div class="min-w-0">
                  <div class="home-tone-pill">{{ toneLabel(card.tone) }}</div>
                  <div class="mt-3 text-sm font-semibold text-ngb-text">{{ card.title }}</div>
                  <div class="mt-3 text-[1.9rem] font-semibold tracking-[-0.03em] text-ngb-text">{{ card.value }}</div>
                </div>

                <span class="home-action-arrow" aria-hidden="true">
                  <NgbIcon name="arrow-right" :size="16" />
                </span>
              </div>

              <div class="mt-3 text-xs font-semibold uppercase tracking-[0.12em] text-ngb-muted">{{ card.meta }}</div>
              <div class="mt-2 text-sm leading-6 text-ngb-muted">{{ card.description }}</div>
            </button>
          </div>
        </section>

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">KPI Strip</div>
              <h2 class="home-section-title">Portfolio health at a glance</h2>
            </div>
            <div class="text-sm text-ngb-muted">Compact, role-ready indicators instead of a BI wall.</div>
          </div>

          <div data-testid="home-kpi-grid" class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-6">
            <div v-for="kpi in kpis" :key="kpi.label" class="home-panel home-kpi-card">
              <div class="text-[11px] font-semibold uppercase tracking-[0.12em] text-ngb-muted">{{ kpi.label }}</div>
              <div class="mt-3 text-[1.65rem] font-semibold tracking-[-0.03em] text-ngb-text">{{ kpi.value }}</div>
              <div class="mt-2 text-sm leading-6 text-ngb-muted">{{ kpi.context }}</div>
            </div>
          </div>
        </section>

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">Trends</div>
              <h2 class="home-section-title">Signals that are worth watching</h2>
            </div>
            <div class="text-sm text-ngb-muted">Each chart should lead to a workflow, not just decorate the page.</div>
          </div>

          <div v-if="dashboard" class="grid grid-cols-1 gap-4 xl:grid-cols-2">
            <button type="button" class="home-panel home-chart-card" @click="openRoute(dashboard.charts.collections.route)">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">{{ dashboard.charts.collections.title }}</h3>
                  <p class="mt-1 text-sm text-ngb-muted">{{ dashboard.charts.collections.subtitle }}</p>
                </div>
                <span class="home-chart-tag">Receivables</span>
              </div>

              <div class="home-chart-frame">
                <NgbTrendChart
                  :labels="dashboard.charts.collections.labels"
                  :series="dashboard.charts.collections.series"
                  mode="line"
                />
              </div>
            </button>

            <button type="button" class="home-panel home-chart-card" @click="openRoute(dashboard.charts.occupancy.route)">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">{{ dashboard.charts.occupancy.title }}</h3>
                  <p class="mt-1 text-sm text-ngb-muted">{{ dashboard.charts.occupancy.subtitle }}</p>
                </div>
                <span class="home-chart-tag">Portfolio</span>
              </div>

              <div class="home-chart-frame">
                <NgbTrendChart
                  :labels="dashboard.charts.occupancy.labels"
                  :series="dashboard.charts.occupancy.series"
                  mode="line"
                />
              </div>
            </button>

            <button type="button" class="home-panel home-chart-card xl:col-span-2" @click="openRoute(dashboard.charts.maintenanceAging.route)">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">{{ dashboard.charts.maintenanceAging.title }}</h3>
                  <p class="mt-1 text-sm text-ngb-muted">{{ dashboard.charts.maintenanceAging.subtitle }}</p>
                </div>
                <span class="home-chart-tag">Maintenance</span>
              </div>

              <div class="home-chart-frame home-chart-frame-wide">
                <NgbTrendChart
                  :labels="dashboard.charts.maintenanceAging.labels"
                  :series="dashboard.charts.maintenanceAging.series"
                  mode="bar"
                />
              </div>
            </button>
          </div>
        </section>

        <section v-if="dashboard" class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">Operational Snapshots</div>
              <h2 class="home-section-title">Live queues, not just aggregates</h2>
            </div>
            <div class="text-sm text-ngb-muted">Short lists that help you jump straight into work.</div>
          </div>

          <div class="grid grid-cols-1 gap-4 xl:grid-cols-3">
            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Upcoming move-ins / move-outs</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Next 14 days of lease activity</p>
                </div>
                <NgbBadge tone="neutral">{{ dashboard.leases.events.length }}</NgbBadge>
              </div>

              <div v-if="dashboard.leases.events.length" class="mt-5 space-y-3">
                <button
                  v-for="event in dashboard.leases.events"
                  :key="`${event.kind}:${event.route}`"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(event.route)"
                >
                  <div class="flex min-w-0 items-center gap-2">
                    <NgbBadge :tone="snapshotBadgeTone(event.kind)">{{ event.kind }}</NgbBadge>
                    <span class="truncate text-sm font-medium text-ngb-text">{{ event.leaseDisplay }}</span>
                  </div>
                  <div class="mt-2 flex items-center justify-between gap-3 text-sm text-ngb-muted">
                    <span class="min-w-0 truncate">{{ event.propertyDisplay }}</span>
                    <span class="shrink-0">{{ event.date }}</span>
                  </div>
                </button>
              </div>
              <div v-else class="mt-5 text-sm text-ngb-muted">No move-ins or move-outs are scheduled in the next 14 days.</div>
            </div>

            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Maintenance queue snapshot</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Open items sorted by urgency and aging</p>
                </div>
                <NgbBadge tone="neutral">{{ dashboard.maintenance.openItemCount }}</NgbBadge>
              </div>

              <div v-if="dashboard.maintenance.items.length" class="mt-5 space-y-3">
                <button
                  v-for="item in dashboard.maintenance.items"
                  :key="`${item.requestDisplay}:${item.subject}`"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(item.route)"
                >
                  <div class="flex min-w-0 items-center gap-2">
                    <NgbBadge :tone="snapshotBadgeTone(item.queueState)">{{ item.queueState }}</NgbBadge>
                    <span class="truncate text-sm font-medium text-ngb-text">{{ item.subject }}</span>
                  </div>
                  <div class="mt-2 flex items-center justify-between gap-3 text-sm text-ngb-muted">
                    <span class="min-w-0 truncate">{{ item.propertyDisplay }}</span>
                    <span class="shrink-0">{{ item.dueBy || item.requestedAt || 'No date' }}</span>
                  </div>
                </button>
              </div>
              <div v-else class="mt-5 text-sm text-ngb-muted">No open maintenance items right now.</div>
            </div>

            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Receivables exceptions</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Largest open mismatches in the current month</p>
                </div>
                <NgbBadge tone="neutral">{{ dashboard.receivables.mismatchRowCount }}</NgbBadge>
              </div>

              <div v-if="dashboard.receivables.mismatches.length" class="mt-5 space-y-3">
                <button
                  v-for="row in dashboard.receivables.mismatches"
                  :key="`${row.leaseDisplay}:${row.route}`"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(row.route)"
                >
                  <div class="flex min-w-0 items-center gap-2">
                    <NgbBadge :tone="snapshotBadgeTone(row.rowKind)">{{ row.rowKind }}</NgbBadge>
                    <span class="truncate text-sm font-medium text-ngb-text">{{ row.leaseDisplay }}</span>
                  </div>
                  <div class="mt-2 flex items-center justify-between gap-3 text-sm text-ngb-muted">
                    <span class="min-w-0 truncate">{{ row.propertyDisplay }}</span>
                    <span class="shrink-0">{{ formatDashboardMoneyCompact(Math.abs(row.diff)) }}</span>
                  </div>
                </button>
              </div>
              <div v-else class="mt-5 text-sm text-ngb-muted">Receivables are currently aligned for the selected month.</div>
            </div>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>

<style scoped>
.home-section-label {
  color: var(--ngb-accent-1);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

.home-section-title {
  margin-top: 0.35rem;
  font-size: 1.5rem;
  font-weight: 700;
  letter-spacing: -0.03em;
  color: var(--ngb-text);
}

.home-panel {
  border: 1px solid var(--ngb-border);
  border-radius: var(--ngb-radius);
  background: var(--ngb-card);
  box-shadow: 0 10px 28px rgba(15, 23, 42, 0.05);
}

.home-tone-neutral {
  --home-tone: var(--ngb-muted);
}

.home-tone-success {
  --home-tone: var(--ngb-success);
}

.home-tone-warn {
  --home-tone: var(--ngb-warn);
}

.home-tone-danger {
  --home-tone: var(--ngb-danger);
}

.home-attention-card,
.home-chart-card,
.home-list-item {
  transition: transform 140ms ease, box-shadow 140ms ease, border-color 140ms ease;
}

.home-attention-card,
.home-chart-card,
.home-snapshot-card {
  text-align: left;
}

.home-chart-card {
  --home-tone: var(--ngb-accent-1);
}

.home-attention-card {
  position: relative;
  overflow: hidden;
  padding: 1rem 1rem 1.1rem;
}

.home-attention-card::before {
  content: '';
  position: absolute;
  inset: 0 auto 0 0;
  width: 4px;
  background: var(--home-tone, var(--ngb-border));
}

.home-attention-card:hover,
.home-chart-card:hover,
.home-list-item:hover {
  transform: translateY(-1px);
  border-color: color-mix(in srgb, var(--home-tone, var(--ngb-border)) 26%, var(--ngb-border));
  box-shadow: 0 16px 34px rgba(15, 23, 42, 0.08);
}

.home-tone-pill {
  display: inline-flex;
  align-items: center;
  border: 1px solid color-mix(in srgb, var(--home-tone, var(--ngb-border)) 30%, var(--ngb-border));
  border-radius: 999px;
  background: color-mix(in srgb, var(--home-tone, var(--ngb-border)) 10%, var(--ngb-card));
  color: var(--home-tone, var(--ngb-text));
  font-size: 0.68rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  padding: 0.25rem 0.55rem;
  text-transform: uppercase;
  white-space: nowrap;
}

.home-action-arrow {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  height: 2rem;
  width: 2rem;
  border-radius: 999px;
  border: 1px solid color-mix(in srgb, var(--home-tone, var(--ngb-border)) 24%, var(--ngb-border));
  background: color-mix(in srgb, var(--home-tone, var(--ngb-border)) 8%, var(--ngb-card));
  color: var(--home-tone, var(--ngb-text));
  flex-shrink: 0;
}

.home-kpi-card,
.home-chart-card,
.home-snapshot-card {
  padding: 1rem;
}

.home-chart-frame {
  height: 280px;
  margin-top: 1rem;
  border: 1px solid color-mix(in srgb, var(--ngb-border) 85%, transparent);
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-bg) 72%, var(--ngb-card));
  padding: 0.35rem 0.5rem;
}

.home-chart-frame-wide {
  height: 300px;
}

.home-chart-tag {
  display: inline-flex;
  align-items: center;
  border: 1px solid color-mix(in srgb, var(--ngb-accent-1) 28%, var(--ngb-border));
  border-radius: 999px;
  background: color-mix(in srgb, var(--ngb-accent-1) 8%, var(--ngb-card));
  color: var(--ngb-accent-1);
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  padding: 0.3rem 0.65rem;
  text-transform: uppercase;
  white-space: nowrap;
}

.home-list-item {
  width: 100%;
  border: 1px solid var(--ngb-border);
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-card) 92%, var(--ngb-bg));
  padding: 0.8rem 0.9rem;
  text-align: left;
}

:global(html.dark) .home-panel {
  box-shadow: 0 16px 36px rgba(0, 0, 0, 0.3);
}

:global(html.dark) .home-attention-card:hover,
:global(html.dark) .home-chart-card:hover,
:global(html.dark) .home-list-item:hover {
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.34);
}

@media (max-width: 768px) {
  .home-section-title {
    font-size: 1.28rem;
  }

  .home-chart-frame,
  .home-chart-frame-wide {
    height: 240px;
  }
}
</style>
