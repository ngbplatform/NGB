<template>
  <div data-testid="site-shell" class="flex h-screen min-h-0 w-full min-w-0 overflow-hidden bg-ngb-bg text-ngb-text">
    <!-- Left sidebar (full height) -->
    <div class="hidden h-full min-h-0 shrink-0 self-stretch md:block relative z-20" :class="sidebarCollapsed ? 'w-[72px]' : 'w-[320px]'">
      <NgbSiteSidebar
        :productTitle="productTitle"
        :brandSubtitle="moduleTitle"
        :pinned="pinned"
        :recent="recent"
        :nodes="nodes"
        :selectedId="selectedId"
        :themeResolved="themeResolved"
        :collapsed="sidebarCollapsed"
        @toggleCollapsed="toggleSidebar"
        @navigate="$emit('navigate', $event)"
        @select="(id, route) => $emit('select', id, route)"
      />
    </div>

    <!-- Right column -->
    <div class="flex-1 min-w-0 flex flex-col min-h-0">
      <NgbTopBar
        :pageTitle="resolvedPageTitle"
        :canBack="canBack"
        :unreadNotifications="unreadNotifications"
        :userName="resolvedUserName"
        :userEmail="resolvedUserEmail"
        :userMeta="resolvedUserMeta"
        :userMetaIcon="resolvedUserMetaIcon"
        :themeResolved="themeResolved"
        :hasSettings="settingsSections.length > 0"
        :showMainMenu="nodes.length > 0"
        @openMainMenu="mobileMainMenuOpen = true"
        @openPalette="$emit('openPalette')"
        @back="$emit('back')"
        @openNotifications="helpOpen = false; settingsOpen = false; notificationsOpen = true"
        @openHelp="notificationsOpen = false; settingsOpen = false; helpOpen = true"
        @openSettings="notificationsOpen = false; helpOpen = false; settingsOpen = true"
        @signOut="$emit('signOut')"
        @toggleTheme="theme.toggle()"
      />

      <main data-testid="site-main" class="flex-1 min-h-0 overflow-hidden flex flex-col">
        <slot />
      </main>
    </div>

    <NgbDrawer
      :open="mobileMainMenuOpen"
      title="Main menu"
      hide-header
      flush-body
      side="left"
      panel-class="max-w-[320px]"
      @update:open="mobileMainMenuOpen = $event"
    >
      <div data-testid="mobile-main-menu-sidebar" class="h-full">
        <NgbSiteSidebar
          :productTitle="productTitle"
          :brandSubtitle="moduleTitle"
          :pinned="pinned"
          :recent="recent"
          :nodes="nodes"
          :selectedId="selectedId"
          :themeResolved="themeResolved"
          :collapsed="false"
          :allowCollapse="false"
          @navigate="handleMobileNavigate"
          @select="handleMobileSelect"
        />
      </div>
    </NgbDrawer>

    <!-- Notifications drawer (right sidebar) -->
    <NgbDrawer
      :open="notificationsOpen"
      title="Notifications"
      subtitle="Updates and alerts"
      @update:open="notificationsOpen = $event"
    >
      <div class="flex min-h-full items-center justify-center">
        <div class="max-w-[350px] text-center">
          <div class="text-base font-semibold text-ngb-text">No notifications</div>
          <div class="mt-2 text-sm leading-6 text-ngb-muted">
            When you receive a notification, it will appear here
          </div>
        </div>
      </div>
    </NgbDrawer>

    <!-- Help drawer (right sidebar placeholder) -->
    <NgbDrawer
      :open="helpOpen"
      title="Help"
      subtitle="NGB Assistant"
      @update:open="helpOpen = $event"
    >
      <div class="flex min-h-full items-center justify-center">
        <div class="w-full max-w-[360px] rounded-[24px] border border-ngb-border bg-ngb-bg px-6 py-7 text-center shadow-card">
          <div class="mx-auto flex h-14 w-14 items-center justify-center rounded-full border border-ngb-border bg-ngb-card text-ngb-muted">
            <NgbIcon name="help-circle" :size="26" />
          </div>

          <div class="mt-5 text-lg font-semibold text-ngb-text">Help is coming soon</div>
          <div class="mt-2 text-sm leading-6 text-ngb-muted">
            NGB Assistant will help you find features, learn workflows, and get answers while you work.
          </div>

          <div class="mt-6 rounded-[18px] border border-ngb-border bg-ngb-card px-4 py-4 text-left">
            <div class="text-xs font-semibold uppercase tracking-[0.12em] text-ngb-muted">What you'll be able to do</div>
            <div class="mt-3 space-y-3">
              <div class="flex items-start gap-3">
                <span class="mt-0.5 text-ngb-muted">
                  <NgbIcon name="search" :size="16" />
                </span>
                <span class="text-sm leading-6 text-ngb-text">Get guided onboarding</span>
              </div>
              <div class="flex items-start gap-3">
                <span class="mt-0.5 text-ngb-muted">
                  <NgbIcon name="help-circle" :size="16" />
                </span>
                <span class="text-sm leading-6 text-ngb-text">Ask product questions</span>
              </div>
              <div class="flex items-start gap-3">
                <span class="mt-0.5 text-ngb-muted">
                  <NgbIcon name="arrow-right" :size="16" />
                </span>
                <span class="text-sm leading-6 text-ngb-text">Find pages and actions faster</span>
              </div>
            </div>
          </div>

          <div class="mt-5 text-xs leading-5 text-ngb-muted">This is a UI placeholder.</div>
        </div>
      </div>
    </NgbDrawer>

    <!-- Settings drawer (right sidebar) -->
    <NgbDrawer
      :open="settingsOpen"
      title="Settings"
      subtitle="Configuration"
      @update:open="settingsOpen = $event"
    >
      <div class="space-y-6">
        <template v-if="(settingsSections?.length ?? 0) === 0">
          <div class="text-sm text-ngb-muted leading-6">
            No settings shortcuts are configured for this app.
          </div>
        </template>

        <template v-else>
          <div v-for="s in settingsSections" :key="s.label">
            <div class="text-xs font-semibold text-ngb-muted uppercase tracking-wide">{{ s.label }}</div>
            <div class="mt-2 space-y-1">
              <button
                v-for="it in s.items"
                :key="it.route"
                class="w-full text-left rounded-[var(--ngb-radius)] border border-transparent hover:border-ngb-border hover:bg-ngb-bg px-3 py-2 ngb-focus"
                @click="navigateToSettings(it.route)"
              >
                <div class="flex items-start gap-3">
                  <span class="mt-0.5 text-ngb-muted" v-if="it.icon">
                    <NgbIcon :name="it.icon" :size="18" />
                  </span>
                  <span class="mt-1 text-ngb-muted text-[10px]" v-else>•</span>
                  <div class="min-w-0">
                    <div class="text-sm font-semibold text-ngb-text truncate">{{ it.label }}</div>
                    <div v-if="it.description" class="text-xs text-ngb-muted leading-5 mt-0.5">{{ it.description }}</div>
                  </div>
                </div>
              </button>
            </div>
          </div>
        </template>
      </div>
    </NgbDrawer>
    <NgbToastHost />
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import NgbTopBar from './NgbTopBar.vue';
import NgbSiteSidebar from './NgbSiteSidebar.vue';
import NgbDrawer from '../components/NgbDrawer.vue';
import NgbIcon from '../primitives/NgbIcon.vue';
import NgbToastHost from '../primitives/NgbToastHost.vue';
import type { SiteNavNode, SiteQuickLink, SiteSettingsSection } from './types';
import { useTheme } from './useTheme';
import { provideToasts } from '../primitives/toast';

