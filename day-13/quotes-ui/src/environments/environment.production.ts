// Production environment - replaces environment.ts at build time (see the
// `fileReplacements` block in angular.json's "production" configuration).
//
// QuotesApi deployed to Azure App Service (Linux, F1 free tier) - see
// day-17/README.md for the original resource details, CORS wiring, and
// the SQLite-persistence caveat that comes with running on a free plan.
// day-27: rebuilt on a new subscription ("Azure for Students") after the
// old one expired - new region (eastasia, not centralus - the new
// subscription's policy disallows centralus), new resource names
// (globally-unique names like the SQL server and Web App were still
// reserved by the old, now-inaccessible subscription).
export const environment = {
  production: true,
  apiOrigin: 'https://quotesapi-thinkschool2.azurewebsites.net',
};
