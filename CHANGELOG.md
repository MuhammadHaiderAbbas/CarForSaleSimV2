# Changelog

## v5.0-rc1 — Phase 10 "Production readiness"
- **Settings** in tabs: General, Display, Audio, Driving, Controls.
- **Key rebinding** for every control, saved between sessions, with a reset button.
- **Subtitles** for the auctioneer and customers; **colour-blind-safe theme**.
- **Audio**: volume per sound group, ambience by area (street, showroom, workshop, auction hall) with rain, and an
  Audio Library asset to drop in your own sounds and music.
- **Difficulty**: Easy, Normal, Hard, Realistic or a custom file; changes starting money, customer budgets, repair
  costs and footfall. **Simulation Configurator** (*Tools > Car Sales*) compares them in a 30-day sandbox.
- **Credits** screen in the main menu and pause menu.
- **One-click Windows builds** (*Tools > Car Sales > Build*), with CREDITS.txt and build info.
- **Strings table** for translations (menus, desktop, settings, HUD).
- HUD money counts up to the new balance.
- **PlayMode tests** (load, buy, sleep, save/load, drive, 14-day soak) and a save-migration test matrix.
- 17 new EditMode tests (283 total).

## v5.0-dev — Phase 9 "Advanced business"
- **Accounts**: cash flow with a live reconciliation check, profit per car, stock value and ageing, VAT.
- **VAT** on the margin scheme with quarterly returns, refunds and late fines.
- **Overdraft** from your credit rating, with interest.
- **Insurance** (basic / comprehensive) with claims and excess; **CCTV & alarm**; vandalism events.
- **Running costs**: utilities and business rates per site.
- **Marketing campaigns** (paper, online, social, signs, radio) with diminishing returns; reviews move footfall.
- **Dealership levels 1-6** that unlock staff slots and luxury buyers.
- **Business app**: level, sites, marketing, insurance & costs, analytics, property development.
- 7 new tests including a 30-day sandbox run that reconciles exactly (266 total).

## v5.0-dev — Phase 8b "Build your town"
- **Draw roads** in the World Builder: click start and end, ends snap and make junctions; pavements, trees and
  lamps are added and traffic drives on them.
- **More site types**: workshop, car wash, fuel station, parts shop, house, rental apartments, scenery buildings.
- **Showroom interiors** from imported Interior assets.
- **Performance**: traffic/people setting, frame-rate overlay, automatic thinning when the frame rate drops, far
  traffic updated less often.
- 2 new tests (259 total).

## v5.0-dev — Phase 8a "Living town"
- **Weather**: rain, heavy rain, fog, overcast; wet roads, less grip, fewer customers, dirty cars.
- **Calendar**: Sunday hours, bank holidays, busy Saturdays. **Fuel prices** change daily.
- **Random events**: police checks, breakdowns, road works, theft attempts; book an **open day**.
- **Traffic** that drives on the left, keeps its distance and stops at traffic lights (and for you).
- **Pedestrians** on the pavements; some walk into your dealership.
- **World Builder** (*Tools > Car Sales > World Builder*): add working dealerships anywhere, even inside an imported
  building, and validate every site. Your sites survive Build Everything.
- 8 new tests (257 total).

## v5.0-dev — Phase 7b "Sourcing & selling"
- **Private sellers**: classified ads, viewings at houses on Maple Close, questions (they may lie), inspection,
  a drive round the block, haggling, logbook handover.
- **Listing editor**: photos from six angles, honest highlights, a price helper against market and rivals, online
  adverts that bring enquiries - customers who come to view a specific car.
- **Customers** walk round the car, ask questions you answer honestly or not, and take **test drives** (with you or alone).
- **Paperwork & handover** with a **warranty** add-on (and later claims).
- New staff roles: **receptionist** and **manager**.
- 6 new tests (249 total).

## v5.0-dev — Phase 7a "Market, customers, staff"
- **Living market**: prices drift daily, seasons (sports cars in summer, SUVs in winter), demand and supply.
- **Competitor dealers** you can compare your prices with, and weekly **trade batches** from other dealers.
- **Nine kinds of buyer** who judge each car on price, condition, mileage, age, history, economy and paperwork.
- **Negotiation 2.0**: buyers spot faults, you can disclose them, **trade-ins** and **finance** (with commission).
- **Reviews** after every sale (honesty matters - hidden faults bring complaints) and a **Customers** app (CRM).
- **Staff**: hire salespeople, mechanics, detailers and drivers; weekly wages, skills, morale, sick days.
  Salespeople close deals with customers you don't reach; mechanics work the workshop board.
