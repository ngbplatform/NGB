<template>
  <header data-testid="site-topbar" class="sticky top-0 z-30 border-b border-ngb-border bg-ngb-card px-3 lg:h-14">
    <!--
      Layout goal:
      - Search stays strictly centered.
      - Left is pinned to the left edge: Back + current page title.
      - Right icons are pinned to the right edge.
    -->
    <div class="flex min-w-0 flex-col gap-2 py-2 lg:grid lg:h-full lg:grid-cols-[minmax(0,320px)_minmax(0,1fr)_minmax(0,320px)] lg:items-center lg:gap-3 lg:py-0">
      <!-- Left (intentionally empty to avoid duplicated title/back in list views) -->
      <div class="hidden h-full lg:block"></div>

      <div class="order-1 flex min-w-0 items-center gap-2 md:hidden">
        <button
          v-if="showMainMenu"
          data-testid="site-topbar-main-menu"
          class="ngb-iconbtn"
          title="Main menu"
          aria-label="Main menu"
          @click="$emit('openMainMenu')"
        >
          <NgbIcon name="panel-left" />
        </button>

        <div class="ml-auto flex min-w-0 flex-wrap items-center justify-end gap-1">
          <!-- Notifications -->
          <button class="ngb-iconbtn relative" title="Notifications" aria-label="Notifications" @click="$emit('openNotifications')">
            <NgbIcon name="bell" />
            <span
              v-if="unreadNotifications > 0"
              class="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 rounded-full bg-ngb-danger text-white text-[10px] font-bold leading-[18px] flex items-center justify-center border border-ngb-card"
            >
              {{ unreadNotifications > 99 ? '99+' : unreadNotifications }}
            </span>
          </button>

          <button class="ngb-iconbtn" title="Help" aria-label="Help" @click="$emit('openHelp')">
            <NgbIcon name="help-circle" />
          </button>

          <button v-if="hasSettings" class="ngb-iconbtn" title="Settings" aria-label="Settings" @click="$emit('openSettings')">
            <NgbIcon name="settings" />
          </button>

          <button
            class="ngb-iconbtn"
            :title="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
            :aria-label="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
            @click="$emit('toggleTheme')"
          >
            <NgbIcon :name="isDark ? 'sun' : 'moon'" />
          </button>

          <Menu as="div" class="relative" v-slot="{ open }">
            <MenuButton
              class="flex h-9 w-9 items-center justify-center rounded-full border text-white ngb-focus transition-all duration-150"
              :class="open ? 'border-[rgba(11,60,93,.45)] bg-ngb-blue shadow-[0_8px_18px_rgba(11,60,93,.16)]' : 'border-ngb-border bg-ngb-blue hover:border-[rgba(11,60,93,.35)] hover:shadow-[0_4px_12px_rgba(11,60,93,.10)]'"
              title="User"
            >
              <span class="text-[12px] font-bold">{{ initials }}</span>
            </MenuButton>

            <transition
              enter-active-class="transition duration-150 ease-out"
              enter-from-class="translate-y-1.5 opacity-0 scale-[0.98]"
              enter-to-class="translate-y-0 opacity-100 scale-100"
              leave-active-class="transition duration-100 ease-in"
              leave-from-class="translate-y-0 opacity-100 scale-100"
              leave-to-class="translate-y-1 opacity-0 scale-[0.98]"
            >
              <MenuItems class="absolute right-0 z-30 mt-2 w-[320px] max-w-[calc(100vw-24px)] rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3 shadow-card focus:outline-none">
                <div class="rounded-[var(--ngb-radius)] bg-ngb-bg px-3 py-3">
                  <div class="text-[11px] font-semibold uppercase tracking-[0.16em] text-ngb-muted">Signed in</div>
                  <div class="mt-1 truncate text-sm font-semibold text-ngb-text">{{ resolvedUserName }}</div>
                  <div v-if="hasUserEmail" class="mt-1 truncate text-xs text-ngb-muted">{{ userEmail }}</div>
                  <div v-if="hasUserMeta" class="mt-3 inline-flex items-center gap-2 rounded-[var(--ngb-radius)] bg-ngb-card px-2.5 py-1 text-[11px] font-semibold text-ngb-text">
                    <NgbIcon :name="userMetaIcon" :size="14" />
                    <span>{{ userMeta }}</span>
                  </div>
                </div>

                <div class="my-2 h-px bg-ngb-border" />

                <MenuItem v-slot="{ active }">
                  <button
                    type="button"
                    class="group flex w-full items-center gap-3 rounded-[var(--ngb-radius)] px-3 py-2.5 text-left transition-colors duration-150 ngb-focus"
                    :class="active ? 'bg-[rgba(155,28,28,.06)] text-ngb-danger' : 'text-ngb-danger hover:bg-[rgba(155,28,28,.04)]'"
                    @click="$emit('signOut')"
                  >
                    <span class="flex h-8 w-8 shrink-0 items-center justify-center text-current">
                      <NgbIcon name="log-out" :size="17" />
                    </span>

                    <span class="min-w-0 flex-1">
                      <span class="block text-sm font-semibold">Sign out</span>
                      <span class="mt-0.5 block text-xs text-ngb-muted">End this secure session on this device</span>
                    </span>
                  </button>
                </MenuItem>
              </MenuItems>
            </transition>
          </Menu>
        </div>
      </div>

      <div class="order-1 hidden min-w-0 items-center justify-end gap-1 md:flex lg:hidden">
        <button class="ngb-iconbtn relative" title="Notifications" aria-label="Notifications" @click="$emit('openNotifications')">
          <NgbIcon name="bell" />
          <span
            v-if="unreadNotifications > 0"
            class="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 rounded-full bg-ngb-danger text-white text-[10px] font-bold leading-[18px] flex items-center justify-center border border-ngb-card"
          >
            {{ unreadNotifications > 99 ? '99+' : unreadNotifications }}
          </span>
        </button>

        <button class="ngb-iconbtn" title="Help" aria-label="Help" @click="$emit('openHelp')">
          <NgbIcon name="help-circle" />
        </button>

        <button v-if="hasSettings" class="ngb-iconbtn" title="Settings" aria-label="Settings" @click="$emit('openSettings')">
          <NgbIcon name="settings" />
        </button>

        <button
          class="ngb-iconbtn"
          :title="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
          :aria-label="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
          @click="$emit('toggleTheme')"
        >
          <NgbIcon :name="isDark ? 'sun' : 'moon'" />
        </button>

        <Menu as="div" class="relative" v-slot="{ open }">
          <MenuButton
            class="flex h-9 w-9 items-center justify-center rounded-full border text-white ngb-focus transition-all duration-150"
            :class="open ? 'border-[rgba(11,60,93,.45)] bg-ngb-blue shadow-[0_8px_18px_rgba(11,60,93,.16)]' : 'border-ngb-border bg-ngb-blue hover:border-[rgba(11,60,93,.35)] hover:shadow-[0_4px_12px_rgba(11,60,93,.10)]'"
            title="User"
            aria-label="User"
          >
            <span class="text-[12px] font-bold">{{ initials }}</span>
          </MenuButton>

          <transition
            enter-active-class="transition duration-150 ease-out"
            enter-from-class="translate-y-1.5 opacity-0 scale-[0.98]"
            enter-to-class="translate-y-0 opacity-100 scale-100"
            leave-active-class="transition duration-100 ease-in"
            leave-from-class="translate-y-0 opacity-100 scale-100"
            leave-to-class="translate-y-1 opacity-0 scale-[0.98]"
          >
            <MenuItems class="absolute right-0 z-30 mt-2 w-[320px] max-w-[calc(100vw-24px)] rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3 shadow-card focus:outline-none">
              <div class="rounded-[var(--ngb-radius)] bg-ngb-bg px-3 py-3">
                <div class="text-[11px] font-semibold uppercase tracking-[0.16em] text-ngb-muted">Signed in</div>
                <div class="mt-1 truncate text-sm font-semibold text-ngb-text">{{ resolvedUserName }}</div>
                <div v-if="hasUserEmail" class="mt-1 truncate text-xs text-ngb-muted">{{ userEmail }}</div>
                <div v-if="hasUserMeta" class="mt-3 inline-flex items-center gap-2 rounded-[var(--ngb-radius)] bg-ngb-card px-2.5 py-1 text-[11px] font-semibold text-ngb-text">
                  <NgbIcon :name="userMetaIcon" :size="14" />
                  <span>{{ userMeta }}</span>
                </div>
              </div>

              <div class="my-2 h-px bg-ngb-border" />

              <MenuItem v-slot="{ active }">
                <button
                  type="button"
                  class="group flex w-full items-center gap-3 rounded-[var(--ngb-radius)] px-3 py-2.5 text-left transition-colors duration-150 ngb-focus"
                  :class="active ? 'bg-[rgba(155,28,28,.06)] text-ngb-danger' : 'text-ngb-danger hover:bg-[rgba(155,28,28,.04)]'"
                  @click="$emit('signOut')"
                >
                  <span class="flex h-8 w-8 shrink-0 items-center justify-center text-current">
                    <NgbIcon name="log-out" :size="17" />
                  </span>

                  <span class="min-w-0 flex-1">
                    <span class="block text-sm font-semibold">Sign out</span>
                    <span class="mt-0.5 block text-xs text-ngb-muted">End this secure session on this device</span>
                  </span>
                </button>
              </MenuItem>
            </MenuItems>
          </transition>
        </Menu>
      </div>

      <!-- Center -->
      <div class="order-2 min-w-0 lg:order-none lg:flex lg:justify-center">
        <!-- Searchbar (kept as-is) -->
        <button
          class="w-full max-w-[720px] h-10 rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card hover:bg-ngb-bg ngb-focus flex items-center gap-3 px-3 text-left"
          @click="$emit('openPalette')"
        >
          <span class="text-ngb-muted">
            <NgbIcon name="search" />
          </span>
          <span class="text-ngb-muted text-sm truncate">Search pages, records, reports, or run a command…</span>
          <span class="ml-auto flex items-center gap-1">
            <span class="ngb-kbd">{{ primaryModifier }}</span>
            <span class="ngb-kbd">K</span>
          </span>
        </button>
      </div>

      <!-- Right -->
      <div class="hidden min-w-0 flex-wrap items-center justify-end gap-1 lg:flex lg:order-none lg:flex-nowrap">
        <!-- Notifications -->
        <button class="ngb-iconbtn relative" title="Notifications" aria-label="Notifications" @click="$emit('openNotifications')">
          <NgbIcon name="bell" />
          <span
            v-if="unreadNotifications > 0"
            class="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 rounded-full bg-ngb-danger text-white text-[10px] font-bold leading-[18px] flex items-center justify-center border border-ngb-card"
          >
            {{ unreadNotifications > 99 ? '99+' : unreadNotifications }}
          </span>
        </button>

        <!-- Help -->
        <button class="ngb-iconbtn" title="Help" aria-label="Help" @click="$emit('openHelp')">
          <NgbIcon name="help-circle" />
        </button>

        <!-- Settings -->
        <button v-if="hasSettings" class="ngb-iconbtn" title="Settings" aria-label="Settings" @click="$emit('openSettings')">
          <NgbIcon name="settings" />
        </button>

        <!-- Theme toggle (icon shows the *target* mode) -->
        <button
          class="ngb-iconbtn"
          :title="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
          :aria-label="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
          @click="$emit('toggleTheme')"
        >
          <NgbIcon :name="isDark ? 'sun' : 'moon'" />
        </button>

        <!-- User menu (keep as-is) -->
        <Menu as="div" class="relative" v-slot="{ open }">
          <MenuButton
            class="flex h-9 w-9 items-center justify-center rounded-full border text-white ngb-focus transition-all duration-150"
            :class="open ? 'border-[rgba(11,60,93,.45)] bg-ngb-blue shadow-[0_8px_18px_rgba(11,60,93,.16)]' : 'border-ngb-border bg-ngb-blue hover:border-[rgba(11,60,93,.35)] hover:shadow-[0_4px_12px_rgba(11,60,93,.10)]'"
            title="User"
            aria-label="User"
          >
            <span class="text-[12px] font-bold">{{ initials }}</span>
          </MenuButton>

          <transition
            enter-active-class="transition duration-150 ease-out"
            enter-from-class="translate-y-1.5 opacity-0 scale-[0.98]"
            enter-to-class="translate-y-0 opacity-100 scale-100"
            leave-active-class="transition duration-100 ease-in"
            leave-from-class="translate-y-0 opacity-100 scale-100"
            leave-to-class="translate-y-1 opacity-0 scale-[0.98]"
          >
            <MenuItems class="absolute right-0 z-30 mt-2 w-[320px] max-w-[calc(100vw-24px)] rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3 shadow-card focus:outline-none">
              <div class="rounded-[var(--ngb-radius)] bg-ngb-bg px-3 py-3">
                <div class="text-[11px] font-semibold uppercase tracking-[0.16em] text-ngb-muted">Signed in</div>
                <div class="mt-1 truncate text-sm font-semibold text-ngb-text">{{ resolvedUserName }}</div>
                <div v-if="hasUserEmail" class="mt-1 truncate text-xs text-ngb-muted">{{ userEmail }}</div>
                <div v-if="hasUserMeta" class="mt-3 inline-flex items-center gap-2 rounded-[var(--ngb-radius)] bg-ngb-card px-2.5 py-1 text-[11px] font-semibold text-ngb-text">
                  <NgbIcon :name="userMetaIcon" :size="14" />
                  <span>{{ userMeta }}</span>
                </div>
              </div>

              <div class="my-2 h-px bg-ngb-border" />

              <MenuItem v-slot="{ active }">
                <button
                  type="button"
                  class="group flex w-full items-center gap-3 rounded-[var(--ngb-radius)] px-3 py-2.5 text-left transition-colors duration-150 ngb-focus"
                  :class="active ? 'bg-[rgba(155,28,28,.06)] text-ngb-danger' : 'text-ngb-danger hover:bg-[rgba(155,28,28,.04)]'"
                  @click="$emit('signOut')"
                >
                  <span class="flex h-8 w-8 shrink-0 items-center justify-center text-current">
                    <NgbIcon name="log-out" :size="17" />
                  </span>

                  <span class="min-w-0 flex-1">
                    <span class="block text-sm font-semibold">Sign out</span>
                    <span class="mt-0.5 block text-xs text-ngb-muted">End this secure session on this device</span>
                  </span>
                </button>
              </MenuItem>
            </MenuItems>
          </transition>
        </Menu>
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { Menu, MenuButton, MenuItems, MenuItem } from '@headlessui/vue';
import NgbIcon from '../primitives/NgbIcon.vue';

