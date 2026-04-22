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
  NgbTrendChart,
  useDashboardPageState,
} from 'ngb-ui-framework'

import { loadHomeDashboard, type TradeHomeDashboardData } from '../home/homeData'

type Tone = 'neutral' | 'success' | 'warn' | 'danger'

const router = useRouter()

const {
  asOf,
  dashboard,
  error,
  loading,
  refresh,
  warnings,
} = useDashboardPageState<TradeHomeDashboardData>({
  load: loadHomeDashboard,
  resolveWarnings: (value) => value?.warnings ?? [],
})

const fallbackRoutes = {
  sales: '/reports/trd.sales_by_customer',
  purchases: '/reports/trd.purchases_by_vendor',
  inventory: '/reports/trd.inventory_balances',
  grossMargin: '/reports/trd.sales_by_item',
  currentPrices: '/reports/trd.current_item_prices',
  salesByItem: '/reports/trd.sales_by_item',
  salesByCustomer: '/reports/trd.sales_by_customer',
  purchasesByVendor: '/reports/trd.purchases_by_vendor',
}

const monthLabel = computed(() => dashboard.value?.monthLabel ?? '')
const headerSummary = computed(() => {
  const data = dashboard.value
  if (!data) return null
  return `${formatDashboardCount(data.activeSalesItemCount)} selling items · ${formatDashboardCount(data.activeCustomerCount)} active customers · ${formatDashboardCount(data.activeVendorCount)} active vendors`
})
const asOfBadge = computed(() => `As of ${dashboard.value?.asOf ?? asOf.value}`)
const routes = computed(() => dashboard.value?.routes ?? fallbackRoutes)
const topItems = computed(() => dashboard.value?.topItems ?? [])
const topCustomers = computed(() => dashboard.value?.topCustomers ?? [])
const topVendors = computed(() => dashboard.value?.topVendors ?? [])
const inventoryPositions = computed(() => dashboard.value?.inventoryPositions ?? [])
const recentDocuments = computed(() => dashboard.value?.recentDocuments ?? [])
const salesMixChart = computed(() => dashboard.value?.charts.salesMix ?? null)
const inventoryFootprintChart = computed(() => dashboard.value?.charts.inventoryFootprint ?? null)

const grossMarginPercent = computed(() => {
  const data = dashboard.value
  if (!data || data.salesThisMonth <= 0) return 0
  return (data.grossMargin / data.salesThisMonth) * 100
})

const quickActions = computed(() => [
  {
    title: 'New Sales Invoice',
    subtitle: 'Capture outbound revenue and start the sales document flow.',
    route: '/documents/trd.sales_invoice/new',
    tone: 'success' as Tone,
  },
  {
    title: 'Receive Stock',
    subtitle: 'Post an inbound purchase receipt and refresh inventory positions.',
    route: '/documents/trd.purchase_receipt/new',
    tone: 'neutral' as Tone,
  },
  {
    title: 'Review Price Book',
    subtitle: 'Open the current item prices report and validate active sell-side pricing.',
    route: routes.value.currentPrices,
    tone: 'warn' as Tone,
  },
])

const kpis = computed(() => {
  const data = dashboard.value
  if (!data) return []

  return [
    {
      label: 'Sales This Month',
      value: formatDashboardMoneyCompact(data.salesThisMonth),
      context: `${monthLabel.value} net invoiced after returns`,
      tone: marginTone(data.grossMargin, grossMarginPercent.value),
      route: routes.value.sales,
    },
    {
      label: 'Purchases This Month',
      value: formatDashboardMoneyCompact(data.purchasesThisMonth),
      context: `${monthLabel.value} net receipts after returns`,
      tone: data.purchasesThisMonth > 0 ? 'neutral' as Tone : 'warn' as Tone,
      route: routes.value.purchases,
    },
    {
      label: 'Inventory On Hand',
      value: formatQuantity(data.inventoryOnHand),
      context: `${formatDashboardCount(data.inventoryPositionCount)} active item / warehouse positions`,
      tone: data.inventoryOnHand > 0 ? 'success' as Tone : 'warn' as Tone,
      route: routes.value.inventory,
    },
    {
      label: 'Gross Margin',
      value: formatDashboardMoneyCompact(data.grossMargin),
      context: `${formatDashboardPercent(grossMarginPercent.value)} of net sales`,
      tone: marginTone(data.grossMargin, grossMarginPercent.value),
      route: routes.value.grossMargin,
    },
  ]
})

function openRoute(target: string | null | undefined): void {
  const value = String(target ?? '').trim()
  if (!value) return
  void router.push(value)
}

function formatQuantity(value: number): string {
  const numeric = Number(value ?? 0)
  if (!Number.isFinite(numeric)) return '0'
  return numeric.toLocaleString(undefined, {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  })
}

