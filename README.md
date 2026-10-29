# Clean Routine

A small private web app for our household cleaning routine:

- **Today**: the day's checklist. Either of us can tick things off, and each item shows who did it.
- **Calendar**: a month view. Every weekday has its zone (Monday is Kitchen, and so on), and monthly or seasonal jobs are marked with a dot.
- **Routine**: edit the daily tasks, each day's zone and tasks, and which week and day the monthly and seasonal jobs land on.
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

## Deploy to Azure App Service

This runs as a second web app on the same App Service plan as the tea recipe app, so it usually adds no hosting cost.

### 1. Create the web app

In the Azure portal: **Create a resource → Web App**

- **Publish:** Code
- **Runtime stack:** .NET 10 (LTS)
- **Operating system and region:** the same as the existing plan
- **Pricing plan:** pick the tea app's existing App Service plan

### 2. Turn on these settings

In the new web app, open **Settings → Configuration → General settings**:

- **Always On: On.** Without it the app goes to sleep and the daily reminder doesn't go out. This needs a Basic (B1) plan or higher; the Free and Shared tiers don't offer it.
- **HTTPS Only: On**
- **SCM Basic Auth Publishing Credentials: On.** The GitHub deploy below uses a publish profile, which needs this.

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

Data is saved to `%HOME%/data/clean-routine/household.json`. That's App Service's persistent storage, so it survives restarts and redeploys.

### 5. Connect GitHub for automatic deploys

1. In the web app's **Overview**, click **Download publish profile**.
2. In this GitHub repo, go to **Settings → Secrets and variables → Actions**.
   - Under **Secrets**, add `AZURE_WEBAPP_PUBLISH_PROFILE` and paste the whole publish profile file.
   - Under **Variables**, add `AZURE_WEBAPP_NAME` with the web app's name.
3. Push to `main`, or run the **Build and deploy to Azure** workflow from the Actions tab.

Until those two values are set, the workflow only builds and skips the deploy.

### 6. Set up each phone

1. Open the site and create your account. Each listed email can sign up once.
2. **iPhone:** in Safari, tap **Share → Add to Home Screen**, then open the app from the Home Screen. iOS only allows notifications for Home Screen apps (iOS 16.4 or later).
   **Android:** in Chrome, tap the menu and choose **Add to Home screen** or **Install app**.
3. Go to **Reminders → Turn on notifications**, allow them, and tap **Send a test**.

The reminder time is shared by both of you and defaults to 7:30 AM. If the app was down at reminder time, it still sends the reminder once it's back, as long as that's before 9 PM.

## Where things live

| Path | What it does |
|---|---|
| `Domain/Schedule.cs` | Which tasks are due on a date (daily, weekly, nth weekday of the month, seasonal in Jan/Apr/Jul/Oct) |
| `Domain/SeedRoutine.cs` | The starting routine, loaded on first run |
| `Services/HouseholdStore.cs` | JSON file storage with a lock, atomic saves and daily backups |
| `Services/AccountService.cs` | Sign-up allowlist and password checks |
| `Push/WebPushCrypto.cs` | VAPID signing and payload encryption |
| `Push/DailyReminderService.cs` | Sends the daily reminder |
| `wwwroot/sw.js`, `manifest.webmanifest` | Home-screen app and notification display |

## Backups

The app copies `household.json` to `backups/household-YYYY-MM-DD.json` once a day and keeps 14 days of copies. To restore one, stop the app, copy a backup over `household.json` (through Kudu or the SSH console), and start the app again.
