import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig } from './app/app.config';

// Addresses of the AngularJS application looked like /#!/orders/3, and they are in bookmarks, e-mails and wikis.
// The Angular router uses /orders/3. Without this line an old address opens the list, silently.
if (location.hash.startsWith('#!/')) history.replaceState(null, '', location.hash.slice(2));

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
