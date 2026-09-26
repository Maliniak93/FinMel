import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { THEME_STORAGE_KEY, ThemeService } from '../../core/theme/theme';
import { Shell } from './shell';

describe('Shell', () => {
  let component: Shell;
  let fixture: ComponentFixture<Shell>;
  let themeService: ThemeService;

  beforeEach(async () => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.style.removeProperty('color-scheme');

    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(Shell);
    component = fixture.componentInstance;
    themeService = TestBed.inject(ThemeService);
    await fixture.whenStable();
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.style.removeProperty('color-scheme');
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('cycles the theme and reflects it on <html> when the toggle is clicked', () => {
    expect(themeService.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);

    const toggle: HTMLButtonElement = fixture.nativeElement.querySelectorAll('button')[1];
    toggle.click();
    fixture.detectChanges();

    expect(themeService.preference()).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(document.documentElement.style.getPropertyValue('color-scheme')).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
  });

  // term-deposits: a "Deposits" nav item leads to the cross-portfolio Deposits page.
  it('offers a Deposits nav item linking to /deposits', () => {
    const link = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('mat-nav-list a'),
    ).find((a) => (a.textContent ?? '').includes('Deposits'));

    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/deposits');
  });
});
