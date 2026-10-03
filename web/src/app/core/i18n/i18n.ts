import {
  EnvironmentProviders,
  Injectable,
  inject,
  isDevMode,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { provideTransloco, type Translation, type TranslocoLoader } from '@jsverse/transloco';

import {
  DEFAULT_LANGUAGE,
  LANGUAGES,
  LanguageService,
  isLanguage,
  type Language,
} from './language';

// One dynamic import() per language: the app has no HttpClient and serves nothing from public/.
const TRANSLATION_FILES: Record<Language, () => Promise<Translation>> = {
  en: () => import('../../../i18n/en.json').then((file) => file.default),
  pl: () => import('../../../i18n/pl.json').then((file) => file.default),
};

@Injectable({ providedIn: 'root' })
class TranslationFileLoader implements TranslocoLoader {
  getTranslation(lang: string): Promise<Translation> {
    return TRANSLATION_FILES[isLanguage(lang) ? lang : DEFAULT_LANGUAGE]();
  }
}

export function provideI18n(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideTransloco({
      config: {
        availableLangs: [...LANGUAGES],
        defaultLang: DEFAULT_LANGUAGE,
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
        missingHandler: { logMissingKey: true },
      },
      loader: TranslationFileLoader,
    }),
    provideAppInitializer(() => inject(LanguageService).restore()),
  ]);
}
