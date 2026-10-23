import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { isTranslationIn, looksLikeTranslationKey, textOf } from '../../../testing/i18n';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { ConfirmDialog, type ConfirmDialogData } from './confirm-dialog';

describe('ConfirmDialog', () => {
  let fixture: ComponentFixture<ConfirmDialog>;
  let component: ConfirmDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  const data: ConfirmDialogData = {
    title: 'Archive account?',
    message: 'This hides it from the list.',
  };

  beforeEach(async () => {
    dialogRef = { close: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [ConfirmDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
        provideI18nTesting(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ConfirmDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(async () => {
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('closes with true on confirm', () => {
    component['confirm']();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('closes with false on cancel', () => {
    component['cancel']();
    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('starts focus on Cancel', () => {
    const element = fixture.nativeElement as HTMLElement;
    const [cancel] = Array.from(element.querySelectorAll('button'));
    expect(cancel.hasAttribute('cdkfocusinitial')).toBe(true);
    expect(element.querySelectorAll('[cdkfocusinitial]')).toHaveLength(1);
  });

  it('is an alertdialog and Enter does not confirm', () => {
    const element = fixture.nativeElement as HTMLElement;
    expect(
      element.matches('[role="alertdialog"]') || element.querySelector('[role="alertdialog"]'),
    ).toBeTruthy();

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('destructive confirm is error-styled', async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [ConfirmDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { ...data, destructive: true } },
        { provide: MatDialogRef, useValue: dialogRef },
        provideI18nTesting(),
      ],
    }).compileComponents();
    const destructive = TestBed.createComponent(ConfirmDialog);
    await destructive.whenStable();

    const buttons = Array.from(
      (destructive.nativeElement as HTMLElement).querySelectorAll('button'),
    );
    const confirm = buttons[1];
    expect(confirm.classList).not.toContain('mat-warn');
    expect(confirm.classList).toContain('confirm-dialog__confirm--destructive');
    expect(buttons[0].classList).not.toContain('confirm-dialog__confirm--destructive');
  });

  it('destructive border outranks the global flat-button border', () => {
    const css = readFileSync(
      resolve(process.cwd(), 'src/app/shared/confirm-dialog/confirm-dialog.scss'),
      'utf8',
    );
    expect(css).toMatch(
      /button\.confirm-dialog__confirm--destructive:not\(:disabled\)\s*\{[^}]*border:\s*1px solid var\(--mat-sys-error\)/,
    );
  });

  it('default buttons follow the language', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const buttons = () => Array.from(element.querySelectorAll('button'), textOf);
    expect(buttons()).toEqual(['Cancel', 'Confirm']);

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    const [cancel, confirm] = buttons();
    for (const [english, polish] of [
      ['Cancel', cancel],
      ['Confirm', confirm],
    ]) {
      expect(polish).not.toBe(english);
      expect(looksLikeTranslationKey(polish)).toBe(false);
      expect(isTranslationIn('pl', polish), `"${polish}" is not a pl.json value`).toBe(true);
    }
    expect(textOf(element.querySelector('h2'))).toBe('Archive account?');
    expect(element.textContent).toContain('This hides it from the list.');
  });
});
