// day-28: the actual "prod" infra environment - not to be confused with
// environment.production.ts, whose name means "Angular's optimized
// build mode" and which has always pointed at what we call "dev" infra
// (quotesapi-thinkschool2). That naming collision predates this file and
// isn't fixed here to avoid touching the already-working dev build.
//
// Swapped in by the `prod-live` Angular build configuration (angular.json)
// - a duplicate of `production`'s optimization/budgets/hashing settings
// with only fileReplacements overridden. Duplicated rather than
// inherited: the esbuild-based @angular/build:application builder used
// here rejects an `extends` key in configurations (confirmed by trying
// it - a real schema validation error, not a style choice).
//
// Shares dev's SQL database (see day-23/params/prod.bicepparam's
// deploySql comment) - this is a separate App Service and Static Web App
// hitting the same data, not an isolated production environment in the
// way that name would mean for a real product.
export const environment = {
  production: true,
  // "-prod2", not "-prod": quotesapi-thinkschool-prod was already taken
  // by the old, now-inaccessible subscription (same global-name pattern
  // day-27 hit) - see day-23/params/prod.bicepparam's webAppName.
  apiOrigin: 'https://quotesapi-thinkschool-prod2.azurewebsites.net',
};
