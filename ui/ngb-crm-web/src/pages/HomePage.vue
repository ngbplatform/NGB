<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import {
  formatDashboardCount,
  formatDashboardMoneyCompact,
  formatDashboardPercent,
  NgbBadge,
  NgbDashboardAsOfToolbar,
  NgbDashboardStatusBanner,
  NgbIcon,
  NgbPageHeader,
  useDashboardPageState,
} from 'ngb-ui-framework'

import { loadHomeDashboard, type CrmHomeDashboardData } from '../home/homeData'

type Tone = 'neutral' | 'success' | 'warn'

const router = useRouter()

const {
  asOf,
  dashboard,
  error,
  loading,
  refresh,
  warnings,
} = useDashboardPageState<CrmHomeDashboardData>({
  load: loadHomeDashboard,
  resolveWarnings: (value) => value?.warnings ?? [],
})

const fallbackRoutes = {
  leads: '/documents/crm.lead_intake',
  pipeline: '/reports/crm.sales_pipeline',
  activities: '/reports/crm.activity_summary',
  quotes: '/reports/crm.quote_register',
  funnel: '/reports/crm.lead_conversion_funnel',
}

const routes = computed(() => dashboard.value?.routes ?? fallbackRoutes)
const monthLabel = computed(() => dashboard.value?.monthLabel ?? '')
const pipelineCoverage = computed(() => {
  const data = dashboard.value
  if (!data || data.pipelineAmount <= 0) return 0
  return (data.weightedPipelineAmount / data.pipelineAmount) * 100
})

const headerSummary = computed(() => {
  const data = dashboard.value
  if (!data) return null
  return `${formatDashboardCount(data.leadCount)} leads · ${formatDashboardCount(data.quoteCount)} quotes · ${formatDashboardCount(data.activityCount)} activities`
})

const quickActions = computed(() => [
  {
    title: 'New Lead',
    subtitle: 'Capture a qualified prospect before the first follow-up.',
    route: '/documents/crm.lead_intake/new',
    icon: 'user',
    tone: 'success' as Tone,
  },
  {
    title: 'Update Opportunity',
    subtitle: 'Move a deal to the next stage and refresh forecast value.',
    route: '/documents/crm.opportunity_update/new',
    icon: 'history',
    tone: 'neutral' as Tone,
  },
  {
    title: 'Prepare Quote',
    subtitle: 'Create a quote with reusable product lines.',
    route: '/documents/crm.quote/new',
    icon: 'file-text',
    tone: 'warn' as Tone,
  },
])

const kpis = computed(() => {
  const data = dashboard.value
  if (!data) return []

  return [
    {
      label: 'Pipeline',
      value: formatDashboardMoneyCompact(data.pipelineAmount),
      context: `${formatDashboardPercent(pipelineCoverage.value)} weighted coverage`,
      route: routes.value.pipeline,
      tone: data.pipelineAmount > 0 ? 'success' as Tone : 'neutral' as Tone,
    },
    {
      label: 'Converted Leads',
      value: formatDashboardCount(data.convertedLeadCount),
      context: `${formatDashboardCount(data.qualifiedLeadCount)} qualified from ${formatDashboardCount(data.leadCount)} leads`,
      route: routes.value.funnel,
      tone: data.convertedLeadCount > 0 ? 'success' as Tone : 'neutral' as Tone,
    },
    {
      label: 'Quote Amount',
      value: formatDashboardMoneyCompact(data.quoteAmount),
      context: `${formatDashboardCount(data.quoteCount)} posted quotes`,
      route: routes.value.quotes,
      tone: data.quoteAmount > 0 ? 'warn' as Tone : 'neutral' as Tone,
    },
    {
      label: 'Activities',
      value: formatDashboardCount(data.activityCount),
      context: `${monthLabel.value || 'Current period'} touchpoints`,
      route: routes.value.activities,
      tone: data.activityCount > 0 ? 'success' as Tone : 'neutral' as Tone,
    },
  ]
})

const topOpportunities = computed(() => dashboard.value?.openOpportunities ?? [])

function openRoute(target: string | null | undefined): void {
  const value = String(target ?? '').trim()
  if (!value) return
  void router.push(value)
}

function toneClass(tone: Tone): string {
  return {
    neutral: 'border-ngb-border',
    success: 'border-emerald-300/70 dark:border-emerald-500/40',
    warn: 'border-amber-300/80 dark:border-amber-500/40',
  }[tone]
}
</script>

