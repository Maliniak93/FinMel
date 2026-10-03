import { ComponentFixture, TestBed } from '@angular/core/testing';

import { isTranslationIn, looksLikeTranslationKey, textOf } from '../../../testing/i18n';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../../core/i18n/language';
import { provideI18nTesting } from '../../core/i18n/testing';
import { NotFound } from './not-found';

describe('NotFound', () => {
  let component: NotFound;
  let fixture: ComponentFixture<NotFound>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NotFound],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(NotFound);
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

  it('renders in Polish', async () => {
    const message = () => textOf((fixture.nativeElement as HTMLElement).querySelector('p'));
    expect(message()).toBe('Page not found.');

    await TestBed.inject(LanguageService).setLanguage('pl');
    await fixture.whenStable();

    expect(message()).not.toBe('Page not found.');
    expect(looksLikeTranslationKey(message())).toBe(false);
    expect(isTranslationIn('pl', message())).toBe(true);
  });
});
