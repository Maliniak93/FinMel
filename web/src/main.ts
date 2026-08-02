import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { configureApiClients } from './app/core/api-clients';

configureApiClients();

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