- New apps: Market, Customers, Staff.
- 11 new tests (243 total).

## v5.0-dev — Phase 6 "Auctions"
- **Auction calendar** with five sale types: dealer, fleet, salvage, repossession (hall) and timed online sales.
- **Walk-in sale hall**: lots roll onto the stage, the auctioneer calls *going once, going twice, sold*, and you bid by
  raising your paddle (E). Absentee bids work when you're not there.
- **Viewing yard** with numbered bays: read the condition report, inspect the car, check its history, leave a bid.
- **Online sales** with maximum (proxy) bids, outbid notifications and soft close.
- **AI bidders** with personalities (traders, exporters, breakers, mechanics, franchise and private buyers) who bid
  from the condition report.
- **Buyer accounts** (refundable deposit), **invoices** with buyer's premium, internet fee and VAT, late fees,
  cancellation, storage, collection from the car park.
- Auctions app on the computer and phone.
- 11 new tests (232 total).

## v5.0-dev — Phase 5b "Your own workshop"
- **AUTO WORLD Service Centre** next to your dealership: mechanical bay with a lift, tyre bay, MOT bay.
- **Hands-on work**: walk around a car and hold E on glowing markers to inspect it (wheels, bonnet, interior,
  OBD port, underneath on the lift) or to repair it bolt by bolt. Faster and safer than quick DIY.
- **Workshop Board**: waiting for parts → ready → in progress → done, on the wall and in the Workshop app.
- **Hire a mechanic** who works through the board during the day.
- **Parts Direct** counter in town: 10% cheaper, take the parts with you.
- Free MOT tests at your own MOT bay.
- 7 new tests (221 total).