function marginTone(marginValue: number, marginPercentValue: number): Tone {
  if (marginValue < 0) return 'danger'
  if (marginPercentValue >= 25) return 'success'
  if (marginPercentValue > 0) return 'warn'
  return 'neutral'
}

function recentDocumentTone(notes: string): Tone {
  const normalized = notes.trim().toLowerCase()
  if (normalized.includes('posted')) return 'success'
  if (normalized.includes('draft')) return 'warn'
  return 'neutral'
}

function toneLabel(tone: Tone): string {
  return {
    neutral: 'Ready',
    success: 'Priority',
    warn: 'Review',
    danger: 'Risk',
  }[tone]
}

function actionCardClass(tone: Tone): string {
  return {
    neutral: 'home-tone-neutral',
    success: 'home-tone-success',
    warn: 'home-tone-warn',
    danger: 'home-tone-danger',
  }[tone]
}
</script>

<template>
  <div class="flex h-full min-h-0 flex-col" data-testid="trade-home-page">
    <NgbPageHeader title="Home">
      <template #secondary>
        <div class="flex min-w-0 items-center gap-2 overflow-x-auto whitespace-nowrap pb-px">
          <span class="text-sm text-ngb-muted">Trading pulse and inventory control</span>
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
        <NgbDashboardStatusBanner :error="error" :warnings="warnings" error-title="Trade home data failed to load" />

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">Action Center</div>
              <h2 class="home-section-title">Start from the workflow that matters right now</h2>
            </div>
            <div class="text-sm text-ngb-muted">
              {{ loading ? 'Refreshing trade workspace…' : `Operational focus for ${monthLabel || 'the selected period'}` }}
            </div>
          </div>

          <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            <button
              v-for="action in quickActions"
              :key="action.title"
              type="button"
              class="home-panel home-attention-card"
              :class="actionCardClass(action.tone)"
              @click="openRoute(action.route)"
            >
              <div class="flex items-start justify-between gap-4">
                <div class="min-w-0">
                  <div class="home-tone-pill">{{ toneLabel(action.tone) }}</div>
                  <div class="mt-3 text-sm font-semibold text-ngb-text">{{ action.title }}</div>
                  <div class="mt-2 text-sm leading-6 text-ngb-muted">{{ action.subtitle }}</div>
                </div>

                <span class="home-action-arrow" aria-hidden="true">
                  <NgbIcon name="arrow-right" :size="16" />
                </span>
              </div>
            </button>
          </div>
        </section>

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">KPI Strip</div>
              <h2 class="home-section-title">Performance and inventory in one compact view</h2>
            </div>
            <div class="text-sm text-ngb-muted">Small cards, standard radii, no decorative chrome.</div>
          </div>

          <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4" data-testid="trade-home-kpis">
            <button
              v-for="card in kpis"
              :key="card.label"
              type="button"
              class="home-panel home-kpi-card"
              :class="actionCardClass(card.tone)"
              @click="openRoute(card.route)"
            >
              <div class="text-[11px] font-semibold uppercase tracking-[0.12em] text-ngb-muted">{{ card.label }}</div>
              <div class="mt-3 text-[1.65rem] font-semibold tracking-[-0.03em] text-ngb-text">{{ card.value }}</div>
              <div class="mt-2 text-sm leading-6 text-ngb-muted">{{ card.context }}</div>
              <div class="trade-home-link">
                <span>Open analysis</span>
                <NgbIcon name="arrow-right" :size="14" />
              </div>
            </button>
          </div>
        </section>

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">Trends</div>
              <h2 class="home-section-title">Signals worth watching</h2>
            </div>
            <div class="text-sm text-ngb-muted">Charts should lead into reports and action, not decorate the page.</div>
          </div>

          <div class="grid grid-cols-1 gap-4 xl:grid-cols-2">
            <button type="button" class="home-panel home-chart-card" @click="openRoute(salesMixChart?.route ?? routes.salesByItem)">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">{{ salesMixChart?.title ?? 'Sales mix by item' }}</h3>
                  <p class="mt-1 text-sm text-ngb-muted">{{ salesMixChart?.subtitle ?? 'Net sales and gross margin for the top-selling items this month' }}</p>
                </div>
                <span class="home-chart-tag">Sales</span>
              </div>

              <div v-if="salesMixChart && salesMixChart.labels.length > 0" class="home-chart-frame">
                <NgbTrendChart :labels="salesMixChart.labels" :series="salesMixChart.series" mode="bar" />
              </div>
              <div v-else class="trade-home-empty">No posted sales activity exists for the selected month yet.</div>
            </button>

            <button type="button" class="home-panel home-chart-card" @click="openRoute(inventoryFootprintChart?.route ?? routes.inventory)">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">{{ inventoryFootprintChart?.title ?? 'Inventory footprint' }}</h3>
                  <p class="mt-1 text-sm text-ngb-muted">{{ inventoryFootprintChart?.subtitle ?? 'Largest on-hand positions across item and warehouse combinations' }}</p>
                </div>
                <span class="home-chart-tag">Inventory</span>
              </div>

              <div v-if="inventoryFootprintChart && inventoryFootprintChart.labels.length > 0" class="home-chart-frame">
                <NgbTrendChart :labels="inventoryFootprintChart.labels" :series="inventoryFootprintChart.series" mode="bar" />
              </div>
              <div v-else class="trade-home-empty">Inventory balances are empty for the selected as-of date.</div>
            </button>
          </div>
        </section>

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">Commercial Snapshots</div>
              <h2 class="home-section-title">Short lists that let you jump straight into work</h2>
            </div>
            <div class="text-sm text-ngb-muted">Top contributors and supplier/customer concentration for the current month.</div>
          </div>

          <div class="grid grid-cols-1 gap-4 xl:grid-cols-3">
            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Top Selling Items</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Best net revenue contributors this month</p>
                </div>
                <button type="button" class="trade-home-inline-link" @click="openRoute(routes.salesByItem)">View all</button>
              </div>

              <div v-if="topItems.length" class="mt-5 space-y-3">
                <button
                  v-for="item in topItems"
                  :key="item.item"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(item.route)"
                >
                  <div class="min-w-0">
                    <div class="truncate text-sm font-medium text-ngb-text">{{ item.item }}</div>
                    <div class="mt-2 text-sm text-ngb-muted">{{ formatQuantity(item.soldQuantity) }} units sold</div>
                  </div>
                  <div class="trade-home-values">
                    <div class="trade-home-value">{{ formatDashboardMoneyCompact(item.netSales) }}</div>
                    <NgbBadge :tone="marginTone(item.grossMargin, item.marginPercent)">{{ formatDashboardPercent(item.marginPercent) }}</NgbBadge>
                  </div>
                </button>
              </div>
              <div v-else class="trade-home-empty trade-home-empty-compact">No item sales have been posted in the current month.</div>
            </div>

            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Top Customers</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Net sales contribution by customer</p>
                </div>
                <button type="button" class="trade-home-inline-link" @click="openRoute(routes.salesByCustomer)">View all</button>
              </div>

              <div v-if="topCustomers.length" class="mt-5 space-y-3">
                <button
                  v-for="customer in topCustomers"
                  :key="customer.customer"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(customer.route)"
                >
                  <div class="min-w-0">
                    <div class="truncate text-sm font-medium text-ngb-text">{{ customer.customer }}</div>
                    <div class="mt-2 text-sm text-ngb-muted">
                      {{ formatDashboardCount(customer.salesDocumentCount) }} sales docs · {{ formatDashboardCount(customer.returnDocumentCount) }} returns
                    </div>
                  </div>
                  <div class="trade-home-values">
                    <div class="trade-home-value">{{ formatDashboardMoneyCompact(customer.netSales) }}</div>
                    <NgbBadge :tone="marginTone(customer.grossMargin, customer.marginPercent)">{{ formatDashboardPercent(customer.marginPercent) }}</NgbBadge>
                  </div>
                </button>
              </div>
              <div v-else class="trade-home-empty trade-home-empty-compact">No customer sales activity is available for this month yet.</div>
            </div>

            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Top Vendors</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Net purchases by supplier</p>
                </div>
                <button type="button" class="trade-home-inline-link" @click="openRoute(routes.purchasesByVendor)">View all</button>
              </div>

              <div v-if="topVendors.length" class="mt-5 space-y-3">
                <button
                  v-for="vendor in topVendors"
                  :key="vendor.vendor"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(vendor.route)"
                >
                  <div class="min-w-0">
                    <div class="truncate text-sm font-medium text-ngb-text">{{ vendor.vendor }}</div>
                    <div class="mt-2 text-sm text-ngb-muted">
                      {{ formatDashboardCount(vendor.purchaseDocumentCount) }} receipts · {{ formatDashboardCount(vendor.returnDocumentCount) }} returns
                    </div>
                  </div>
                  <div class="trade-home-values">
                    <div class="trade-home-value">{{ formatDashboardMoneyCompact(vendor.netPurchases) }}</div>
                    <NgbBadge tone="neutral">Vendor</NgbBadge>
                  </div>
                </button>
              </div>
              <div v-else class="trade-home-empty trade-home-empty-compact">No vendor purchasing activity is available for this month yet.</div>
            </div>
          </div>
        </section>

        <section class="space-y-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <div>
              <div class="home-section-label">Live Trade Activity</div>
              <h2 class="home-section-title">Inventory posture and latest documents</h2>
            </div>
            <div class="text-sm text-ngb-muted">Current balance positions and the most recent trade events.</div>
          </div>

          <div class="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Largest Inventory Positions</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Highest on-hand quantities across item and warehouse combinations</p>
                </div>
                <button type="button" class="trade-home-inline-link" @click="openRoute(routes.inventory)">View balances</button>
              </div>

              <div v-if="inventoryPositions.length" class="mt-5 space-y-3">
                <button
                  v-for="position in inventoryPositions"
                  :key="`${position.item}:${position.warehouse}`"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(position.route)"
                >
                  <div class="min-w-0">
                    <div class="truncate text-sm font-medium text-ngb-text">{{ position.item }}</div>
                    <div class="mt-2 text-sm text-ngb-muted">{{ position.warehouse }}</div>
                  </div>
                  <div class="trade-home-values">
                    <div class="trade-home-value">{{ formatQuantity(position.quantity) }}</div>
                    <NgbBadge tone="neutral">On hand</NgbBadge>
                  </div>
                </button>
              </div>
              <div v-else class="trade-home-empty trade-home-empty-compact">No inventory balance positions are available yet.</div>
            </div>

            <div class="home-panel home-snapshot-card">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h3 class="text-base font-semibold text-ngb-text">Recent Documents</h3>
                  <p class="mt-1 text-sm text-ngb-muted">Latest posted and draft trade activity</p>
                </div>
              </div>

              <div v-if="recentDocuments.length" class="mt-5 space-y-3">
                <button
                  v-for="document in recentDocuments"
                  :key="`${document.title}:${document.documentDate ?? ''}`"
                  type="button"
                  class="home-list-item"
                  @click="openRoute(document.route)"
                >
                  <div class="min-w-0">
                    <div class="truncate text-sm font-medium text-ngb-text">{{ document.title }}</div>
                    <div class="mt-2 text-sm text-ngb-muted">{{ document.notes }}</div>
                  </div>
                  <div class="trade-home-values">
                    <div class="trade-home-value">{{ document.amountDisplay ?? 'n/a' }}</div>
                    <NgbBadge :tone="recentDocumentTone(document.notes)">{{ document.documentDate ?? 'Date n/a' }}</NgbBadge>
                  </div>
                </button>
              </div>
              <div v-else class="trade-home-empty trade-home-empty-compact">No recent trade documents exist yet.</div>
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
.home-kpi-card,
.home-list-item {
  transition: transform 140ms ease, box-shadow 140ms ease, border-color 140ms ease;
}

