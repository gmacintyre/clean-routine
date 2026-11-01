# Clean Routine

A shared cleaning checklist for households. Anyone can sign up for free; the free version shows ads, and a household can pay to go ad-free.

- **Today**: the day's checklist. Anyone in the household can tick things off, and each item shows who did it.
- **Calendar**: a month view. Every weekday has its zone (Monday is Kitchen, and so on), and monthly or seasonal jobs are marked with a dot.
- **Routine**: every chore, grouped by how often it happens. Tap one to rename it, change how often it repeats (daily, weekly, monthly, seasonal or just once), move it to another day, or delete it. You can also add new chores and rename each day's zone here.
- **One-off jobs**: add a job for a single date, from Routine or from any day's checklist.
- **Reminders**: one push notification a day on each phone, listing that day's jobs, in the household's own time zone.
- **Households**: signing up creates a household with a suggested starter routine. Invite the people you live with by link (Settings → Household).
- **Ads and ad-free**: Google AdSense ads on Today, Calendar and Routine, below the content. Ad-free is bought through Stripe and covers the whole household.

It's ASP.NET Core 10 Razor Pages with **no NuGet packages**. Stripe and web push are called over plain HTTPS with .NET's built-in crypto. Sign-in uses a cookie with hashed passwords.

## Why a web app, not a native app

Clean Routine stays an installable web app (PWA) for launch:

- **It already works like an app.** People add it to their home screen, and it gets notifications on Android and on iPhone (iOS 16.4+, from the home screen).
- **Payments cost less.** Stripe takes about 2.9% + 30¢. In-app purchases in the App Store or Google Play take 15% (small business rate) and the stores require them for digital upgrades like ad-free.
- **Ads work without a store.** AdSense runs on websites. AdMob, the mobile ad network, only works inside a native app.
- **One codebase, no review queues, no $99/year Apple fee.**

If you later want store listings, wrap the same site: a Trusted Web Activity for Google Play (keeps web ads and payments, with some Play billing rules) or Capacitor for the App Store (needs in-app purchase and AdMob instead of Stripe and AdSense). That's worth doing once the web version has users.

## Run it locally

