export type SiteNavNode = {
  id: string;
  label: string;
  route?: string;
  icon?: string | null;
  badge?: string;
  disabled?: boolean;
  children?: SiteNavNode[];
};

export type SiteQuickLink = {
  id: string;
  title: string;
  subtitle?: string;
  route: string;
};

// Settings hub (opened via topbar gear icon).
// This is intentionally simple and app-defined (the host app decides which pages are "settings").
export type SiteSettingsItem = {
  label: string;
  description?: string;
  route: string;
  // Optional icon name from NgbIcon (string to keep this type decoupled from the icon implementation).
  icon?: string;
};

export type SiteSettingsSection = {
  label: string;
  items: SiteSettingsItem[];
};
