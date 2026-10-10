import { Component } from '@angular/core';

import { getApiReportingSnapshotsLatest } from '../../api/reporting';
import type { getApiPortfolioMe } from '../../api/portfolio';

import { BondsCard } from './bonds-card/bonds-card';

type MeFn = typeof getApiPortfolioMe;

@Component({
  selector: 'app-dashboard',
  imports: [BondsCard],
  template: '<app-bonds-card />',
})
export class Dashboard {
  protected load(): ReturnType<typeof getApiReportingSnapshotsLatest> {
    return getApiReportingSnapshotsLatest();
  }
}
