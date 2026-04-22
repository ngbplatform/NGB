<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { NgbBadge, NgbIcon, NgbPageHeader } from 'ngb-ui-framework'

import { createAgencyBillingHomeData } from '../home/homeData'

type Tone = 'neutral' | 'success' | 'warn'

const router = useRouter()
const data = createAgencyBillingHomeData()

const actions = computed(() => data.actions)
const focusAreas = computed(() => data.focusAreas)
const pulses = computed(() => data.pulses)

function openRoute(target: string): void {
  void router.push(target)
}

function cardToneClass(tone: Tone): string {
  return {
    neutral: 'home-tone-neutral',
    success: 'home-tone-success',
    warn: 'home-tone-warn',
  }[tone]
}
</script>

<template>
  <div class="flex h-full min-h-0 flex-col" data-testid="agency-billing-home-page">
    <NgbPageHeader title="Home">
      <template #secondary>
        <div class="flex min-w-0 items-center gap-2 overflow-x-auto whitespace-nowrap pb-px">
          <span class="text-sm text-ngb-muted">Agency Billing control center</span>
          <NgbBadge tone="neutral">{{ data.headerSummary }}</NgbBadge>
        </div>
      </template>
    </NgbPageHeader>

    <div class="min-h-0 flex-1 overflow-auto">
      <div class="mx-auto flex w-full max-w-[1680px] flex-col gap-6 p-6">
        <section class="grid grid-cols-1 gap-4 xl:grid-cols-[1.45fr_0.95fr]">
          <div class="home-panel home-hero-panel">
            <div class="home-section-label">Vertical</div>
            <h1 class="mt-4 max-w-[16ch] text-[2rem] font-semibold tracking-[-0.04em] text-ngb-text">
              Run time capture, billing, and collection in one workspace.
            </h1>
            <p class="mt-4 max-w-[66ch] text-sm leading-7 text-ngb-muted">
              This shell is structured for agency-style operations: client setup, delivery tracking, invoice drafting,
              and incoming cash application, all aligned with the shared NGB accounting backbone.
            </p>

            <div class="mt-6 grid grid-cols-1 gap-3 md:grid-cols-3">
              <div
                v-for="pulse in pulses"
                :key="pulse.title"
                class="home-pulse-card"
              >
                <div class="text-[11px] font-semibold uppercase tracking-[0.14em] text-ngb-muted">{{ pulse.badge }}</div>
                <div class="mt-2 text-sm font-semibold text-ngb-text">{{ pulse.title }}</div>
                <div class="mt-2 text-sm leading-6 text-ngb-muted">{{ pulse.detail }}</div>
              </div>
            </div>
          </div>

          <div class="home-panel home-action-panel">
            <div class="flex items-center justify-between gap-3">
              <div>
                <div class="home-section-label">Launchpad</div>
                <h2 class="home-section-title text-xl">Start with the next operational move</h2>
              </div>
              <NgbIcon name="sparkles" :size="18" class="text-ngb-muted" />
            </div>

            <div class="mt-5 space-y-3">
              <button
                v-for="action in actions"
                :key="action.title"
                type="button"
                class="home-panel home-action-card w-full text-left ngb-focus"
                :class="cardToneClass(action.tone)"
                @click="openRoute(action.route)"
              >
                <div class="flex items-start justify-between gap-4">
                  <div class="min-w-0">
                    <div class="home-tone-pill">
                      <NgbIcon :name="action.icon" :size="14" />
                      <span>{{ action.badge }}</span>
                    </div>
                    <div class="mt-3 text-sm font-semibold text-ngb-text">{{ action.title }}</div>
                    <div class="mt-2 text-sm leading-6 text-ngb-muted">{{ action.subtitle }}</div>
                  </div>

                  <span class="home-action-arrow mt-1" aria-hidden="true">
                    <NgbIcon name="arrow-right" :size="16" />
                  </span>
                </div>
              </button>
            </div>
          </div>
        </section>

        <section class="space-y-4">
          <div>
            <div class="home-section-label">Operating Model</div>
            <h2 class="home-section-title text-xl">Three lanes that keep the vertical coherent</h2>
          </div>

          <div class="grid grid-cols-1 gap-4 xl:grid-cols-3">
            <button
              v-for="area in focusAreas"
              :key="area.title"
              type="button"
              class="home-panel home-focus-card text-left ngb-focus"
              @click="openRoute(area.route)"
            >
              <div class="home-focus-header">
                <div class="home-focus-icon">
                  <NgbIcon :name="area.icon" :size="18" />
                </div>
                <span class="home-action-arrow" aria-hidden="true">
                  <NgbIcon name="arrow-right" :size="16" />
                </span>
              </div>

              <h3 class="mt-4 text-lg font-semibold tracking-tight text-ngb-text">{{ area.title }}</h3>
              <p class="mt-3 text-sm leading-7 text-ngb-muted">{{ area.description }}</p>

              <div class="mt-4 space-y-2">
                <div
                  v-for="point in area.points"
                  :key="point"
                  class="home-point"
                >
                  {{ point }}
                </div>
              </div>
            </button>
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

.home-hero-panel {
  padding: 1.25rem;
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--ngb-accent-1) 8%, transparent), transparent 56%),
    var(--ngb-card);
}

.home-action-panel {
  padding: 1.25rem;
}

.home-pulse-card {
  border: 1px solid color-mix(in srgb, var(--ngb-border) 82%, transparent);
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-card) 90%, var(--ngb-bg));
  padding: 0.95rem;
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

.home-action-card,
.home-focus-card {
  transition: transform 140ms ease, box-shadow 140ms ease, border-color 140ms ease;
}

.home-action-card {
  position: relative;
  overflow: hidden;
  padding: 1rem 1rem 1.1rem;
}

.home-action-card::before {
  content: '';
  position: absolute;
  inset: 0 auto 0 0;
  width: 4px;
  background: var(--home-tone, var(--ngb-border));
}

.home-action-card:hover,
.home-focus-card:hover {
  transform: translateY(-1px);
  border-color: color-mix(in srgb, var(--home-tone, var(--ngb-border)) 26%, var(--ngb-border));
  box-shadow: 0 16px 34px rgba(15, 23, 42, 0.08);
}

.home-focus-card {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  justify-content: flex-start;
  padding: 1rem;
}

.home-focus-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 2.75rem;
  gap: 1rem;
}

.home-focus-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  height: 2.75rem;
  width: 2.75rem;
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-bg) 80%, var(--ngb-card));
  color: var(--ngb-text);
}

.home-point {
  border-radius: var(--ngb-radius);
  background: color-mix(in srgb, var(--ngb-bg) 82%, var(--ngb-card));
  padding: 0.55rem 0.75rem;
  font-size: 0.92rem;
  line-height: 1.6;
  color: var(--ngb-muted);
}

.home-tone-pill {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
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

:global(html.dark) .home-panel {
  box-shadow: 0 16px 36px rgba(0, 0, 0, 0.3);
}

:global(html.dark) .home-action-card:hover,
:global(html.dark) .home-focus-card:hover {
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.34);
}

@media (max-width: 768px) {
  .home-section-title {
    font-size: 1.28rem;
  }
}
</style>
