import { ComponentFixture, TestBed } from '@angular/core/testing';

import { openLanguageMenu, textOf } from '../../../testing/i18n';
import { LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { LanguageSwitch } from './language-switch';

describe('LanguageSwitch variants', () => {
  let fixture: ComponentFixture<LanguageSwitch>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LanguageSwitch],
      providers: [provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(LanguageSwitch);
  });

  afterEach(async () => {
    await TestBed.inject(LanguageService).setLanguage('en');
  });

  async function render(variant: 'code' | 'icon'): Promise<HTMLButtonElement> {
    fixture.componentRef.setInput('variant', variant);
    fixture.detectChanges();
    await fixture.whenStable();

    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')!;
  }

  it('shows the language code as text in the code variant and opens the menu', async () => {
    const trigger = await render('code');

    expect(textOf(trigger)).toBe('EN');
    expect(trigger.querySelector('mat-icon')).toBeNull();
    expect(trigger.getAttribute('aria-label')).toBe('Language');

    const items = await openLanguageMenu(fixture);
    expect(items.map((item) => item.getAttribute('role'))).toEqual([
      'menuitemradio',
      'menuitemradio',
    ]);
  });

  it('shows the code of the active language', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');
    const trigger = await render('code');

    expect(textOf(trigger)).toBe('PL');
  });

  it('shows the translate icon in the icon variant and opens the same menu', async () => {
    const trigger = await render('icon');

    expect(textOf(trigger.querySelector('mat-icon'))).toBe('translate');
    expect(textOf(trigger)).not.toContain('EN');
    expect(trigger.getAttribute('aria-label')).toBe('Language');

    const items = await openLanguageMenu(fixture);
    expect(items.map((item) => item.getAttribute('role'))).toEqual([
      'menuitemradio',
      'menuitemradio',
    ]);
  });
});