const props = defineProps<{
  moduleTitle: string;
  productTitle: string;
  envLabel?: string;
  userName?: string;
  userEmail?: string;
  userMeta?: string;
  userMetaIcon?: 'shield-check' | 'user';
  unreadNotifications?: number;
  canBack?: boolean;
  pageTitle?: string;
  pinned: SiteQuickLink[];
  recent: SiteQuickLink[];
  nodes: SiteNavNode[];
  settings?: SiteSettingsSection[];
  selectedId: string | null;
}>();

const emit = defineEmits<{
  (e: 'navigate', route: string): void;
  (e: 'select', id: string, route: string): void;
  (e: 'openPalette'): void;
  (e: 'back'): void;
  (e: 'signOut'): void;
}>();

const sidebarCollapsed = ref(false);
const theme = useTheme();
const themeResolved = computed(() => theme.resolved.value);

// Global toast stack (available to any child component).
provideToasts();

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value;
}

const notificationsOpen = ref(false);
const helpOpen = ref(false);
const settingsOpen = ref(false);
const mobileMainMenuOpen = ref(false);
const settingsSections = computed(() => props.settings ?? []);

function navigateToSettings(route: string) {
  settingsOpen.value = false;
  emit('navigate', route);
}

function handleMobileNavigate(route: string) {
  mobileMainMenuOpen.value = false;
  emit('navigate', route);
}

function handleMobileSelect(id: string, route: string) {
  mobileMainMenuOpen.value = false;
  emit('select', id, route);
}

const unreadNotifications = computed(() => props.unreadNotifications ?? 0);
const canBack = computed(() => props.canBack ?? false);

function findNodeLabel(nodes: SiteNavNode[], id: string): string | null {
  for (const n of nodes) {
    if (n.id === id) return n.label;
    if (n.children) {
      const found = findNodeLabel(n.children, id);
      if (found) return found;
    }
  }
  return null;
}

const resolvedUserName = computed(() => (props.userName?.trim() ? props.userName.trim() : 'User'));
const resolvedUserEmail = computed(() => (props.userEmail?.trim() ? props.userEmail.trim() : ''));
const resolvedUserMeta = computed(() => (props.userMeta?.trim() ? props.userMeta.trim() : ''));
const resolvedUserMetaIcon = computed<'shield-check' | 'user'>(() => props.userMetaIcon === 'shield-check' ? 'shield-check' : 'user');

const resolvedPageTitle = computed(() => {
  if (props.pageTitle?.trim()) return props.pageTitle.trim();
  if (props.selectedId) {
    const label = findNodeLabel(props.nodes, props.selectedId);
    if (label) return label;
  }
  return props.moduleTitle;
});
</script>