.home-attention-card,
.home-chart-card,
.home-kpi-card,
.home-snapshot-card {
  text-align: left;
}

.home-chart-card {
  --home-tone: var(--ngb-accent-1);
  padding: 1rem;
}

.home-kpi-card {
  padding: 1rem;
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
.home-kpi-card:hover,
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

.home-chart-frame {
  height: 280px;
  margin-top: 1rem;
  border: 1px solid color-mix(in srgb, var(--ngb-border) 85%, transparent);
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-bg) 72%, var(--ngb-card));
  padding: 0.35rem 0.5rem;
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
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.85rem;
  width: 100%;
  border: 1px solid var(--ngb-border);
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-card) 92%, var(--ngb-bg));
  padding: 0.8rem 0.9rem;
  text-align: left;
}

.home-snapshot-card {
  padding: 1rem;
}

.trade-home-inline-link,
.trade-home-link {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  color: var(--ngb-text);
  font-size: 0.82rem;
  font-weight: 600;
  white-space: nowrap;
}

.trade-home-link {
  margin-top: 0.85rem;
}

.trade-home-values {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.35rem;
  flex-shrink: 0;
  text-align: right;
}

.trade-home-value {
  color: var(--ngb-text);
  font-size: 0.92rem;
  font-weight: 700;
}

.trade-home-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 180px;
  margin-top: 1rem;
  border: 1px dashed color-mix(in srgb, var(--ngb-border) 80%, transparent);
  border-radius: var(--ngb-radius);
  color: var(--ngb-muted);
  text-align: center;
  font-size: 0.92rem;
  line-height: 1.6;
  padding: 1rem;
  background: color-mix(in srgb, var(--ngb-bg) 72%, var(--ngb-card));
}

.trade-home-empty-compact {
  min-height: 96px;
}

:global(html.dark) .home-panel {
  box-shadow: 0 16px 36px rgba(0, 0, 0, 0.3);
}

:global(html.dark) .home-attention-card:hover,
:global(html.dark) .home-chart-card:hover,
:global(html.dark) .home-kpi-card:hover,
:global(html.dark) .home-list-item:hover {
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.34);
}

@media (max-width: 768px) {
  .home-section-title {
    font-size: 1.28rem;
  }

  .home-chart-frame {
    height: 240px;
  }
}
</style>
