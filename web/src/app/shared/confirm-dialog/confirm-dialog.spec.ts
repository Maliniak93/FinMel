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
    title: 'Archive portfolio?',
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
    expect(textOf(element.querySelector('h2'))).toBe('Archive portfolio?');
    expect(element.textContent).toContain('This hides it from the list.');
  });
});
