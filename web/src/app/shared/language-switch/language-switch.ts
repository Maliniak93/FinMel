import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { LanguageService, type Language } from '../../core/i18n/language';

const LANGUAGE_OPTIONS: readonly { id: Language; name: string }[] = [
  { id: 'en', name: 'English' },
  { id: 'pl', name: 'Polski' },
];

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
