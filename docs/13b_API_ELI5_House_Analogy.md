# GymTrackPro — API ELI5 (House/Household Analogy)

> Each API endpoint described as a person, room, or appliance in a house. Zero technical jargon.

---

## 🏠 The Entry Door — Authentication

---

🏠 **POST /api/v1/auth/sync-user** — The Front Door Doorman
**What it does:** This is the doorman at the front entrance of the house who checks your guest ID card (your Firebase badge) and then goes to the master notebook inside to find your name.
**How it helps:** If your name isn't in the notebook yet, he writes it in; if it is, he updates the "last seen" column — so the house always knows who walked through the door and when.

---

🏠 **POST /api/v1/auth/activate** — The Key-Cutting Desk
**What it does:** This is a special desk near the entrance where a new family member brings their invite letter and a blank key, and the desk cuts them a proper key for their specific room.
**How it helps:** Once the desk stamps and activates the key, the new family member gets exactly the right level of access (gym staff room vs. member lounge) for the rest of their stay.

---

## 🏠 The Member Registry Room — Member Management

---

🏠 **GET /api/v1/members** — The Family Album on the Living Room Table
**What it does:** This is a big photo album sitting on the coffee table that shows the picture and name of every person who currently lives in the house.
**How it helps:** Anyone on the staff can flip through it quickly to find out who all the active household members are without going room by room.

---

🏠 **GET /api/v1/members/{id}** — The Single Portrait on the Wall
**What it does:** This is like looking at one specific framed portrait on the wall and reading the label beneath it to learn everything about that one person.
**How it helps:** Staff who already know someone's door number can go directly to their picture and get all the details without searching the whole album.

---

🏠 **POST /api/v1/members/qr/lookup** — The QR Scanner at the Welcome Desk
**What it does:** This is a scanner placed at the welcome desk; when you hold up your membership badge (QR code), the scanner looks up your portrait in the album.
**How it helps:** The check-in desk can instantly pull up a member's full profile just by reading their badge, without needing to type anything.

---

🏠 **POST /api/v1/members** — The Registration Desk in the Lobby
**What it does:** This is the welcome desk where new people fill out a paper form, have their photo taken, and get handed a personalized badge with their unique house code.
**How it helps:** As soon as the form is processed, the person's portrait is printed and hung in the family album, and they are officially part of the household.

---

🏠 **PUT /api/v1/members/{id}** — The Bulletin Board Updater
**What it does:** This is a staff member who walks to a specific portrait on the wall and carefully replaces the old information card beneath it with an updated one.
**How it helps:** When someone changes their phone number or address, the household records stay accurate without needing to register the person all over again.

---

🏠 **GET /api/v1/members/search** — The Index Card Drawer
**What it does:** This is a large drawer full of alphabetically sorted index cards where the staff can filter by name, phone number, or room number to find specific residents.
**How it helps:** Instead of flipping through hundreds of portraits, staff can narrow the search using any detail they remember, and the drawer shows only the matching cards page by page.

---

🏠 **DELETE /api/v1/members/{id}** — The Portrait Covered with a Sheet
**What it does:** Only the head of the household (Administrator) is allowed to drape a white sheet over a specific portrait, hiding it from view without destroying it.
**How it helps:** The covered portrait is no longer visible to day-to-day staff, but the record is preserved in storage so the house has a history of everyone who ever lived there.

---

🏠 **POST /api/v1/members/{id}/app-invite** — The Guest Pass Printer
**What it does:** This is a special machine in the office that prints a time-limited guest pass code for a specific household member so they can register for the self-service member lounge.
**How it helps:** The staff hands the pass to the gym member, who uses it to unlock their own private corner of the house app without staff needing to set it up for them.

---

## 🏠 The Attendance Hall — Check-In/Check-Out

---

🏠 **POST /api/v1/attendance/check-in** — The Arrivals Logbook at the Gym Door
**What it does:** This is the logbook at the gym door where the receptionist stamps the time and scans the member's badge the moment they walk in.
**How it helps:** Every arrival is recorded with a timestamp so the household knows exactly who is inside the gym right now and when they arrived.

---

🏠 **POST /api/v1/attendance/{id}/check-out** — The Departures Stamp
**What it does:** This is a second rubber stamp next to the logbook that the receptionist uses when a member walks out, completing their entry with a departure time.
**How it helps:** The filled entry (arrival + departure) becomes a finished record, and the household can calculate exactly how long that member worked out.

---

🏠 **POST /api/v1/attendance/{id}/correct-checkout** — The Eraser and Correction Pen
**What it does:** Only the head of the house has an eraser that can cross out a wrong departure time in the logbook and write the correct one above it, creating a new verified entry.
**How it helps:** Human mistakes in logging departure times are fixed cleanly, and the old entry is marked as replaced so there is a full audit trail of the correction.

---

🏠 **POST /api/v1/attendance/{id}/void** — The Red VOID Stamp
**What it does:** The head of the house has a large red VOID stamp they can press onto a logbook entry to completely invalidate it, with a written reason on the side.
**How it helps:** If someone was accidentally checked in who should not have been, that entry is marked invalid so reports stay accurate and the mistake is documented.

