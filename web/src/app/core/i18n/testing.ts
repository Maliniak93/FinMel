import { EnvironmentProviders, importProvidersFrom } from '@angular/core';
import { TranslocoTestingModule } from '@jsverse/transloco';

import en from '../../../i18n/en.json';
import pl from '../../../i18n/pl.json';
import { DEFAULT_LANGUAGE, LANGUAGES } from './language';

// Test-only: Transloco with the real en.json and pl.json, both loaded synchronously before the
// first render, English active. Add it to the TestBed `providers` of every spec whose component
// renders translated text. A spec that switches to Polish switches back to English afterwards —
// specs share one worker (isolate: false).
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