const props = defineProps<{
  pageTitle: string;
  canBack: boolean;
  unreadNotifications: number;
  themeResolved: 'light' | 'dark';
  userName?: string;
  userEmail?: string;
  userMeta?: string;
  userMetaIcon?: 'shield-check' | 'user';
  hasSettings?: boolean;
  showMainMenu?: boolean;
}>();

defineEmits<{
  (e: 'back'): void;
  (e: 'openMainMenu'): void;
  (e: 'openPalette'): void;
  (e: 'openNotifications'): void;
  (e: 'openHelp'): void;
  (e: 'openSettings'): void;
  (e: 'signOut'): void;
  (e: 'toggleTheme'): void;
}>();

const isDark = computed(() => props.themeResolved === 'dark');
const isMac = computed(() => {
  if (typeof navigator === 'undefined') return false;
  return /Mac|iPhone|iPad|iPod/i.test(String(navigator.platform ?? ''));
});
const primaryModifier = computed(() => (isMac.value ? '⌘' : 'Ctrl'));
const userEmail = computed(() => String(props.userEmail ?? '').trim());
const userMeta = computed(() => String(props.userMeta ?? '').trim());
const userMetaIcon = computed<'shield-check' | 'user'>(() => props.userMetaIcon === 'shield-check' ? 'shield-check' : 'user');
const hasUserEmail = computed(() => userEmail.value.length > 0);
const hasUserMeta = computed(() => userMeta.value.length > 0);
const resolvedUserName = computed(() => {
  const value = String(props.userName ?? '').trim();
  return value || 'User';
});

const initials = computed(() => {
  const n = resolvedUserName.value;
  if (!n) return 'U';
  const parts = n.split(/\s+/g).filter(Boolean);
  const a = parts[0]?.[0] ?? 'U';
  const b = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : (parts[0]?.[1] ?? '');
  return (a + b).toUpperCase();
});
</script>
