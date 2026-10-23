export interface NavLink {
  path: string;
  label: string;
  icon: string;
  shortLabel?: string;
}

export interface NavSection {
  label: string;
  links: NavLink[];
}

export const ASSET_LINKS: readonly NavLink[] = [
  {
    path: 'accounts',
    label: 'shell.nav.accounts',
    icon: 'account_balance_wallet',
    shortLabel: 'shell.navShort.accounts',
  },
  {
    path: 'bank-accounts',
    label: 'shell.nav.bankAccounts',
    icon: 'account_balance',
    shortLabel: 'shell.navShort.bankAccounts',
  },
  { path: 'cash', label: 'shell.nav.cash', icon: 'payments' },
  { path: 'deposits', label: 'shell.nav.deposits', icon: 'savings' },
  { path: 'bonds', label: 'shell.nav.bonds', icon: 'receipt_long' },
  { path: 'securities', label: 'shell.nav.securities', icon: 'show_chart' },
  {
    path: 'metals',
    label: 'shell.nav.metals',
    icon: 'diamond',
    shortLabel: 'shell.navShort.metals',
  },
];

export const DASHBOARD_LINK: NavLink = {
  path: 'dashboard',
  label: 'shell.nav.dashboard',
  icon: 'dashboard',
};

export const PLANNING_LINK: NavLink = {
  path: 'planning',
  label: 'shell.nav.planning',
  icon: 'event_note',
};

export const GROUPS_LINK: NavLink = { path: 'groups', label: 'shell.nav.groups', icon: 'category' };

export const SETTINGS_LINK: NavLink = {
  path: 'settings',
  label: 'shell.nav.settings',
  icon: 'settings',
};

export const NAV_SECTIONS: readonly NavSection[] = [
  { label: 'shell.navSection.overview', links: [DASHBOARD_LINK, PLANNING_LINK] },
  { label: 'shell.navSection.wealth', links: [...ASSET_LINKS, GROUPS_LINK] },
];

export const BOTTOM_LINKS: readonly NavLink[] = [DASHBOARD_LINK, PLANNING_LINK, GROUPS_LINK];