---

🏠 **POST /api/v1/me/attendance/check-in** — The Self-Service Kiosk Tap Button
**What it does:** This is a button on a self-service kiosk in the member lounge that a gym member taps themselves when they arrive, generating a unique tap-ID to prevent accidental double-presses.
**How it helps:** Members can log their own arrival without a receptionist, and the unique tap-ID makes sure no matter how many times they accidentally tap, only one arrival is recorded.

---

🏠 **GET /api/v1/me/attendance/current** — The "Are You Still Here?" Light on the Kiosk
**What it does:** This is a small status light on the kiosk that a member can glance at to see whether they are currently logged as present (green) or logged out (grey).
**How it helps:** Members always know their current check-in status on the app without needing to ask the receptionist.

---

## 🏠 The Finance Office — Subscriptions & Payments

---

🏠 **POST /api/v1/subscriptions** — The Enrollment Desk
**What it does:** This is a desk in the finance office where staff writes a new membership contract connecting one household resident to one specific house plan (e.g., 30-day Gold Plan).
**How it helps:** The written contract (with start and end dates) tells the rest of the house how long that member is allowed to use the gym facilities.

---

🏠 **POST /api/v1/subscriptions/{id}/pause** — The Contract Pause Sticker
**What it does:** This is a bright sticker placed on top of a member's contract indicating that the clock on their plan is temporarily frozen, with a reason written on the sticker.
**How it helps:** When a member goes on vacation or gets sick, their contract time stops counting down, so they do not lose paid days they have not yet used.

---

🏠 **POST /api/v1/subscriptions/{id}/resume** — The Sticker Removed, Clock Restarted
**What it does:** Removing the pause sticker from the contract tells the house clock to start counting the remaining contract days again.
**How it helps:** The member picks up exactly where they left off, with no days lost due to their absence.

---

🏠 **POST /api/v1/subscriptions/renew** — The One-Stop Renewal Counter
**What it does:** This is a single counter where staff simultaneously stamps a new contract and rings up the membership fee in one transaction, so both are always done together.
**How it helps:** The house guarantees it never creates a paid invoice without a matching contract, or a contract without a matching receipt — they happen atomically together.

---

🏠 **POST /api/v1/payments** — The Cash Register
**What it does:** This is the house cash register that records every payment a member makes, prints a numbered receipt, and notes whether they paid with cash, GCash, card, or another method.
**How it helps:** Every peso collected is documented in the ledger with a receipt number, creating a complete financial trail for the household.

---

🏠 **POST /api/v1/payments/{id}/refund** — The Refund Drawer
**What it does:** Only the head of the house can open the refund drawer; when they do, a specific receipt is stamped REFUNDED, and the matching contract is stamped CANCELLED.
**How it helps:** The house ensures that when money is returned to a member, their access is simultaneously removed, keeping the financial and access records in sync.

---

🏠 **GET /api/v1/payments/search** — The Receipt Sorter
**What it does:** This is a filing cabinet with multiple sorting tabs — by date, payment method, status, or member — that lets staff pull out exactly the receipts they are looking for.
**How it helps:** Instead of flipping through every receipt in the house, staff can narrow down to the exact transaction they need in seconds.

---

## 🏠 The Reports Room — Analytics & Exports

---

🏠 **GET /api/v1/reports/daily-revenue** — The Daily Cash Tally Board
**What it does:** This is a whiteboard in the reports room that shows the total money collected for each day within a date range, along with how many transactions happened each day.
**How it helps:** The head of the house can walk in each morning, glance at the board, and immediately see which days earned the most and identify income trends.

---

🏠 **GET /api/v1/reports/daily-revenue/export** — The Printer Next to the Tally Board
**What it does:** This is a printer attached to the tally board that produces a formatted CSV spreadsheet version of the daily revenue data when requested.
**How it helps:** The head of the house can hand a printed copy to the accountant or store it in a filing cabinet for monthly reviews.

---

🏠 **GET /api/v1/reports/attendance** — The Visitor Logbook Summary Shelf
**What it does:** This is a shelf holding a bound summary of all attendance entries — who came, when they came, when they left, and which plan they were on — for a selected date range.
**How it helps:** It gives management a clear picture of peak hours and who the most active members are.

---

🏠 **GET /api/v1/reports/expiring-memberships** — The "Contracts About to Expire" Notice Board
**What it does:** This is a notice board near the finance office that automatically displays a list of all members whose contracts will expire within the next N days.
**How it helps:** Staff can proactively reach out to these members for renewal before their access lapses, reducing membership drop-off.

---

🏠 **GET /api/v1/reports/cashier-activity** — The Cashier Shift Report Clipboard
**What it does:** This is a clipboard that tracks every action a cashier (receptionist or admin) took during a time period — payments processed, subscriptions created, refunds given.
**How it helps:** The head of the house can review the clipboard to ensure all cashiers are handling transactions correctly and to investigate any discrepancies.

---

