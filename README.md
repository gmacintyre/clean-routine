# Clean Routine

A small private web app for our household cleaning routine:

- **Today**: the day's checklist. Either of us can tick things off, and each item shows who did it.
- **Calendar**: a month view. Every weekday has its zone (Monday is Kitchen, and so on), and monthly or seasonal jobs are marked with a dot.
- **Routine**: every chore, grouped by how often it happens. Tap one to rename it, change how often it repeats (daily, weekly, monthly, seasonal or just once), move it to another day, or delete it. You can also add new chores and rename each day's zone here.
- **One-off jobs**: add a job for a single date, from Routine or from any day's checklist.
- **Reminders**: one push notification a day on each phone, listing that day's jobs.

It's ASP.NET Core 10 Razor Pages with **no NuGet packages**. Data is one JSON file, and the app keeps a daily backup of it for 14 days. Sign-in uses a cookie with hashed passwords, and only the email addresses you list can create an account. Web push (VAPID + aes128gcm) uses .NET's built-in crypto.

## Run it locally

Needs the .NET 10 SDK (included with Visual Studio 2026, or from https://dotnet.microsoft.com/download).

```bash
cd src/CleanRoutine
dotnet run
```

Open http://localhost:5103 and create an account with `you@example.com` (the development settings allow `you@example.com` and `partner@example.com`). Local data goes in `src/CleanRoutine/App_Data/`, which git ignores.

To try notifications locally, generate keys (see step 3 below) and put them in `appsettings.Development.json` under `Push`. Browsers allow push on `localhost` without HTTPS.

## Deploy to Azure App Service (Free plan)

The app runs on Azure's **Free (F1)** plan, which costs nothing. It gets its own plan, separate from the tea recipe app's B1 plan.

The Free plan puts the app to sleep after about 20 minutes with no visits, so the first visit after that takes 10–30 seconds to load. A sleeping app also can't send the morning reminder. A scheduled GitHub Actions job solves this (step 7): it wakes the app every 15 minutes through the morning, and the app sends the reminder once its time has passed.

### 1. Create the web app

In the Azure portal: **Create a resource → Web App**

- **Publish:** Code
- **Runtime stack:** .NET 10 (LTS)
- **Operating system:** Linux
- **Pricing plan:** click **Create new**, then under **Explore pricing plans** choose **Free F1**

### 2. Turn on these settings

In the new web app, open **Settings → Configuration → General settings**:

- **HTTPS Only: On**

Always On isn't available on the Free plan. The wake-up job in step 7 takes its place.

### 3. Generate notification keys

On your computer:

```bash
cd src/CleanRoutine
dotnet run -- generate-vapid-keys
```

It prints a public and a private key. Keep the private key secret and don't commit it.

### 4. Add app settings

In **Settings → Environment variables → App settings**, add:

| Name | Value |
|---|---|
| `Household__AllowedEmails__0` | your email |
| `Household__AllowedEmails__1` | your husband's email |
| `Household__TimeZone` | `America/New_York` |
| `Push__PublicKey` | public key from step 3 |
| `Push__PrivateKey` | private key from step 3 |
| `Push__Subject` | `mailto:` followed by your email |
| `Reminders__TriggerKey` | a long random password you make up; it's also used in step 7 |

Data is saved to `%HOME%/data/clean-routine/household.json`. That's App Service's persistent storage, so it survives restarts and redeploys.

### 5. Automatic deploys from GitHub

Deploys use the workflow Visual Studio created when publishing (`.github/workflows/CleanRoutine20261029180254.yml`), like the tea recipe app's. It signs in to Azure with a managed identity instead of a stored password, and runs on every push to `main`.

It needs three variables in the repo's **Settings → Environments → CleanRoutine20261029180254_Env**:

- `CleanRoutine20261029180254_CLIENT_ID_EC6B`: the **Client ID** of the managed identity Visual Studio created (search for "Managed Identities" in the portal; it's named after the app)
- `CleanRoutine20261029180254_TENANT_ID_EC6B`: your **Directory (tenant) ID**, the same value as the tea app's `..._TENANT_ID_...` variable
- `CleanRoutine20261029180254_SUBSCRIPTION_ID_EC6B`: your **Subscription ID**, the same value as the tea app's

If the **Azure Login** step fails with "Not all values are present", one of these is missing.

### 6. Set up each phone

1. Open the site and create your account. Each listed email can sign up once.
2. **iPhone:** in Safari, tap **Share → Add to Home Screen**, then open the app from the Home Screen. iOS only allows notifications for Home Screen apps (iOS 16.4 or later).
   **Android:** in Chrome, tap the menu and choose **Add to Home screen** or **Install app**.
3. Go to **Reminders → Turn on notifications**, allow them, and tap **Send a test**.

The reminder time is shared by both of you and defaults to 7:30 AM.

### 7. Turn on the morning wake-up job

The **Daily reminder wake-up** workflow (`.github/workflows/reminder.yml`) calls the app every 15 minutes from 5 AM to about noon Eastern. Each call wakes the app and sends the day's reminder once the reminder time has passed. Later calls that day do nothing.

In this GitHub repo, go to **Settings → Secrets and variables → Actions**:

- Under **Secrets**, add `REMINDER_TRIGGER_KEY` with the same value as the `Reminders__TriggerKey` app setting.
- Under **Variables**, add `CLEAN_ROUTINE_URL` with the app's address, like `https://your-app-name.azurewebsites.net`.

To check it, open the **Actions** tab, choose **Daily reminder wake-up → Run workflow**, and open the run. Its log ends with a status:

| Status | Meaning |
|---|---|
| `sent` | The reminder went out |
| `already-sent` | It already went out today |
| `not-yet` | The reminder time hasn't come yet |
| `no-devices` | No phone has notifications turned on |
| `disabled` | Reminders are switched off in the app |
| `push-not-configured` | The `Push__` keys are missing |

A 401 error means the key in GitHub doesn't match the app setting.

Things to know:

- **Pick a reminder time between 5:00 AM and 11:30 AM.** The job only runs in the morning. A reminder can arrive up to about 20 minutes after the time you set, because the job runs every 15 minutes and GitHub sometimes starts scheduled jobs a few minutes late.
- **In a public repo, GitHub turns off scheduled jobs after 60 days with no commits.** It emails you first, and you can turn the job back on from the Actions tab. A private repo doesn't have this limit, but each run counts against the free 2,000 Actions minutes a month. This job uses about 960 a month.
- **The wake-up calls also keep the app awake in the morning,** so it loads quickly then.

To move to a paid plan later, scale the plan up to B1 and turn on **Always On**. The app's own timer then sends the reminder at any time of day, and you can delete the wake-up job.

## Where things live

| Path | What it does |
|---|---|
| `Domain/Schedule.cs` | Which tasks are due on a date (daily, weekly, nth weekday of the month, seasonal in Jan/Apr/Jul/Oct, one-off) |
| `Pages/Chore.cshtml` | Add or edit a chore |
| `Domain/SeedRoutine.cs` | The starting routine, loaded on first run |
| `Services/HouseholdStore.cs` | JSON file storage with a lock, atomic saves and daily backups |
| `Services/AccountService.cs` | Sign-up allowlist and password checks |
| `Push/WebPushCrypto.cs` | VAPID signing and payload encryption |
| `Push/DailyReminderService.cs` | Sends the daily reminder (from the app's timer or the wake-up link) |
| `.github/workflows/reminder.yml` | Morning wake-up job for the Free plan |
| `wwwroot/sw.js`, `manifest.webmanifest` | Home-screen app and notification display |

## Backups

The app copies `household.json` to `backups/household-YYYY-MM-DD.json` once a day and keeps 14 days of copies. To restore one, stop the app, copy a backup over `household.json` (through Kudu or the SSH console), and start the app again.
