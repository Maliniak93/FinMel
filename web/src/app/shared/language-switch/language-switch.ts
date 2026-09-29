import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { LanguageService, type Language } from '../../core/i18n/language';

// Each language is offered under its own name, whatever the active language — so these are not
// translation keys. A third language needs only its JSON file, an `availableLangs` entry and a row
// here.
const LANGUAGE_OPTIONS: readonly { id: Language; name: string }[] = [
  { id: 'en', name: 'English' },
  { id: 'pl', name: 'Polski' },
];

// The language menu: in the shell toolbar, and on the login and register pages (outside the shell).
@Component({
  selector: 'app-language-switch',
  imports: [MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslocoPipe],
  templateUrl: './language-switch.html',
})
export class LanguageSwitch {
  protected readonly languageService = inject(LanguageService);
  protected readonly options = LANGUAGE_OPTIONS;

  protected select(language: Language): void {
    void this.languageService.setLanguage(language);
  }
}
