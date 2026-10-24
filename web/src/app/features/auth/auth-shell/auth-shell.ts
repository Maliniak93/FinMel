import { Component, input } from '@angular/core';

import { LanguageSwitch } from '../../../shared/language-switch/language-switch';

@Component({
  selector: 'app-auth-shell',
  imports: [LanguageSwitch],
  templateUrl: './auth-shell.html',
  styleUrl: './auth-shell.scss',
})
export class AuthShell {
  readonly title = input.required<string>();
}
