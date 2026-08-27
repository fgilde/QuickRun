# Store credentials

Every distribution step in `.github/workflows/release.yml` skips itself when its credential is
missing, and says so in the log. A release therefore never fails because a store is not set up -
it just does not reach that store. This page is what to do about it: where each credential comes
from, what to call it, and what still needs a person.

## Where secrets go

All of them live in **this repository**, under
[Settings → Secrets and variables → Actions](https://github.com/fgilde/QuickRun/settings/secrets/actions),
or from a terminal:

```bash
gh secret set NAME --repo fgilde/QuickRun
```

Without `--body` the command asks for the value and reads it from the terminal, so it lands neither
in your shell history nor in a screenshot. Prefer that for anything secret. To see what is set
(names and dates only - GitHub never gives a value back):

```bash
gh secret list --repo fgilde/QuickRun
```

## What each channel needs

| Channel | Secrets | Cost |
|---|---|---|
| [Homebrew tap](#homebrew-tap) | `TAP_TOKEN` | free |
| [winget](#winget) | `WINGET_TOKEN` | free |
| [Chrome Web Store](#chrome-web-store) | `CHROME_EXTENSION_ID`, `CHROME_CLIENT_ID`, `CHROME_CLIENT_SECRET`, `CHROME_REFRESH_TOKEN` | 5 USD once |
| [Firefox Add-ons](#firefox-add-ons) | `AMO_JWT_ISSUER`, `AMO_JWT_SECRET` | free |
| [Edge Add-ons](#edge-add-ons) | `EDGE_PRODUCT_ID`, `EDGE_CLIENT_ID`, `EDGE_API_KEY` | free |

The website and the GitHub Release need nothing: they run on `GITHUB_TOKEN`, which Actions provides
by itself.

---

## Homebrew tap

**Optional.** The tap keeps itself current without this: `fgilde/homebrew-tap` runs a scheduled
workflow that pulls `quickrun.rb` and `quickrun-cask.rb` from the newest release every hour. The
token only makes it immediate instead of within the hour.

Create a **fine-grained personal access token** at
<https://github.com/settings/personal-access-tokens/new>:

- Repository access: only `fgilde/homebrew-tap`
- Permissions: **Contents → Read and write**

```bash
gh secret set TAP_TOKEN --repo fgilde/QuickRun
```

`GITHUB_TOKEN` cannot be used here - a workflow's own token may not write to another repository.

> **If it is ever missing for long:** GitHub disables scheduled workflows in a repository with no
> activity for 60 days. Two months without a release would put the tap's own sync to sleep, and the
> token is what makes that harmless.

## winget

Create a **classic personal access token** at <https://github.com/settings/tokens/new> with the
`public_repo` scope. It is used to fork `microsoft/winget-pkgs` and open a pull request.

```bash
gh secret set WINGET_TOKEN --repo fgilde/QuickRun
```

**The first submission is manual.** `wingetcreate update` needs the package to already exist in the
winget catalogue. Run `wingetcreate new` once with the release URLs, or open the pull request by
hand. Every submission is merged by a Microsoft moderator, which takes one to seven days - winget
therefore lags a release rather than tracking it.

## Chrome Web Store

Register once at the [developer dashboard](https://chrome.google.com/webstore/devconsole)
(one-off 5 USD fee) and publish the extension by hand. That first upload is what allocates the
extension id; there is no API for creating a listing.

Then create OAuth credentials following
[Using the Chrome Web Store Publish API](https://developer.chrome.com/docs/webstore/using-api):

| Secret | Where it comes from |
|---|---|
| `CHROME_EXTENSION_ID` | the dashboard URL of your published item |
| `CHROME_CLIENT_ID` | Google Cloud console, OAuth client of type *Desktop app* |
| `CHROME_CLIENT_SECRET` | the same OAuth client |
| `CHROME_REFRESH_TOKEN` | exchanged once, by hand, from an authorisation code |

```bash
gh secret set CHROME_EXTENSION_ID --repo fgilde/QuickRun
gh secret set CHROME_CLIENT_ID --repo fgilde/QuickRun
gh secret set CHROME_CLIENT_SECRET --repo fgilde/QuickRun
gh secret set CHROME_REFRESH_TOKEN --repo fgilde/QuickRun
```

The refresh token does not expire on a schedule, but it is revoked if the OAuth client or the
Google account's permissions change.

## Firefox Add-ons

Generate API credentials at <https://addons.mozilla.org/developers/addon/api/key/> →
**Generate new credentials**:

| Secret | Field on that page |
|---|---|
| `AMO_JWT_ISSUER` | **JWT issuer**, looks like `user:12345678:123` |
| `AMO_JWT_SECRET` | **JWT secret**, a long hex string |

```bash
gh secret set AMO_JWT_ISSUER --repo fgilde/QuickRun
gh secret set AMO_JWT_SECRET --repo fgilde/QuickRun
```

**The secret is shown once.** If it is lost, generate new credentials - the old pair stops working.

Two things about the upload itself:

- The add-on must already be **listed**, with the id from `browser_specific_settings.gecko.id` in
  the manifest. `web-ext` uploads a new version to an existing listing; it does not create one.
- The version must be higher than any already submitted, or AMO answers 409.

The workflow passes `--approval-timeout 0`, so it uploads and stops. Waiting for a listed add-on's
review means waiting for a human, which is hours or days - long past any sensible job timeout.

## Edge Add-ons

The trap here is where the credentials live: **not** under the extension, but under the programme.
Being inside your extension at `…/microsoftedge/<product-id>/listings` is one level too deep.

1. Open the [Partner Center dashboard](https://partner.microsoft.com/dashboard/microsoftedge/publishapi).
2. In the left navigation under **Microsoft Edge**, select **Publish API**.
3. Next to *"enable the new experience"*, press **Enable**. That switches from v1 (access tokens,
   retired at the end of 2024) to v1.1, which is what this workflow speaks.
4. Press **Create API credentials**. This takes a few minutes.

| Secret | Where it comes from |
|---|---|
| `EDGE_PRODUCT_ID` | *Microsoft Edge → Overview → your extension → Extension identity → Product ID*. Also the GUID in the dashboard URL. |
| `EDGE_CLIENT_ID` | the **Publish API** page, after creating credentials |
| `EDGE_API_KEY` | the same page. **Shown once.** |

```bash
gh secret set EDGE_PRODUCT_ID --repo fgilde/QuickRun
gh secret set EDGE_CLIENT_ID --repo fgilde/QuickRun
gh secret set EDGE_API_KEY --repo fgilde/QuickRun
```

**The API cannot publish a new extension**, only update one that is already in the store - the
Microsoft documentation says so outright. Until the first listing is live by hand, the workflow
step runs, uploads, and is turned away.

Check whether the credentials work without changing anything - `404` means authenticated, `401`
means the key is wrong:

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "Authorization: ApiKey $EDGE_API_KEY" -H "X-ClientID: $EDGE_CLIENT_ID" \
  "https://api.addons.microsoftedge.microsoft.com/v1/products/$EDGE_PRODUCT_ID/submissions/draft/package/operations/00000000-0000-0000-0000-000000000000"
```

Check whether the extension is actually live - `302` means yes, `404` means it is not rolled out,
whatever the dashboard says. `<crx-id>` is the **CRX ID** from *Extension identity*, which is not
the same thing as the product ID:

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  "https://edge.microsoft.com/extensionwebstorebase/v1/crx?response=redirect&acceptformat=crx3&x=id%3D<crx-id>%26installsource%3Dondemand%26uc"
```

The store's own detail page is no help for this: it is a single-page application and answers `200`
for any id at all, including ones that do not exist.

---

## What always needs a person, once

- **A developer account per store**, each with an agreement to accept.
- **The first submission.** Chrome needs one manual upload to allocate an id; Edge and Firefox
  cannot create a listing over the API at all. Every store reviews a first listing by hand.
- **The listing itself** - description, category, screenshots, the privacy declaration. Those live
  in each store's dashboard, not in this repository. See [store-listing.md](store-listing.md) for
  the text we submit.
- **The first winget pull request**, and a moderator for every one after it.

## When a store link goes live

Add its URL to `site/.vitepress/theme/stores.js`. The extension cards on the download page then
lead with an Install button instead of "store review pending". Until then they stay honest: a
button pointing at a listing that is still in review is worse than no button.
