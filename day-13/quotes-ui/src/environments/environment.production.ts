// Production environment - replaces environment.ts at build time (see the
// `fileReplacements` block in angular.json's "production" configuration).
//
// QuotesApi deployed to Azure App Service (Linux, F1 free tier) - see
// day-17/README.md for the resource details, CORS wiring, and the
// SQLite-persistence caveat that comes with running on a free plan.
export const environment = {
  production: true,
  apiOrigin: 'https://quotesapi-thinkschool.azurewebsites.net',
};
