# Concord Docs Worker

Serves the versioned docs from Cloudflare R2.

## Setup

1. Create the R2 bucket:

```bash
npx wrangler r2 bucket create concord-docs
```

2. Deploy the worker:

```bash
cd worker
npm install
npx wrangler deploy
```

3. Add a custom domain (`concordlib.dev`) in the Cloudflare dashboard under
   Workers > concord-docs > Settings > Domains & Routes.

4. Set these GitHub repo secrets for CI deployment:

   - `CLOUDFLARE_API_TOKEN` - API token with R2 + Workers permissions
   - `CLOUDFLARE_ACCOUNT_ID` - your Cloudflare account ID
   - `CORE_CHECKOUT_TOKEN` - token used when the Core repo needs an authenticated checkout

## How it works

Versions are driven by git branches, not tags. A push to `main` deploys under
the `unstable/` prefix, and a push to a `vX.Y` branch deploys under `vX.Y/`.
A deploy of the highest `vX.Y` branch also mirrors that build to `latest/`
(`scripts/latest-version.py` finds the highest branch).

The Worker maps the first URL segment to an R2 prefix. `/` redirects to
`/latest/`, and a path with no version prefix serves from `latest`.
`versions.json` at the bucket root lists every version for the theme's
dropdown; `scripts/build-versions.py` rebuilds it on deploy.

## Basic auth

If both `DOCS_USER` and `DOCS_PASS` are set, the Worker requires basic auth.
Remove both secrets to make the site public again:

```bash
npx wrangler secret delete DOCS_USER
npx wrangler secret delete DOCS_PASS
```

The Worker reads secret changes without a code change.