<template>
  <div class="flex h-full min-h-0 flex-col" data-testid="crm-home-page">
    <NgbPageHeader title="Dashboard">
      <template #secondary>
        <div class="flex min-w-0 items-center gap-2 overflow-x-auto whitespace-nowrap pb-px">
          <span class="text-sm text-ngb-muted">CRM pipeline workspace</span>
          <NgbBadge v-if="headerSummary" tone="neutral">{{ headerSummary }}</NgbBadge>
          <NgbBadge tone="neutral">As of {{ dashboard?.asOf ?? asOf }}</NgbBadge>
        </div>
      </template>

      <template #actions>
        <NgbDashboardAsOfToolbar v-model="asOf" :loading="loading" @refresh="refresh" />
      </template>
    </NgbPageHeader>

    <div class="flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-[1500px] flex-col gap-5 p-6">
        <NgbDashboardStatusBanner :error="error" :warnings="warnings" error-title="CRM home data failed to load" />

        <section class="grid grid-cols-1 gap-4 lg:grid-cols-3">
          <button
            v-for="action in quickActions"
            :key="action.title"
            type="button"
            class="rounded-[8px] border bg-ngb-card p-4 text-left shadow-card transition hover:-translate-y-0.5 hover:shadow-lg ngb-focus"
            :class="toneClass(action.tone)"
            @click="openRoute(action.route)"
          >
            <div class="flex items-start justify-between gap-4">
              <div class="min-w-0">
                <div class="flex items-center gap-2 text-sm font-semibold text-ngb-text">
                  <NgbIcon :name="action.icon" :size="17" />
                  <span>{{ action.title }}</span>
                </div>
                <p class="mt-2 text-sm leading-6 text-ngb-muted">{{ action.subtitle }}</p>
              </div>
              <NgbIcon name="arrow-right" :size="16" class="text-ngb-muted" />
            </div>
          </button>
        </section>

        <section class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4" data-testid="crm-home-kpis">
          <button
            v-for="card in kpis"
            :key="card.label"
            type="button"
            class="rounded-[8px] border bg-ngb-card p-4 text-left shadow-card transition hover:bg-ngb-bg ngb-focus"
            :class="toneClass(card.tone)"
            @click="openRoute(card.route)"
          >
            <div class="text-[11px] font-semibold uppercase tracking-[0.12em] text-ngb-muted">{{ card.label }}</div>
            <div class="mt-3 text-2xl font-semibold text-ngb-text">{{ card.value }}</div>
            <div class="mt-2 text-sm leading-6 text-ngb-muted">{{ card.context }}</div>
          </button>
        </section>

        <section class="grid grid-cols-1 gap-5 xl:grid-cols-[1.4fr_0.9fr]">
          <div class="rounded-[8px] border border-ngb-border bg-ngb-card shadow-card">
            <div class="flex items-center justify-between gap-3 border-b border-ngb-border px-4 py-3">
              <div>
                <h2 class="text-sm font-semibold text-ngb-text">Weighted Opportunities</h2>
                <p class="mt-1 text-xs text-ngb-muted">Highest forecast contribution first</p>
              </div>
              <button
                type="button"
                class="inline-flex h-8 items-center gap-2 rounded-[var(--ngb-radius)] border border-ngb-border px-3 text-xs font-semibold text-ngb-text hover:bg-ngb-bg ngb-focus"
                @click="openRoute(routes.pipeline)"
              >
                <span>Pipeline</span>
                <NgbIcon name="arrow-right" :size="14" />
              </button>
            </div>

            <div v-if="topOpportunities.length" class="divide-y divide-ngb-border">
              <button
                v-for="item in topOpportunities"
                :key="`${item.opportunity}:${item.account}:${item.stage}`"
                type="button"
                class="grid w-full grid-cols-[1fr_auto] gap-4 px-4 py-3 text-left hover:bg-ngb-bg ngb-focus"
                @click="openRoute(item.route ?? routes.pipeline)"
              >
                <div class="min-w-0">
                  <div class="truncate text-sm font-semibold text-ngb-text">{{ item.opportunity }}</div>
                  <div class="mt-1 truncate text-xs text-ngb-muted">{{ item.account }} · {{ item.stage }}</div>
                </div>
                <div class="text-right">
                  <div class="text-sm font-semibold text-ngb-text">{{ formatDashboardMoneyCompact(item.weightedAmount) }}</div>
                  <div class="mt-1 text-xs text-ngb-muted">{{ formatDashboardMoneyCompact(item.amount) }}</div>
                </div>
              </button>
            </div>

            <div v-else class="px-4 py-10 text-center text-sm text-ngb-muted">
              No posted opportunities yet.
            </div>
          </div>

          <div class="rounded-[8px] border border-ngb-border bg-ngb-card p-4 shadow-card">
            <h2 class="text-sm font-semibold text-ngb-text">Navigation</h2>
            <div class="mt-4 grid gap-2">
              <button class="crm-nav-link" type="button" @click="openRoute(routes.leads)">
                <span>Leads</span>
                <NgbIcon name="arrow-right" :size="14" />
              </button>
              <button class="crm-nav-link" type="button" @click="openRoute('/catalogs/crm.account')">
                <span>Accounts</span>
                <NgbIcon name="arrow-right" :size="14" />
              </button>
              <button class="crm-nav-link" type="button" @click="openRoute('/catalogs/crm.contact')">
                <span>Contacts</span>
                <NgbIcon name="arrow-right" :size="14" />
              </button>
              <button class="crm-nav-link" type="button" @click="openRoute(routes.quotes)">
                <span>Quotes</span>
                <NgbIcon name="arrow-right" :size="14" />
              </button>
            </div>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>

<style scoped>
.crm-nav-link {
  display: inline-flex;
  height: 2.5rem;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  border-radius: var(--ngb-radius);
  border: 1px solid rgb(var(--ngb-border));
  padding: 0 0.75rem;
  color: rgb(var(--ngb-text));
  font-size: 0.875rem;
}

.crm-nav-link:hover {
  background: rgb(var(--ngb-bg));
}
</style>