Needs the .NET 10 SDK (included with Visual Studio 2026, or from https://dotnet.microsoft.com/download).

```bash
cd src/CleanRoutine
dotnet run
```

Open http://localhost:5103, choose **Get started free** and create an account. Local data goes in `src/CleanRoutine/App_Data/`, which git ignores. Without AdSense settings, the ad spots show a small "go ad-free" card instead.

To try notifications locally, generate keys (see step 3 below) and put them in `appsettings.Development.json` under `Push`. Browsers allow push on `localhost` without HTTPS.

To try password reset locally without email, choose **Forgot your password?**: in Development the reset link is written to the console instead of being emailed.

To try payments locally, use Stripe test keys and the Stripe CLI: `stripe listen --forward-to localhost:5103/api/stripe/webhook` prints a `whsec_…` secret to use as `Billing__StripeWebhookSecret`. Pay with card `4242 4242 4242 4242`.

## Launch checklist (things only the owner can do)

1. **Make this repo private** (GitHub → Settings → General → Danger Zone → Change visibility) before adding any secrets. Secrets go in Azure app settings, never in the repo.
2. **Move the app to the Basic B1 plan and turn on Always On** (about $13/month). The Free plan sleeps, so reminders in other time zones can't go out, has a 60 CPU-minutes/day cap, and can't use a custom domain, which AdSense needs.
3. **Buy a domain** (for example `cleanroutine.app`) and add it to the web app with a free App Service managed certificate.
4. **Google AdSense:** apply with the domain, set up the consent message for EU/UK visitors (AdSense → Privacy & messaging), create three display ad units, and add the ids as app settings (below). The welcome page, privacy policy and terms are public so the review has content to look at.
5. **Stripe:** create an account, a product "Clean Routine ad-free" with a price, a webhook, and turn on the customer portal (below).
6. **Privacy policy and terms:** the app has drafts at `/Privacy` and `/Terms`. Fill in `Site__OperatorName` and `Site__ContactEmail`, and have them reviewed.
7. **Email for password resets:** set up an email service with SMTP (Azure Communication Services Email is the easy choice on Azure; Brevo, Postmark or Mailgun also work), send from your own domain, and add the `Email__…` settings (below). Until then, "Forgot your password?" asks people to email `Site__ContactEmail`.

## Deploy to Azure App Service

### 1. Create the web app

In the Azure portal: **Create a resource → Web App**

- **Publish:** Code
- **Runtime stack:** .NET 10 (LTS)
- **Operating system:** Linux
- **Pricing plan:** **Basic B1** (Free F1 works for testing only; see the launch checklist)

### 2. Turn on these settings

In the web app, open **Settings → Configuration → General settings**:

- **HTTPS Only: On**
- **Always On: On** (B1 and up)

### 3. Generate notification keys

On your computer:

```bash
cd src/CleanRoutine
dotnet run -- generate-vapid-keys
```

It prints a public and a private key. Keep the private key secret and don't commit it.

### 4. Add app settings

In **Settings → Environment variables → App settings**, add these. Each name uses **two underscores** between parts. Click **Apply** to save.

| Name | Value |
|---|---|
| `Household__TimeZone` | Default time zone for new households if the browser doesn't send one, e.g. `America/New_York` |
| `Push__PublicKey` / `Push__PrivateKey` | from step 3 |
| `Push__Subject` | `mailto:` followed by your email |
| `Site__OperatorName` | your name or business name, shown on the privacy policy and terms |
| `Site__ContactEmail` | where people write for help |
| `Site__BaseUrl` | the app's public address, e.g. `https://cleanroutine.app`, used in reset-email links |
| `Email__SmtpHost` / `Email__SmtpPort` | SMTP server and port (587, STARTTLS), e.g. `smtp.azurecomm.net` |
| `Email__SmtpUser` / `Email__SmtpPassword` | SMTP sign-in from your email service |
| `Email__From` | sender address the service allows, e.g. `DoNotReply@cleanroutine.app` |
| `Ads__AdSenseClientId` | `ca-pub-…` from AdSense |
| `Ads__Slots__today` / `Ads__Slots__calendar` / `Ads__Slots__routine` | the ad unit ids (the `data-ad-slot` numbers) |
| `Billing__StripeSecretKey` | `sk_live_…` (or `sk_test_…` while testing) |
| `Billing__StripePriceId` | `price_…` of the ad-free price |
| `Billing__StripeWebhookSecret` | `whsec_…` from the webhook (step 5) |
| `Billing__Mode` | `subscription` for a monthly/yearly price, `payment` for a one-time price |
| `Billing__PriceLabel` | the price as shown in the app, e.g. `$1.99 a month` |
| `Reminders__TriggerKey` | only if you stay on the Free plan and use the wake-up job (step 9) |

`/ads.txt` is served automatically from `Ads__AdSenseClientId`. Without Stripe settings the Ad-free page says "coming soon"; without AdSense settings the ad spots show the "go ad-free" card (set `Ads__HouseAds` to `false` to hide it).

Data is saved under `%HOME%/data/clean-routine/`, App Service's persistent storage, so it survives restarts and redeploys.

### 5. Set up Stripe

1. **Products → Add product:** "Clean Routine ad-free", with a recurring price (suggested $1.99/month or $14.99/year) or a one-time price. Copy the price id into `Billing__StripePriceId`.
2. **Developers → Webhooks → Add endpoint:** `https://your-domain/api/stripe/webhook`, with these events: `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `customer.subscription.created`, `customer.subscription.updated`, `customer.subscription.deleted`. Copy its signing secret into `Billing__StripeWebhookSecret`.
3. **Settings → Billing → Customer portal:** turn it on and allow cancelling. The app's "Manage billing or cancel" button opens it.

### 6. Set up email (for password resets)

The app sends email through SMTP, the standard way programs hand mail to an email service. These steps use Azure Communication Services, which costs a fraction of a cent per email. It takes about 30 minutes, plus waiting for DNS.

**a. Create the email service and a sending domain**

1. In the Azure portal: **Create a resource → Email Communication Services**. Put it in the same resource group as the web app. Data location: United States.
2. Open it, then **Provision domains → Add domain → Custom domain**, and enter your domain (for example `apronstrings.app`).
3. Azure shows DNS records to add at your domain registrar: a **TXT** record to prove you own it, then **SPF** (TXT) and two **DKIM** (CNAME) records so mail isn't marked as spam. Add each one, then click **Verify** next to it. All four need to say Verified. DNS can take from a few minutes to a few hours.
   - Just testing before you have a domain? Choose **Add an Azure managed domain** instead. It's instant, but it sends from a `…azurecomm.net` address, has low sending limits and more often lands in spam.
4. Open the domain → **MailFrom addresses**. `DoNotReply@yourdomain` is there by default. Change the display name to "Apron Strings".

**b. Create the Communication Services resource and connect the domain**

1. **Create a resource → Communication Services**, same resource group, data location United States.
2. Open it → **Email → Domains → Connect domain**, and pick the domain from step a.

**c. Give the app a login for SMTP**

This uses an app registration, like the deploy login.

1. **Microsoft Entra ID → App registrations → New registration**. Name it `ApronStrings-Email` and keep the defaults.
2. In it, open **Certificates & secrets → Client secrets → New client secret**, set it to expire in 24 months, and **copy the Value straight away** (it's shown only once). This is the SMTP password. Put a reminder in your calendar to make a new one before it expires.
3. Open the **Communication Services** resource → **Access control (IAM) → Add → Add role assignment**. Choose the role **Communication and Email Service Owner**, then **User, group, or service principal → Select members**, and pick `ApronStrings-Email`. Then **Review + assign**.
4. Still in the Communication Services resource, open **SMTP Usernames → Add SMTP Username**. Pick `ApronStrings-Email` and enter `DoNotReply@yourdomain` as the username. Wait until it says **Ready to use**. If the app isn't in the list, the role from step 3 hasn't applied yet; wait a few minutes.

**d. Add the app settings** (web app → Environment variables → App settings, then Apply)

| Name | Value |
|---|---|
| `Email__SmtpHost` | `smtp.azurecomm.net` |
| `Email__SmtpPort` | `587` |
| `Email__SmtpUser` | the SMTP username from c4 |
| `Email__SmtpPassword` | the client secret from c2 |
| `Email__From` | `DoNotReply@yourdomain` |
| `Site__BaseUrl` | `https://yourdomain` |

**e. Test it.** Sign out, choose **Forgot your password?**, and enter your own email. If nothing arrives within a few minutes, check spam, then **Log stream** in the web app for a line starting "Couldn't send the password reset email":

| Error mentions | Fix |
|---|---|
| `5.7.57` or authentication failed | Wrong SMTP username or password, or the client secret expired |
| `5.3.5` sender's username is invalid | `Email__From` isn't a MailFrom address of a connected domain |
| `5.7.1` not allowed / domain not linked | The domain isn't connected (step b2) or isn't verified |

Prefer something simpler? Any SMTP email service works with the same settings. Brevo, for example, has a free tier: you verify your domain in its dashboard and it gives you an SMTP host, login and key to put in the `Email__…` settings.

### 7. Automatic deploys from GitHub

Deploys use the workflow Visual Studio created (`.github/workflows/CleanRoutine20261029180254.yml`) and run on every push to `main`. GitHub signs in to Azure through a Microsoft Entra **app registration** with no stored password, the same way the tea recipe app does. These pieces have to be in place:

1. **App registration:** Microsoft Entra ID → App registrations → **CleanRoutine**.
2. **Federated credential** on that registration (Certificates & secrets → Federated credentials), for GitHub Actions with the entity type **Environment** and the name `CleanRoutine20261029180254_Env`. This repo sends the subject with IDs, so it must read exactly:
   `repo:gmacintyre@3603168/clean-routine@1438788421:environment:CleanRoutine20261029180254_Env`
3. **Role:** the registration's service principal needs a role (for example Contributor) in **Access control (IAM)** on the subscription or the app's resource group. Add it under "User, group, or service principal", not Managed identity.
4. **GitHub environment variables** in Settings → Environments → `CleanRoutine20261029180254_Env`:
   - `CleanRoutine20261029180254_CLIENT_ID_EC6B`: the registration's Application (client) ID
   - `CleanRoutine20261029180254_TENANT_ID_EC6B`: the Directory (tenant) ID
   - `CleanRoutine20261029180254_SUBSCRIPTION_ID_EC6B`: the Subscription ID

If the deploy fails at **Azure Login**, the step before it lists which variables it found. Then read the error:

| Error | Fix |
|---|---|
| "Not all values are present" | A variable in step 4 is missing or misnamed |
| AADSTS70025 "no configured federated identity credentials" | Step 2 is missing, or the client ID points at a different identity |
| AADSTS700213 "No matching federated identity record" | The subject in step 2 doesn't match. Copy it from the error message. |
| "No subscriptions found" | Step 3: the role is missing or hasn't applied yet (wait about 5 minutes) |

### 8. Set up each phone

1. Open the site and create an account, or follow an invite link.
2. **iPhone:** in Safari, tap **Share → Add to Home Screen**, then open the app from the Home Screen. iOS only allows notifications for Home Screen apps (iOS 16.4 or later).
   **Android:** in Chrome, tap the menu and choose **Add to Home screen** or **Install app**.
3. Go to **Settings → Turn on notifications**, allow them, and tap **Send a test**.

### 9. Free plan only: the morning wake-up job

On B1 with Always On, skip this and you can delete `.github/workflows/reminder.yml`.

On the Free plan the app sleeps, so the **Daily reminder wake-up** workflow calls `POST /api/reminders/run` every 15 minutes from about 5 AM to noon Eastern. Each call wakes the app and sends any reminders that are due. In GitHub **Settings → Secrets and variables → Actions**, add the secret `REMINDER_TRIGGER_KEY` (same value as `Reminders__TriggerKey`) and the variable `CLEAN_ROUTINE_URL`. The run log ends with `nothing-due`, `sent-N-of-M` (households reached of those due) or `push-not-configured`. This only suits households in US time zones with morning reminder times.

## Where things live

| Path | What it does |
|---|---|
| `Domain/Schedule.cs` | Which tasks are due on a date (daily, weekly, nth weekday of the month, seasonal in Jan/Apr/Jul/Oct, one-off) |
| `Domain/SeedRoutine.cs` | The starter routine every new household gets |
| `Pages/Chore.cshtml` | Add or edit a chore |
| `Services/AccountStore.cs` | Accounts and households (`accounts.json`) |
| `Services/HouseholdStore.cs` | Each household's chores and ticks (`households/{id}.json`) |
| `Services/Storage.cs` | Shared JSON file handling: locking, atomic saves, daily backups |
| `Services/DataMigration.cs` | One-time move from the original single `household.json` |
| `Services/AccountService.cs` | Sign-up, invites, password reset, joining and deleting accounts |
| `Services/EmailSender.cs` | Sends email over SMTP |
| `Pages/Account/Forgot.cshtml`, `Reset.cshtml` | Forgot password: one-time links that expire after an hour and sign out other devices |
| `Billing/StripeBilling.cs` | Stripe Checkout, customer portal and webhook |
| `Pages/Shared/_Ad.cshtml` | The ad spot (AdSense unit or "go ad-free" card) |
| `Pages/Premium.cshtml` | The ad-free page |
| `Pages/Welcome.cshtml`, `Privacy.cshtml`, `Terms.cshtml` | Public pages |
| `Push/WebPushCrypto.cs` | VAPID signing and payload encryption |
| `Push/DailyReminderService.cs` | Sends each household's daily reminder |
| `wwwroot/sw.js`, `manifest.webmanifest` | Home-screen app and notification display |

## Data and backups

```
data/clean-routine/
  accounts.json              every account and household (plan, time zone, reminder time, invite code)
  households/{id}.json       one household's chores, zones, ticks and phones
  backups/YYYY-MM-DD/        copies of each file from before its first change that day (14 days kept)
  household.json.migrated    the original single-household file, kept after the one-time move
```

The first time this version starts, it moves the original `household.json` into this layout. Both original accounts keep their passwords, chores and history, end up in one household called "Our home" (rename it in Settings), and that household is ad-free for good.

To restore a backup, stop the app, copy the file from `backups/` over the live one (through Kudu or the SSH console), and start the app again.

This layout comfortably handles thousands of households on one server. If the app grows past that, or needs more than one server, move `AccountStore` and `HouseholdStore` to Azure SQL or Cosmos DB; nothing else reads the files directly.
