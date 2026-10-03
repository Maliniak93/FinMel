import { EnvironmentProviders, importProvidersFrom } from '@angular/core';
import { TranslocoTestingModule } from '@jsverse/transloco';

import en from '../../../i18n/en.json';
import pl from '../../../i18n/pl.json';
import { DEFAULT_LANGUAGE, LANGUAGES } from './language';

export function provideI18nTesting(): EnvironmentProviders {
  return importProvidersFrom(
    TranslocoTestingModule.forRoot({
      langs: { en, pl },
      translocoConfig: {
        availableLangs: [...LANGUAGES],
        defaultLang: DEFAULT_LANGUAGE,
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
      },
      preloadLangs: true,
    }),
  );
}
