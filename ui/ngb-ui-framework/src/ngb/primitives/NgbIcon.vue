<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    class="block"
  >
    <!-- Arrow left -->
    <template v-if="name === 'arrow-left'">
      <path d="M10 6l-6 6 6 6" />
      <path d="M4 12h16" />
    </template>

    <!-- Arrow right -->
    <template v-else-if="name === 'arrow-right'">
      <path d="M14 6l6 6-6 6" />
      <path d="M4 12h16" />
    </template>

    <!-- Plus -->
    <template v-else-if="name === 'plus'">
      <path d="M12 5v14" />
      <path d="M5 12h14" />
    </template>

    <!-- Minus -->
    <template v-else-if="name === 'minus'">
      <path d="M5 12h14" />
    </template>

    <!-- Trash -->
    <template v-else-if="name === 'trash'">
      <path d="M3 6h18" />
      <path d="M8 6V4h8v2" />
      <path d="M6 6l1 16h10l1-16" />
      <path d="M10 11v7" />
      <path d="M14 11v7" />
    </template>

    <!-- Trash restore (unmark) -->
    <template v-else-if="name === 'trash-restore'">
      <path d="M3 6h18" />
      <path d="M8 6V4h8v2" />
      <path d="M6 6l1 16h10l1-16" />
      <path d="M12 18v-6" />
      <path d="M9.5 14.5 12 12l2.5 2.5" />
    </template>

    <!-- Check (post) -->
    <template v-else-if="name === 'check'">
      <path d="M20 6 9 17l-5-5" />
    </template>

    <!-- Undo (unpost) -->
    <template v-else-if="name === 'undo'">
      <path d="M9 14 4 9l5-5" />
      <path d="M4 9h10a6 6 0 0 1 0 12h-1" />
    </template>

    <!-- Panel left -->
    <template v-else-if="name === 'panel-left'">
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M9 4v16" />
    </template>

    <!-- Panel right (open compact page) -->
    <template v-else-if="name === 'panel-right'">
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M15 4v16" />
    </template>

    <!-- Grip vertical -->
    <template v-else-if="name === 'grip-vertical'">
      <circle cx="9" cy="7" r="1" fill="currentColor" stroke="none" />
      <circle cx="15" cy="7" r="1" fill="currentColor" stroke="none" />
      <circle cx="9" cy="12" r="1" fill="currentColor" stroke="none" />
      <circle cx="15" cy="12" r="1" fill="currentColor" stroke="none" />
      <circle cx="9" cy="17" r="1" fill="currentColor" stroke="none" />
      <circle cx="15" cy="17" r="1" fill="currentColor" stroke="none" />
    </template>

    <!-- Bell -->
    <template v-else-if="name === 'bell'">
      <path d="M18 8a6 6 0 0 0-12 0c0 7-3 8-3 8h18s-3-1-3-8" />
      <path d="M13.73 21a2 2 0 0 1-3.46 0" />
    </template>

    <!-- Help circle -->
    <template v-else-if="name === 'help-circle'">
      <circle cx="12" cy="12" r="9" />
      <path d="M9.5 9.5a2.5 2.5 0 0 1 5 0c0 1.8-2 2-2 3.5" />
      <path d="M12 17h.01" />
    </template>

    <!-- Settings -->
    <template v-else-if="name === 'settings'">
      <!-- Feather Icons: settings (cog) -->
      <circle cx="12" cy="12" r="3" />
      <path
        d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.6a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"
      />
    </template>

    <!-- Cogs -->
    <template v-else-if="name === 'cogs'">
      <circle cx="9" cy="15" r="3.25" />
      <path d="M9 9.5v1.35M9 19.15v1.35M3.5 15h1.35M13.15 15h1.35" />
      <path d="M5.1 11.1l.95.95M11.95 17.95l.95.95M5.1 18.9l.95-.95M11.95 12.05l.95-.95" />

      <circle cx="16.75" cy="8" r="2.35" />
      <path d="M16.75 4.3v1.05M16.75 10.65v1.05M13.05 8h1.05M19.4 8h1.05" />
      <path d="M14.15 5.4l.75.75M18.35 9.6l.75.75M14.15 10.6l.75-.75M18.35 6.4l.75-.75" />
    </template>

    <!-- Sun -->
    <template v-else-if="name === 'sun'">
      <circle cx="12" cy="12" r="3.5" />
      <path d="M12 2v2.5M12 19.5V22" />
      <path d="M4.2 4.2l1.8 1.8M18 18l1.8 1.8" />
      <path d="M2 12h2.5M19.5 12H22" />
      <path d="M4.2 19.8 6 18M18 6l1.8-1.8" />
    </template>

    <!-- Moon -->
    <template v-else-if="name === 'moon'">
      <!-- Feather Icons: moon -->
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
    </template>

    <!-- Home -->
    <template v-else-if="name === 'home'">
      <path d="M3 11l9-8 9 8" />
      <path d="M5 10v10h5v-6h4v6h5V10" />
    </template>

    <!-- Grid -->
    <template v-else-if="name === 'grid'">
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </template>

    <!-- List -->
    <template v-else-if="name === 'list'">
      <path d="M9 6h11" />
      <path d="M9 12h11" />
      <path d="M9 18h11" />
      <circle cx="5" cy="6" r="1.2" fill="currentColor" stroke="none" />
      <circle cx="5" cy="12" r="1.2" fill="currentColor" stroke="none" />
      <circle cx="5" cy="18" r="1.2" fill="currentColor" stroke="none" />
    </template>

    <!-- File text -->
    <template v-else-if="name === 'file-text'">
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <path d="M14 2v6h6" />
      <path d="M8 13h8" />
      <path d="M8 17h8" />
      <path d="M8 9h3" />
    </template>

    <!-- Bar chart -->
    <template v-else-if="name === 'bar-chart'">
      <path d="M12 20V10" />
      <path d="M18 20V4" />
      <path d="M6 20v-6" />
      <path d="M4 20h16" />
    </template>

    <!-- Calculator -->
    <template v-else-if="name === 'calculator'">
      <rect x="5" y="3" width="14" height="18" rx="2" />
      <path d="M8 7h8" />
      <path d="M8 11h2" />
      <path d="M14 11h2" />
      <path d="M8 15h2" />
      <path d="M14 15h2" />
      <path d="M8 19h2" />
      <path d="M14 19h2" />
    </template>

    <!-- Book open -->
    <template v-else-if="name === 'book-open'">
      <path d="M12 7a4 4 0 0 0-4-2H4v14h4a4 4 0 0 1 4 2" />
      <path d="M12 7a4 4 0 0 1 4-2h4v14h-4a4 4 0 0 0-4 2" />
    </template>

    <!-- Receipt -->
    <template v-else-if="name === 'receipt'">
      <path d="M6 3h12v18l-2.5-1.8L13 21l-2.5-1.8L8 21l-2-1.8V3z" />
      <path d="M9 8h6" />
      <path d="M9 12h6" />
      <path d="M9 16h4" />
    </template>

    <!-- Users -->
    <template v-else-if="name === 'users'">
      <path d="M16 21v-1.5a3.5 3.5 0 0 0-3.5-3.5h-1A3.5 3.5 0 0 0 8 19.5V21" />
      <circle cx="12" cy="10" r="3.2" />
      <path d="M21 21v-1a3 3 0 0 0-2.4-2.95" />
      <path d="M3 21v-1A3 3 0 0 1 5.4 17.05" />
      <path d="M17.5 7.4a2.7 2.7 0 1 1 0 5.2" />
      <path d="M6.5 7.4a2.7 2.7 0 1 0 0 5.2" />
    </template>

    <!-- User -->
    <template v-else-if="name === 'user'">
      <circle cx="12" cy="8.5" r="3.2" />
      <path d="M5.5 20.5v-1.1A4.9 4.9 0 0 1 10.4 14.5h3.2a4.9 4.9 0 0 1 4.9 4.9v1.1" />
    </template>

    <!-- Building 2 -->
    <template v-else-if="name === 'building-2'">
      <rect x="4" y="3" width="10" height="18" rx="1.8" />
      <path d="M14 8h6v13h-6" />
      <path d="M7 7h1" />
      <path d="M10 7h1" />
      <path d="M7 11h1" />
      <path d="M10 11h1" />
      <path d="M7 15h1" />
      <path d="M10 15h1" />
      <path d="M8.5 21v-4h2v4" />
    </template>

    <!-- Coins -->
    <template v-else-if="name === 'coins'">
      <ellipse cx="9" cy="8" rx="4" ry="2" />
      <path d="M5 8v4.5c0 1.1 1.8 2 4 2s4-.9 4-2V8" />
      <ellipse cx="15.5" cy="13" rx="4" ry="2" />
      <path d="M11.5 13v4.5c0 1.1 1.8 2 4 2s4-.9 4-2V13" />
    </template>

    <!-- Wallet -->
    <template v-else-if="name === 'wallet'">
      <path d="M4 7.5A2.5 2.5 0 0 1 6.5 5H18a2 2 0 0 1 2 2v2H6.5A2.5 2.5 0 0 1 4 6.5v9A2.5 2.5 0 0 0 6.5 18H20v-8H6.5A2.5 2.5 0 0 1 4 7.5z" />
      <circle cx="16" cy="14" r="1" fill="currentColor" stroke="none" />
    </template>

    <!-- Wrench -->
    <template v-else-if="name === 'wrench'">
      <path
        d="M7 10h3V7L6.5 3.5a6 6 0 0 1 8 8l6 6a2 2 0 0 1-3 3l-6-6a6 6 0 0 1-8-8L7 10z"
      />
    </template>

    <!-- Clipboard list -->
    <template v-else-if="name === 'clipboard-list'">
      <rect x="6" y="4" width="12" height="17" rx="2" />
      <path d="M9 4.5h6V7H9z" />
      <path d="M10 11h5" />
      <path d="M10 15h5" />
      <circle cx="8" cy="11" r="1" fill="currentColor" stroke="none" />
      <circle cx="8" cy="15" r="1" fill="currentColor" stroke="none" />
    </template>

    <!-- Check square -->
    <template v-else-if="name === 'check-square'">
      <rect x="4" y="4" width="16" height="16" rx="2" />
      <path d="M8 12.2 10.7 15 16 9.8" />
    </template>

    <!-- Tag -->
    <template v-else-if="name === 'tag'">
      <path d="M20 10.5 11.5 19a2 2 0 0 1-2.8 0L3 13.3V4h9.3l5.7 5.7a2 2 0 0 1 0 2.8z" />
      <circle cx="8" cy="8" r="1.2" fill="currentColor" stroke="none" />
    </template>

    <!-- Landmark -->
    <template v-else-if="name === 'landmark'">
      <path d="M3 9h18" />
      <path d="M5 9v8" />
      <path d="M9 9v8" />
      <path d="M15 9v8" />
      <path d="M19 9v8" />
      <path d="M2 20h20" />
      <path d="M12 3 3 7v2h18V7l-9-4z" />
    </template>

    <!-- Calendar check -->
    <template v-else-if="name === 'calendar-check'">
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M8 3v4" />
      <path d="M16 3v4" />
      <path d="M3 10h18" />
      <path d="M8.5 15.2 10.8 17.5 15.5 12.8" />
    </template>

    <!-- Scale -->
    <template v-else-if="name === 'scale'">
      <path d="M12 4v14" />
      <path d="M7 7h10" />
      <path d="M5 7 2.5 12h5L5 7z" />
      <path d="M19 7 16.5 12h5L19 7z" />
      <path d="M9 21h6" />
    </template>

    <!-- Git merge -->
    <template v-else-if="name === 'git-merge'">
      <circle cx="7" cy="6" r="2" />
      <circle cx="17" cy="18" r="2" />
      <circle cx="17" cy="6" r="2" />
      <path d="M9 6h6" />
      <path d="M15 6a4 4 0 0 0-4 4v4a4 4 0 0 0 4 4" />
    </template>

    <!-- Shield -->
    <template v-else-if="name === 'shield'">
      <path d="M12 2l8 4v6c0 5-3.5 9.5-8 10-4.5-.5-8-5-8-10V6z" />
    </template>

    <!-- Shield check -->
    <template v-else-if="name === 'shield-check'">
      <path d="M12 2l8 4v6c0 5-3.5 9.5-8 10-4.5-.5-8-5-8-10V6z" />
      <path d="M8.8 12.2 10.9 14.3 15.6 9.6" />
    </template>

    <!-- Heart pulse -->
    <template v-else-if="name === 'heart-pulse'">
      <path d="M11.98 20.25c-4.6-2.9-8.23-6-8.23-10.1A4.9 4.9 0 0 1 8.63 5.25c1.4 0 2.72.6 3.6 1.66a4.66 4.66 0 0 1 3.58-1.66 4.9 4.9 0 0 1 4.94 4.9c0 4.08-3.65 7.2-8.27 10.1l-.25.16-.25-.16Z" />
      <path d="M6.9 12h2.2l1.25-2.4 2.15 5.1 1.82-3.1h2.78" />
    </template>

    <!-- Inspect with check -->
    <template v-else-if="name === 'inspect-check'">
      <circle cx="10.25" cy="10.25" r="6.75" />
      <path d="M7.75 10.35l2.05 2.05 4.05-4.05" />
      <path d="M15.2 15.2 20 20" />
    </template>

    <!-- Selected month -->
    <template v-else-if="name === 'selected-check'">
      <rect x="3.6" y="3.6" width="16.8" height="16.8" rx="2.6" />
      <path d="M8 12.15 10.8 14.95 16.1 9.65" />
    </template>

    <!-- Close month -->
    <template v-else-if="name === 'close-month'">
      <path d="M12 2.6 18.9 5.75v5.35c0 4.8-2.95 8.8-6.9 10.3-3.95-1.5-6.9-5.5-6.9-10.3V5.75L12 2.6z" />
      <path d="M8.7 11.95 11 14.25l4.4-4.4" />
    </template>

    <!-- Chevrons left -->
    <template v-else-if="name === 'chevrons-left'">
      <path d="M11 19l-7-7 7-7" />
      <path d="M20 19l-7-7 7-7" />
    </template>

    <!-- Chevrons right -->
    <template v-else-if="name === 'chevrons-right'">
      <path d="M13 5l7 7-7 7" />
      <path d="M4 5l7 7-7 7" />
    </template>

    <!-- Search -->
    <template v-else-if="name === 'search'">
      <circle cx="11" cy="11" r="7" />
      <path d="M20 20l-3.5-3.5" />
    </template>

    <!-- Filter -->
    <template v-else-if="name === 'filter'">
      <path d="M3.5 5.5h17l-6.5 7v6.5l-4 2v-8.5l-6.5-7Z" />
    </template>

    <!-- Composer -->
    <template v-else-if="name === 'composer'">
      <path d="M3.5 6.5h12.5" />
      <path d="M18 6.5h2.5" />
      <rect x="16" y="4.6" width="2.4" height="3.8" rx="0.9" />

      <path d="M3.5 12h4.8" />
      <path d="M10.7 12h9.8" />
      <rect x="8.3" y="10.1" width="2.4" height="3.8" rx="0.9" />

      <path d="M3.5 17.5h9.3" />
      <path d="M15.2 17.5h5.3" />
      <rect x="12.8" y="15.6" width="2.4" height="3.8" rx="0.9" />
    </template>

    <!-- Play -->
    <template v-else-if="name === 'play'">
      <path d="M9 7.5v9l7-4.5-7-4.5Z" fill="currentColor" stroke="none" />
    </template>

    <!-- Refresh -->
    <template v-else-if="name === 'refresh'">
      <path d="M21 12a9 9 0 1 1-3.1-6.8" />
      <path d="M21 3v6h-6" />
    </template>

    <!-- Open in new -->
    <template v-else-if="name === 'open-in-new'">
      <path d="M14 3h7v7" />
      <path d="M10 14 21 3" />
      <path d="M21 14v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7" />
    </template>

    <!-- Load variant -->
    <template v-else-if="name === 'load-variant'">
      <path d="M12 4v9.5" />
      <path d="M8.8 10.3 12 13.5l3.2-3.2" />
      <path d="M5.5 15.25v2.5A1.75 1.75 0 0 0 7.25 19.5h9.5a1.75 1.75 0 0 0 1.75-1.75v-2.5" />
    </template>

    <!-- X -->
    <template v-else-if="name === 'x'">
      <path d="M6 6l12 12M18 6L6 18" />
    </template>

    <!-- Circle X -->
    <template v-else-if="name === 'circle-x'">
      <circle cx="12" cy="12" r="9" />
      <path d="M9 9l6 6M15 9l-6 6" />
    </template>

    <!-- History -->
    <template v-else-if="name === 'history'">
      <path d="M3 12a9 9 0 1 0 3-6.7" />
      <path d="M3 4v5h5" />
      <path d="M12 7v5l3 2" />
    </template>

    <!-- Effects flow -->
    <template v-else-if="name === 'effects-flow'">
      <path d="M4 8h12" />
      <path d="M13 5 16 8l-3 3" />
      <path d="M20 16H8" />
      <path d="M11 13 8 16l3 3" />
    </template>

    <!-- File apply -->
    <template v-else-if="name === 'file-apply'">
      <path d="M12 3v8" />
      <path d="M8.8 8.8 12 12l3.2-3.2" />
      <path d="M4 14.5h4.2a2.1 2.1 0 0 1 1.6.75l1.1 1.25a1.4 1.4 0 0 0 1.05.5h.1a1.4 1.4 0 0 0 1.05-.5l1.1-1.25a2.1 2.1 0 0 1 1.6-.75H20" />
      <path d="M4 14.5v3A1.5 1.5 0 0 0 5.5 19h13a1.5 1.5 0 0 0 1.5-1.5v-3" />
    </template>

    <!-- Edit -->
    <template v-else-if="name === 'edit'">
      <path d="M3 21l3.75-.75L19 8a2.12 2.12 0 0 0-3-3L3.75 17.25 3 21z" />
      <path d="M14.5 6.5l3 3" />
    </template>

    <!-- Document flow -->
    <template v-else-if="name === 'document-flow'">
      <rect x="3.5" y="4" width="12.5" height="4.5" rx="1.35" />
      <path d="M6.25 8.5v3.25a1.75 1.75 0 0 0 1.75 1.75H10" />
      <path d="M10 13.5h1.75" />
      <rect x="11.75" y="11.25" width="8.75" height="4.25" rx="1.25" />
      <path d="M10 13.5v3.25A1.75 1.75 0 0 0 11.75 18H13.5" />
      <rect x="13.5" y="15.75" width="7" height="4.25" rx="1.25" />
      <path d="M5.75 5.75h4.5" />
      <path d="M13.5 13.25h3.25" />
      <path d="M15 17.75h2.75" />
    </template>

    <!-- Share -->
    <template v-else-if="name === 'share'">
      <circle cx="18" cy="5" r="2.2" />
      <circle cx="6" cy="12" r="2.2" />
      <circle cx="18" cy="19" r="2.2" />
      <path d="M8 11 15.8 6.3" />
      <path d="M8 13l7.8 4.7" />
    </template>

    <!-- More vertical -->
    <template v-else-if="name === 'more-vertical'">
      <circle cx="12" cy="5" r="1.45" fill="currentColor" stroke="none" />
      <circle cx="12" cy="12" r="1.45" fill="currentColor" stroke="none" />
      <circle cx="12" cy="19" r="1.45" fill="currentColor" stroke="none" />
    </template>

    <!-- Copy -->
    <template v-else-if="name === 'copy'">
      <rect x="9" y="9" width="10" height="11" rx="1.8" />
      <path d="M7 15H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h7a2 2 0 0 1 2 2v1" />
    </template>

    <!-- Printer -->
    <template v-else-if="name === 'printer'">
      <path d="M7 8V4h10v4" />
      <rect x="7" y="14" width="10" height="6.5" rx="1.3" />
      <rect x="4" y="8" width="16" height="7.5" rx="2" />
      <circle cx="17" cy="11.75" r="1" fill="currentColor" stroke="none" />
    </template>

    <!-- Save -->
    <template v-else-if="name === 'save'">
      <!-- floppy-ish -->
      <path d="M5 3h12l4 4v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" />
      <path d="M7 3v6h10V3" />
      <path d="M7 21v-7h10v7" />
    </template>

    <!-- Download -->
    <template v-else-if="name === 'download'">
      <path d="M12 4v10" />
      <path d="M8.5 10.5 12 14l3.5-3.5" />
      <path d="M4 18.5h16" />
    </template>

    <!-- Log out -->
    <template v-else-if="name === 'log-out'">
      <path d="M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4" />
      <path d="M14 16l4-4-4-4" />
      <path d="M8 12h10" />
    </template>
  </svg>
</template>

<script setup lang="ts">
import type { NgbIconName } from './iconNames'

withDefaults(
  defineProps<{
    name: NgbIconName
    size?: number
  }>(),
  {
    size: 18,
  },
)
</script>
