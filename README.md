# MeDan API (ASP.NET Core 9 + EF Core + PostgreSQL)

Backend for the MeDan hostel platform. **Authentication stays in Firebase** — the Flutter
app signs users in with Firebase Auth and sends the Firebase ID token as
`Authorization: Bearer <token>`. This API validates that token against Google's public keys
(no passwords stored here) and owns all the relational data.

## Stack
- ASP.NET Core 9 Web API
- EF Core 9 + **SQL Server** (LocalDB for dev). *Swap to PostgreSQL by changing the package to `Npgsql.EntityFrameworkCore.PostgreSQL` + `UseSqlServer`→`UseNpgsql`.*
- Firebase ID-token validation via JWT bearer (`securetoken.google.com/<projectId>`)
- Swagger UI at `/swagger` in Development

## Configure
Edit `appsettings.json` (or use environment variables / user-secrets):

```json
"ConnectionStrings": { "Default": "Server=(localdb)\\MSSQLLocalDB;Database=medan;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True" },
"Firebase": { "ProjectId": "medan-6bca0" }
```

> `Firebase:ProjectId` must match the Flutter app's Firebase project, or token validation fails.
> For a full SQL Server instance use e.g. `Server=localhost;Database=medan;User Id=sa;Password=...;TrustServerCertificate=True`.

### Paystack
The secret key **never goes in `appsettings.json`**:

```bash
dotnet user-secrets set "Paystack:SecretKey" "sk_test_xxx"   # or: Paystack__SecretKey env var
```

| Key | Meaning |
|-----|---------|
| `Paystack:SecretKey` | `sk_test_…` / `sk_live_…`. Empty + Development ⇒ **simulation mode** |
| `Paystack:CallbackUrl` | Where Paystack returns the browser after checkout (optional) |
| `Paystack:Currency` | `GHS` |
| `Referrals:RewardAmount` | GH₵ per successful referral (default 20) |
| `Referrals:ShareBaseUrl` | Deep-link base for share codes (default `https://medan.app/r`) |

**Simulation mode**: with no key in Development, `initialize` returns a reference with no
checkout URL and `verify` always reports success — enough to drive booking → escrow locally.
It is refused outside Development: the app fails at startup if the key is missing. Point
Paystack's webhook at `POST /api/payments/webhook`; it checks the `x-paystack-signature`
HMAC-SHA512 over the raw body, then **re-verifies the reference with Paystack** rather than
trusting the payload's amounts.

## Run
```bash
# 1. SQL Server LocalDB ships with Visual Studio / the SQL Server Express tools.
#    Apply the schema (needs: dotnet tool install -g dotnet-ef)
dotnet ef database update

# 2. Run (also auto-migrates in Development)
dotnet run
# → http://localhost:5xxx/swagger
```

## Testing
Import `backend/postman/` into Postman — a collection covering every route, with chained
variables and a request that fetches a Firebase ID token for you. See its
[README](../postman/README.md).

## Auth flow
1. App creates the user in **Firebase Auth** (email/password, Google, etc.).
2. App calls `POST /api/auth/register` with the ID token + profile details (name, role,
   and — for students — course/department/guardian). The UID and email are read from the
   **verified token**, never trusted from the body.
3. Thereafter every request carries the token; `GET /api/auth/me` returns the profile.

## Endpoints (v1)
| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| POST | `/api/auth/register` | token | Create the AppUser (+ student profile) for the Firebase user |
| GET  | `/api/auth/me` | token | Current user's profile |
| GET  | `/api/hostels` | public | List/filter hostels (`campus`, `type`, `maxPrice`, `verified`, `q`) |
| GET  | `/api/hostels/{id}` | public | Hostel detail incl. rooms + amenities |
| POST | `/api/hostels` | owner/worker | Create a listing (enforces tier listing limit) |
| GET  | `/api/hostels/{hostelId}/rooms` | public | Rooms in a hostel |
| POST | `/api/hostels/{hostelId}/rooms` | owner/worker | Add a room; auto-creates `Capacity` beds |
| POST | `/api/companies` | token | Create a company (caller becomes Owner) |
| GET  | `/api/companies/mine` | token | Companies you own or work for |
| POST | `/api/companies/{id}/members` | owner | Add a worker by email |
| DELETE | `/api/companies/{id}/members/{userId}` | owner | Remove a worker |
| GET  | `/api/bookings/mine` | student | Your bookings |
| GET  | `/api/bookings/{id}` | student/staff | One booking |
| GET  | `/api/bookings/company/{companyId}` | owner/worker | Company dashboard feed (`?status=`) |
| POST | `/api/bookings` | student | Reserve a bed (Pending) |
| POST | `/api/bookings/{id}/confirm-payment` | student | Attach a reference, **verify it with Paystack**, hold in escrow |
| POST | `/api/bookings/{id}/check-in` | owner/worker | Confirm arrival with check-in code |
| POST | `/api/bookings/{id}/complete` | owner/worker | Release escrow after 48h window (+ grants referral reward) |
| POST | `/api/bookings/{id}/cancel` | student | Cancel + release the bed |
| POST | `/api/bookings/{id}/dispute` | student | Raise a dispute inside the 48h window |
| POST | `/api/bookings/{id}/resolve-dispute` | admin/manager | Close it: `refund` or `release` |
| POST | `/api/payments/initialize` | student | Start a Paystack transaction → reference + checkout URL |
| POST | `/api/payments/{reference}/verify` | student/staff | Verify with Paystack and hold in escrow |
| GET  | `/api/payments/{reference}` | student/staff | Stored state of a reference |
| GET  | `/api/payments/booking/{bookingId}` | student/staff | The payment attached to a booking |
| POST | `/api/payments/webhook` | **public** (HMAC) | Paystack callback — signature-checked, then re-verified |
| GET  | `/api/referrals/me` | token | Your share code (created on first call) + earnings summary |
| GET  | `/api/referrals/mine` | token | Everyone who signed up with your code |
| GET  | `/api/referrals/referrer` | token | Who referred you, if anyone |
| POST | `/api/referrals/attach` | token | Use a friend's code (once, right after registering) |
| POST | `/api/referrals/{id}/mark-paid` | admin/manager | Mark a claimed reward as paid out |