## v5.0-dev — Phase 5a "Inspection, workshop, documents"
- **Hidden faults**: 40 kinds (misfires, head gaskets, slipping clutches, worn pads, rust, smoker's cars...). Worn cars
  have more; they get worse every day and lower the value.
- **Inspect step by step**: walk-around, interior, under the bonnet, OBD scanner, road test (drive 1 km), lift,
  history check, or pay for a full inspection. Each finds different faults; you get better with practice.
- **Paperwork & history**: plate and VIN, logbook, keys, MOT, service history; history check for write-offs (Cat N/S),
  outstanding finance (the lender can take the car!) and clocked mileage.
- **MOT tests** with fails and advisories.
- **Workshop app**: parts in three qualities from three suppliers, repair jobs at the garage or do it yourself,
  jobs in progress, MOT and paperwork (replacement logbook, locksmith, settle finance).
- Vehicle file **Documents** and **History** tabs are live; marketplace cards show *No logbook* / *MOT expired*.
- 21 new tests (214 total).

## v5.0-dev — Phase 4b "Screens"
- **HUD** is minimal: clock and shop status, cash (with +/- pops), slim GPS, smaller minimap; speed/gear/fuel only
  while driving; prompts show their key; **H** shows the controls. The HUD hides while menus are open.
- **Dealer computer** is a desktop: app icons, widgets (clock, today, goal, messages), taskbar. New **Accounts** app
  (profit & loss by period, profit-per-day chart, sortable transactions).
- **Vehicle file** with tabs: Overview, Condition, Documents (coming in phase 5), History, Costs (break-even), Listing.
  Open it for your own cars too (My Cars → *File*, car menu → *Vehicle file*).
- **Marketplace**: filter sidebar, *Best deal* sort, grid or list view, deal and verified badges.
- **Map**: full screen with filters, zoom, click-to-GPS and the route drawn along the roads.
- **Main menu**: cinematic camera; save slots show a screenshot.
- **Esc goes back** (phone app → home, computer app → desktop, vehicle file → where you came from).
- **Reflection probes** in showrooms.
- **Your models in the town**: Asset Studio → *Town* swaps backdrop buildings, trees, lamps and benches for models you
  imported; Build Everything keeps them.
- New UI components (tabs, sortable table, dialogs, stat tiles, charts); the colour lint also catches `<color=#..>`.
- 4 new tests (193 total).

## v5.0-dev — Phase 4a "Design system & lighting"
- **Themes**: Dark (original), Light, High contrast and a Custom theme asset; every colour in the UI now comes from the
  theme (a test enforces it). Button text adapts automatically for contrast.
- **Interface size** setting (80-130%), applied immediately.
- Windows **fade in**; gamepad users get the first button selected.
- **New phone**: home screen with clock, date, notifications and apps (Messages, Bank, Tow truck, Taxi, Map, My cars,
  Market, Real estate, Settings). Bank, Tow, Taxi and Messages work right on the phone.
- **Lighting**: URP post-processing (ACES tonemapping, bloom, colour grading, vignette, anti-aliasing) on all cameras;
  your newest Poly Haven sky is used automatically; the sky darkens at night and ambient light follows it.
- 2 new tests (189 total).

## v5.0-dev — Phase 3b "Online sources & characters"
- Asset Studio **Online** tab:
  - **Sketchfab**: search downloadable models (thumbnails, author, licence and whether it can ship), then
    *Download & set up* with your own API token. The licence record is filled in automatically.
  - **Poly Haven** (CC0): skies (HDRI → sky material, *Use as sky*) and materials (URP Lit with normal,
    AO and metallic-smoothness maps).
- **Characters as customers**: humanoid setup, idle/walk animations found automatically (e.g. Mixamo),
  an animator is generated; customers spawn looking like your imported characters.
- **Buildings** get a function and an Entrance marker (working sites come with the World Builder).
- **LODs** from `_LOD0/_LOD1` meshes; import-time mesh LODs where Unity supports them.
- **Preview in scene** with a 1.80 m person for scale.
- 30 new tests (187 total).

## v5.0-dev — Phase 3a "Asset Studio"
- **Asset Studio** (*Tools → Car Sales → Asset Studio*): bring external models into the game.
  - Import from a file, drag & drop, the Drop folder (`Assets/CarSales/Art/_Drop`) or any model in the project.
  - Measures triangles, meshes, materials and size; guesses the scale (cm/mm/inch mix-ups) and orientation
    (Z-up exports, sideways models); everything can be adjusted.
  - *Fix materials for URP*: pink Built-in materials become URP Lit; missing textures are found by file name.
  - Cars: wheels detected by name and position (or picked by hand), paint materials chosen, car data entered →
    a drivable car prefab and a car definition that appears for sale. *Drive on test track* button.
  - Buildings, props, interiors and characters get colliders (and an Animator for characters).
  - Every import needs a **licence record**; release builds refuse non-commercial / unknown licences, development
    builds only warn; `CREDITS.txt` is generated (button, and next to every build).
  - Library: versions with **Roll back** and **Remove**.
- Car paint works on imported models (several material slots, URP / glTF / Built-in colour properties).
- Cars in `Assets/CarSales/Data/Cars` (imports) are kept when you run Build Everything.
- 53 new tests (157 total).

## v5.0-dev — Test track fix
- The test track no longer depends on its scene references: the car database now lives in
  `Generated/Resources/CarSalesDatabase.asset` and is found automatically, input falls back to the default keys.
- Build Everything re-opens the saved test track scene and repairs any links that were lost (and logs which).
- The "input action is not linked" warning now points to *Tools → Car Sales → Re-link Input Actions*.

## v5.0-dev — Phase 2b "Test Track"
- **Test track** scene (built by Build Everything; *Tools → Car Sales → 5. Open Test Track*): long straight with
  distance boards, braking zone, slalom, bumps, skid pad, ramp and knock-over cones. Measures 0-100 km/h, braking
  distance, top speed and cornering g, next to the simulated figures. Switch car (N/B), condition (1-4),
  physics (G), gearbox (M), reset (R). Vehicle Studio: **Drive** button for each car.
- **Manual gearbox** option (Settings): Left Shift / Left Ctrl or gamepad bumpers; reverse from 1st at a standstill;
  over-revving downshifts are refused.
- **Dents**: damaged body panels now show as dents at the front, rear, sides or roof - on parked, listed and driven
  cars. Repairing the body removes them.
- **All parts** view (Inspection → All parts): every component with its condition, and how the car's condition
  changes power, brakes, grip and shocks.
- 10 new tests (104 total).

## v5.0-dev — Phase 2a "Vehicle Framework"
**Driving**
- **Realistic driving**: every car now drives on wheel physics (Rigidbody + 4 WheelColliders) with suspension,
  anti-roll bars, weight and grip. Front-, rear- or four-wheel drive depending on the car.
- **Engine and automatic gearbox**: torque curves, up/down shifts, kick-down, rev limiter, engine braking, reverse gear.
  Electric cars: instant torque, one gear, regenerative braking.
- **Driver aids**: ABS, traction control, stability control (each can be switched off in Settings).
- **10 handling classes** (city hatch, family hatch, sedan, sports sedan, SUV, pickup, van, sports, luxury, electric
  hatch) with realistic 0-100 km/h times and top speeds; petrol/diesel/hybrid/electric variants.
- Controls: S brakes and reverses when stopped, **Space = handbrake**, **C = camera** (chase / far / bonnet), **F3 = telemetry**.
- HUD speedometer shows the gear and goes up to 200 km/h. Engine sound pitched by rpm.
- *Settings → Physics: Arcade* brings back the v4 handling. Cars that can't be set up fall back to it automatically.

**Car condition**
- The 6 condition bars are now made of **~45 individual parts** (engine internals, timing belt, oil, each brake
  pad set, shocks, each tyre, each body panel, paint, glass, 12V battery...). Electric cars have a traction battery,
  motor and inverter instead of an engine. Old saves convert automatically.
- **Condition affects driving**: a worn engine is slower, worn pads brake later, bald tyres slide, worn shocks bounce,
  bad alignment pulls to one side, a dead battery means the car won't start.
- **Crashes damage the side that was hit** (front, rear, left, right, roof, underside). Driving wears parts (tyres, pads, oil...).
- Fuel use depends on how hard you drive.
- The inspection report lists the parts in the worst shape.

**Tools**
- **Vehicle Studio** (*Tools → Car Sales → Vehicle Studio*): each car's class, mass, 0-100, top speed and braking;
  "Make editable" creates a physics profile you can tune in the Inspector (kept when you Build Everything).
- 47 new tests (94 total).

## v5.0-dev — Phase 1 "Foundation"
**Saves**
- **3 save slots** (Main Menu → New Game / Load Game), each with **3 automatic backups**.
- Crash-safe writing: a save is written to a temp file and only then replaces the old one.
- Checksums: a damaged save loads the newest good backup instead ("Save recovered" message).
- Damaged values (NaN fuel, duplicate car ids, missing lists) are repaired on load.
- Your v4 save is moved into slot 1 automatically - nothing is lost.
- *Tools → Car Sales → Delete All Saves* replaces *Delete Save File*.

**Money**
- Every money movement is recorded in a **ledger** with a category (car sales, repairs, fuel, rent, loans...).
- Computer → **Bank** now has an **Account statement** (last 40 transactions, today's in/out, balance).
- The morning report shows where yesterday's money came from and went.

**Calendar**
- Weeks and quarters: the HUD clock shows the weekday (Day 1 is a Monday). Weekly/quarterly events are ready for wages and VAT later.

**Under the hood**
- New engine-free `CarSales.Foundation` assembly (EventBus, Ledger, GameCalendar, save store).
- First automated tests: 47 EditMode tests (*Window → General → Test Runner*). The installer now adds the Test Framework package.

## v4.0 "Open Town"
**World**
- A whole town: main road, north road and two avenues, street trees and lamps, skyline.
- New locations: **Fuel Station**, **Mechanic Workshop** (2 bays + mechanic desk), **Car Wash** (tunnel + attendant),
  **Used Car Market** (walk-around lot), **Auction Center**, 3 homes, 3 investment buildings, 3 dealerships.

**Driving & cars**
- Drive **any car you own** (F). Cars stay where you park them; drive up to a free dealership spot and press E to park.
- **Fuel** (100% = 25 km), refuel at the pumps; mileage increases while driving; crashes damage parts; night headlights.
- **Personal car** (your own beat-up hatchback) — can't be sold.
- **Tow trucks** (phone, P): move any car to a dealership, the workshop, car wash, fuel station or home. Price by distance.
- **Taxis** (phone or map): instant travel for money + time.

**Buying**
- **Used Car Market**: physical cars with price tags, free on-site inspection, buy and drive away.
- **Auction Center** (10:00–17:00): live bidding against AI bidders with a hammer timer; condition is only the seller's claim.
- Online purchases now cost a $300 delivery fee.

**Life & business**
- **24-hour day**: time always runs; dealerships open 08:00–20:00 automatically (close early with O or the sign).
- **Energy** drains while awake; **sleep at home** (from 18:00) to end the day; pass out at 02:00 (fine, groggy).
- **Properties** (computer → Real Estate, or FOR SALE signs): Suburban House & Hillside Villa (wake earlier, +reputation),
  **Downtown Motors** & **Luxury Lane** dealerships (own spots & customers; Luxury customers have 60% bigger budgets),
  Rental Apartments / Parking Garage / Office Tower (income every night).
- Morning report with rent, interest and property income.
- **Town map** (M) with GPS arrow on the HUD and minimap; **phone** (P).
- HUD: energy bar, GPS, fuel; new key hints. Goals updated for the town (15 goals).


## v3.0 "AUTO WORLD"
**UI redesign (everything builds itself from code - no prefabs, no Inspector wiring)**
- Dark-blue theme with procedurally generated rounded panels, icons, check marks and arrows (`UIFactory`).
- **Live car photos**: a hidden photo studio renders every car into the UI (`ThumbnailService`).
- **Dealer computer** with sidebar: Home dashboard, Marketplace (search, category chips, sorting, card grid),
  My Inventory (Owned / For Sale tabs), Messages, Upgrades, Bank & Loans, Statistics.
- **Vehicle Details**, **Inspection report** (6 parts with bars and verdict), **Purchase Successful** screen.
- **My Vehicle** screen with Inspect / Garage / Car Wash / Set Price / Test Drive / Quick Sell.
- **Garage**: pick repairs per part (engine, brakes, tires, body, interior, electronics) or a discounted Full Service.
- **Car Wash** with an animated wash.
- **Negotiation** as a chat with bubbles and a customer patience meter.
- **HUD**: minimap (with markers for cars, customers and the computer), speedometer, key hints, goal panel.
- **Notification cards** (success / info / warning) that stack and fade.

**Gameplay**
- Cars now have **per-part condition**; crashes damage body/electronics/tires.
- **Inspections** ($250, free with the Mechanic upgrade) reveal the truth before buying.
- **Messages**: private sellers offer honest cars at 60-80% of value (today only), plus tips and bank reminders.
- **Quick Sell** to a wholesaler for 75% of value (emergency cash).
- **Tab** opens your inventory anywhere. Debug money moved to **F1**.

**World**
- Glass-front showroom with roof, sign band, ceiling lights, display platforms, reception and lounge.
- Service garage with a car on a lift, car wash bay with brushes, curbs, road markings, sidewalks,
  trees, lamp posts, sale banners, flags, benches, planters and a city skyline.


## v2.0
**New game systems**
- **Main menu scene**: Continue / New Game / Settings / Quit, with a spinning showcase car.
- **Pause menu** (Esc): Resume, Save, Settings, Save & Main Menu, Save & Quit. Time stops while paused.
- **Settings**: mouse sensitivity and volume (saved in PlayerPrefs).
- **Dealer computer hub**: Marketplace, Upgrades, Bank & Loans, Statistics.
- **Upgrades**: Lot Expansion, Marketing, Mechanic Workshop, Detailing Bay, Showroom (13 levels in total).
- **Bank**: borrow up to a reputation-based limit, 2% daily interest, repay any time.
- **Goals**: 13 sequential objectives shown on the HUD, with cash rewards; they double as the tutorial.
- **Win** at $1,000,000 net worth; **bankruptcy** after 3 days in a row ending in debt.
- **Hidden condition**: marketplace sellers may exaggerate condition; the Mechanic upgrade reveals the truth.
- **Test drives**: drive any of your cars for 60 s with a chase camera; crashes reduce condition.
- **Day/night**: the sun moves with the shop clock.
- **Sound effects**: generated in code (no audio files), with optional clip overrides.

**Changes**
- Daily report shows loan interest, net worth and a debt warning.
- HUD shows the current goal, loan, and test-drive speed and timer.
- Error messages are red and play an error sound.
- Save format v2 (loads v1 saves).
- Package installer installs packages one by one (works on every Unity version, survives reloads).

## v1.0
- Core loop: marketplace → repair/clean → price → customers → negotiation → daily report → save/load.
- One-click wizard that generates all assets, prefabs, UI and the test scene.
