import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { App } from './app';
import { routes } from './app.routes';
import { Dashboard } from './features/dashboard/dashboard';
import { NotFound } from './features/not-found/not-found';
import { Portfolios } from './features/portfolios/portfolios';
import { Settings } from './features/settings/settings';

describe('app routing', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)],
    });
    harness = await RouterTestingHarness.create();
  });

  it('redirects the empty path to /dashboard', async () => {
    const component = await harness.navigateByUrl('/', Dashboard);
    expect(component).toBeInstanceOf(Dashboard);
  });

  it('renders Portfolios at /portfolios', async () => {
    const component = await harness.navigateByUrl('/portfolios', Portfolios);
    expect(component).toBeInstanceOf(Portfolios);
  });

  it('renders Settings at /settings', async () => {
    const component = await harness.navigateByUrl('/settings', Settings);
    expect(component).toBeInstanceOf(Settings);
  });

  it('renders the 404 page for unknown routes', async () => {
    const component = await harness.navigateByUrl('/nope', NotFound);
    expect(component).toBeInstanceOf(NotFound);
  });
});