## Data model
**Identity / people**
- `Users` — mirrors Firebase users (`FirebaseUid` unique), holds `Role` (Student/Owner/Worker/Manager/Admin).
- `StudentProfiles` — 1:1 with a student user: course, department, level, campus, index no., **guardian** name/phone/relationship/email.
- `Companies` — a hostel business; has an owner + a subscription `Tier` (commission + listing limit).
- `CompanyMembers` — owner + workers of a company; `CanPostListings` controls who can post.

**Inventory**
- `Hostels` — a listing with `PropertyType` (Hostel/Hometel/Apartment/SelfContained/Hall), location, verified flag, rating, denormalized price range. Belongs to a company; tracks who posted it.
- `HostelPhotos`, `Amenities` + `HostelAmenities` (M:N).
- `Rooms` — `RoomType`, `Capacity` (1–4), `PricePerBedPerSemester`, `Gender`, `AvailableBeds`.
- `Beds` — one row per space in a room (this is what a student books → enables "4 in a room").

**Transactions**
- `Bookings` — a student books a **bed**, with the escrow state machine
  (`Pending → PaymentHeld → CheckedIn → Completed`, plus `Disputed/Refunded/Cancelled`).
- `Payments` — Paystack reference per booking (MoMo / card).
- `Reviews`, `Favorites`, `Referrals`, `Campuses` (seeded: UENR, USTED).

### Relationships (text ER)
```
Company 1───* Hostel 1───* Room 1───* Bed
   │  1                                 │ 0..1
   *                                    │ (current booking)
CompanyMember *───1 AppUser 1──0..1 StudentProfile
                       │ 1
                       *
                    Booking *───1 Hostel, Room, Bed   (+ Payment 1:1)
```

## Migrations
```bash
dotnet ef migrations add <Name> -o Data/Migrations
dotnet ef database update
```

## Payment + referral flows

**Paying for a booking** (`PaymentsController` → `PaymentService`):
```
POST /api/bookings                    → booking Pending, bed Reserved
POST /api/payments/initialize         → Payment Initialized, reference + checkoutUrl
   (customer pays on Paystack)
POST /api/payments/{ref}/verify       ─┐ both call PaymentService.ApplyAsync →
POST /api/payments/webhook            ─┘ Payment Success + booking PaymentHeld
```
Verify and the webhook are **idempotent** and race-safe: whichever lands first advances the
booking, the other is a no-op. A reference that settled for less than the booking price is
rejected, not held. `Payment` is 1:1 with a booking — re-initializing replaces a stale
attempt, but a successful one is final.

**Refer & Earn** (`ReferralsController` → `ReferralService`):
```
GET  /api/referrals/me      → allocates AppUser.ReferralCode on first call (6 chars, no 0/O/1/I)
POST /api/referrals/attach  → friend uses the code once; Referral row Pending
POST /api/bookings/{id}/complete → friend's first completed booking flips it to Claimed
POST /api/referrals/{id}/mark-paid → staff records the payout → Paid
```
The referee is always taken from the token, never the body; a user can be referred once
(enforced by a filtered unique index) and cannot use their own code.

**Escrow release** is deliberately *not* a payments endpoint — `POST /api/bookings/{id}/complete`
owns the booking state machine. The app's `PaymentRepository.releaseEscrow(bookingId)` should
call that route.

## Next steps (not yet built)
- **Payout/refund** legs: Paystack transfer to the owner on `complete`, refund on `cancel`
  after payment. Today `complete` only flips state — no money leaves Paystack.
- A background job to **auto-complete** bookings once the 48h dispute window passes
  (today `complete` is a manual endpoint).
- Disputes endpoints (`raise` / `resolve`) feeding the `Disputed`/`Refunded` states.
- Migrating the Flutter data layer from Firestore to this API (repositories swap their datasource).
