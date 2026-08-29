// Production environment - replaces environment.ts at build time (see the
// `fileReplacements` block in angular.json's "production" configuration).
//
// PLACEHOLDER: no production API has been provisioned yet. Set this to the
// real deployed QuotesApi origin (no trailing slash) before shipping - a
// production build with this value unchanged will not be able to reach any
// backend.
export const environment = {
  production: true,
  apiOrigin: 'https://REPLACE_WITH_PRODUCTION_API_ORIGIN',
};
