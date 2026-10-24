export interface NavLink {
  path: string;
  label: string;
  icon: string;
}

export const DASHBOARD_LINK: NavLink = {
  path: 'dashboard',
  label: 'shell.nav.dashboard',
  icon: 'dashboard',
};

export const WEALTH_LINK: NavLink = {
  path: 'wealth',
  label: 'shell.nav.wealth',
  icon: 'wallet',
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

export const NAV_LINKS: readonly NavLink[] = [
  DASHBOARD_LINK,
  WEALTH_LINK,
  PLANNING_LINK,
  GROUPS_LINK,
];
