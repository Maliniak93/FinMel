import { TestBed } from '@angular/core/testing';
import type { MatDialog } from '@angular/material/dialog';
import type { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';

import { client as portfolioClient } from '../api/portfolio/client.gen';
import { provideI18nTesting } from '../core/i18n/testing';
import { jsonResponse } from '../features/assets/asset-form/testing/asset-form-fixtures';
import { confirmRemoveAsset } from './confirm-remove-asset';

describe('confirmRemoveAsset', () => {
  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideI18nTesting()] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the problem detail on failure', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ title: 'Conflict', status: 409, detail: 'The account is archived.' }, 409),
    );
    const dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }) };
    const snackBar = { open: vi.fn() };

    const deleted = await TestBed.runInInjectionContext(() =>
      confirmRemoveAsset(dialog as unknown as MatDialog, snackBar as unknown as MatSnackBar, {
        assetId: '44444444-4444-4444-4444-444444444444',
        name: 'CD Projekt',
        transactionCount: 2,
        held: { quantity: 10, unit: 'pieces' },
      }),
    );

    expect(deleted).toBe(false);
    expect(snackBar.open).toHaveBeenCalledWith('The account is archived.', expect.any(String));
  });
});
