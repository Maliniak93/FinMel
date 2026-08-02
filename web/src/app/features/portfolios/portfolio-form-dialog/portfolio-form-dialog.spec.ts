import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { PortfolioResponse } from '../../../api/portfolio';
import { PortfolioFormDialog, type PortfolioFormDialogData } from './portfolio-form-dialog';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const existingPortfolio: PortfolioResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Retirement',
  description: 'Long-term',
  currency: 'PLN',
  isArchived: false,
  assetCount: 0,
};

describe('PortfolioFormDialog', () => {
  let fixture: ComponentFixture<PortfolioFormDialog>;
  let component: PortfolioFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(data: PortfolioFormDialogData): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');

    await TestBed.configureTestingModule({
      imports: [PortfolioFormDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PortfolioFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('creates with the form values and closes with true on success', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingPortfolio, name: 'New one' }, 201));

    component['form'].controls.name.setValue('New one');

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('POST');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('updates an existing portfolio via PUT and closes with true on success', async () => {
    await setup({ portfolio: existingPortfolio });
    fetchSpy.mockResolvedValue(jsonResponse(existingPortfolio));

    expect(component['form'].controls.name.value).toBe('Retirement');

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(existingPortfolio.id);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('does not submit an invalid form', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(jsonResponse(existingPortfolio, 201));

    component['form'].controls.name.setValue('');

    await component['onSubmit']();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces a duplicate-name conflict on the name field, without closing', async () => {
    await setup({});
    fetchSpy.mockResolvedValue(
      jsonResponse(
        {
          detail: "A portfolio named 'New one' already exists.",
          errorCode: 'Conflict.DuplicatePortfolioName',
        },
        409,
      ),
    );

    component['form'].controls.name.setValue('New one');

    await component['onSubmit']();

    expect(component['form'].controls.name.hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({});

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