🏠 **GET /api/v1/reports/attendance/summary** — The Attendance Trend Graph Frame
**What it does:** This is a framed line graph on the wall of the reports room that shows visit totals grouped by day, week, or month for a chosen date range.
**How it helps:** The head of the house can see at a glance whether the gym is growing busier or quieter over time, helping plan for staffing and promotions.

---

## 🏠 The Member Lounge — Gym Goer Self-Service

---

🏠 **GET /api/v1/me** — The Member Lounge Mirror
**What it does:** This is a mirror in the private member lounge that, when a member looks into it, shows their own account name, email, and membership role.
**How it helps:** Members can always confirm they are logged in as the correct person before doing anything in their private section of the house.

---

🏠 **GET /api/v1/me/dashboard** — The Personal Progress Bulletin Board in the Lounge
**What it does:** This is a bulletin board inside each member's private lounge corner that displays their current membership status, how many minutes they worked out this month, how many days in a row they visited, and any achievement badges they have earned.
**How it helps:** Members get a motivating summary of their gym habits every time they open the app, encouraging them to keep their streak alive.

---

🏠 **GET /api/v1/me/attendance** — The Member's Personal Logbook
**What it does:** This is a small personal logbook that lives in the member's lounge corner and shows every day they checked in and out, oldest to newest, one page at a time.
**How it helps:** Members can scroll back through their workout history to see exactly how often they have been visiting the gym.

---

🏠 **GET /api/v1/me/digital-card** — The Digital Membership Card Frame
**What it does:** This is a framed digital card on the lounge wall showing the member's QR code, their current membership status, and their plan expiry date.
**How it helps:** The member can show this card (or its QR code) to reception in case they do not have their physical card, so staff can still verify their membership.

---

🏠 **GET /api/v1/me/progress** — The Monthly Fitness Scorecard Drawer
**What it does:** This is a monthly scorecard stored in a drawer that shows how many minutes and visits the member completed during a chosen month, their current streak, and which achievement badges they qualify for.
**How it helps:** Members can compare their scores month by month to see if they are improving their gym consistency over time.

---

## 🏠 The Operations Desk — Extra API Doors

These are real doors in the same house that are useful in a demo, even though they are less central to the story above.

🏠 **GET /api/v1/dashboard/metrics** — The Manager's Wall of Numbers  
**What it does:** Shows staff the big daily numbers: gym activity, membership, money, and plan performance.  
**How it helps:** The manager sees how the gym is doing without counting records one by one.

---

🏠 **GET /api/v1/members/{memberId}/profile-picture** and **GET /api/v1/me/profile-picture** — The Portrait Window  
**What it does:** Shows a member's saved profile photo. Staff use the first door; a Gym Goer uses the second door to see their own.  
**How it helps:** The right person sees the right portrait without exposing everyone else's photos.

---

🏠 **GET /api/v1/plans/{id}** — The Single Plan Folder  
**What it does:** Opens one specific membership-plan folder instead of the entire plan cabinet.  
**How it helps:** Staff can check one plan's details quickly before enrolling or updating a member.

---

🏠 **POST, GET, and DELETE /api/v1/users/{userId}/app-invite...** — The Staff Pass Cabinet  
**What it does:** Lets the administrator create, inspect, or revoke an app invite for a particular user.  
**How it helps:** The household can give a person a pass, check whether it is still usable, or cancel it when needed.

---

🏠 **GET /api/v1/attendance/member/{memberId}**, **POST /api/v1/attendance/checkin**, and **POST /api/v1/attendance/{id}/checkout** — The Old Logbook Labels  
**What it does:** These are older labels for attendance doors. They still work for now but point staff to the newer `/history`, `/check-in`, and `/check-out` labels.  
**How it helps:** Older app versions can still use the house while everyone moves to the clearer labels. The old labels retire on 12 January 2027.

---

## 🏠 The Control Room — System Settings

---

🏠 **GET /api/v1/settings** — The House Control Panel Display
**What it does:** This is a display panel in the control room that shows every configurable setting of the house — like the gym's name, operating timezone, and currency symbol.
**How it helps:** Administrators can see exactly how the house is configured at any time, all on one screen.

---

🏠 **PUT /api/v1/settings/{key}** — The Control Panel Dial
**What it does:** This is a specific labeled dial on the control panel that the head of the house can turn to change one particular setting, like adjusting the timezone from Manila to Singapore.
**How it helps:** Operational parameters can be adjusted without restarting the whole house — the change takes effect immediately across all rooms.

---

## 🏠 The Intercom — Notifications

---

🏠 **GET /api/v1/notifications** — The Intercom Message Board
**What it does:** This is a message board next to the intercom where all messages sent to household members are posted, and staff can filter them by person to see whose messages are pending.
**How it helps:** Staff and the system can review which members have received alerts about their memberships, payments, or reminders.

---

🏠 **PUT /api/v1/notifications/{id}/read** — The "Mark as Seen" Stamp
**What it does:** This is a small rubber stamp that marks a specific intercom message as "read," removing it from the urgent pile.
**How it helps:** The member or staff confirms the message has been acknowledged, keeping the message board clean of clutter.
