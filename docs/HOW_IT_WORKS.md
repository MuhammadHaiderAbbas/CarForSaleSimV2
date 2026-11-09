# How It Works

## Contents
1. [Core loop](#1-core-loop)
2. [Architecture](#2-architecture)
3. [Startup order](#3-startup-order)
4. [Data model](#4-data-model)
5. [Economy formulas](#5-economy-formulas)
6. [Customer AI](#6-customer-ai)
7. [Extending the game](#7-extending-the-game)
8. [Moving into a city project](#8-moving-into-a-city-project)
9. [Roadmap ideas](#9-roadmap-ideas)
10. [v2 systems: upgrades, bank, goals, test drive, menus, audio](#10-v2-systems)
11. [v3: code-built UI, car photos, minimap, parts, messages](#11-v3-systems)
12. [v4: town, driving, day & sleep, properties, auctions](#12-v4-systems)

---

## 1. Core loop
```
[Computer: Buy Car] → [Car delivered to free ParkingSpot] → [Repair / Clean]
        ↑                                                       ↓
[Next Day: new listings] ← [Daily report + rent] ← [Negotiate] ← [Set Price → Listed]
                                                       ↑
                                       [CustomerAI browses Listed cars]
```

## 2. Architecture

**Portability rule:** the game is made of two prefabs plus one component on the player.

| Piece | Contains | Where it goes |
|---|---|---|
| `CarSalesSystems` | `CarSalesBootstrap`, `EconomyManager`, `InventoryManager`, `MarketplaceManager`, `DayCycleManager`, `SaveManager`, `DebugCheats` + Canvas with `UIManager`, HUD and all panels | Anywhere in the scene (one) |
| `Dealership` | `DealershipManager`, `CustomerSpawner`, parking spots, computer, sign, spawn/exit points | On the lot you choose |
| Player | `PlayerInteractor`, `MenuInputBlocker` (+ `SimpleFirstPersonController` in the test scene) | Your player or the city's player |

**Communication**

| Mechanism | Used for |
|---|---|
| `CarSalesServices` (static service locator) | Any script can reach any manager: `CarSalesServices.Economy.TrySpend(500)` |
| `IInteractable` interface | Player looks at an object, presses E → `Interact()` |
| C# events (`OnMoneyChanged`, `OnTimeChanged`, `OnShopStateChanged`, `OnMenuStateChanged`…) | The HUD and sign update themselves; the player is frozen while menus are open |

**Golden rules**
- Managers **register in `Awake()`**. Everyone else **uses services from `Start()`** onward.
- `InventoryManager.OwnedCars` is the **source of truth**. Car GameObjects are visuals that point at the same
  `CarInstance` objects. Saving writes only the data, and loading rebuilds the visuals (`DealershipManager.RebuildLot()`).
- Money is always an **int**.

## 3. Startup order
1. `Awake`: every manager registers in `CarSalesServices`. `RuntimeNavMeshBaker` bakes if no NavMesh data exists.
2. `Start`:
   - `SaveManager` → `LoadGame()`. If there's no save → `NewGame()` (money reset, day 1, 2 starter cars, new listings).
   - `HUDController` subscribes to events and draws its initial values.
   - `MarketplaceManager` creates listings if it still has none.
3. Panels start **inactive**. Their `Awake()` (button listeners) runs the first time `UIManager.Open()` shows them.

## 4. Data model

| Type | Kind | Purpose |
|---|---|---|
| `CarDefinition` | ScriptableObject asset | Static model data: id, name, brand, category, year, base price, popularity, model prefab, thumbnail |
| `CarDatabase` | ScriptableObject asset | List of all `CarDefinition`s |
| `CarInstance` | `[Serializable]` class | One specific car: condition, cleanliness, mileage, colour, prices, state, parking spot |
| `CustomerProfile` | `[Serializable]` class | Budget, preferred category, patience, greed, minimum condition |
| `DailyReport` | `[Serializable]` class | Per-day stats |
| `SaveData` | `[Serializable]` class | Money, reputation, day, owned cars, marketplace listings → JSON |

`CarState` flow: `InTransit → NeedsRepair / Ready → Listed → Sold`.

Saves (v5): `Application.persistentDataPath/saves/slot_1..3/save.json` plus `save.json.bak1..3`
(**Tools → Car Sales → Show Save Folder**). The file is an envelope (`Foundation/Save/SaveEnvelope.cs`) with a checksum
and one JSON block per system: `game` (the `SaveData` above) and `ledger` (every money movement). Writes are atomic
(temp file, then replace) and loading falls back to the newest valid backup. A v4 `carsales_save.json` is moved into
the first free slot automatically. Money must go through `EconomyManager.AddMoney/TrySpend/ForceSpend` with a
`TxCategory`, so it appears in the bank statement and reports.

## 5. Economy formulas
All in `Scripts/Systems/CarValueCalculator.cs`. Balance the game by changing these numbers.

```
MarketValue = BasePrice
            × Lerp(0.35, 1.0, Condition/100)
            × Lerp(0.90, 1.0, Cleanliness/100)
            × Clamp(1 − Mileage/300000, 0.5, 1.0)
            × Popularity

RepairCost  = (100 − Condition) × BasePrice × 0.004
CleanCost   = $150
Marketplace asking price = MarketValue × Random(0.70 … 0.95)
Rent per day = $500 + $50 × cars owned
```

**Negotiation** (`NegotiationSession.cs`):
```
MaxWillingToPay = Min(Budget, MarketValue × Lerp(1.25, 0.95, Greed))   ← hidden
OpeningOffer    = MarketValue × Lerp(0.85, 0.65, Greed)

Your offer ≤ their offer          → accepted
Your offer ≤ MaxWillingToPay      → accept chance = 1 − (offer − theirOffer)/(max − theirOffer)
                                    else they move toward you, patience −1
Your offer > MaxWillingToPay      → "way too much!", patience −2
Patience ≤ 0                      → they walk away, reputation −2
Sale at ≤ market value → reputation +2, above → +0.5
```

## 6. Customer AI
`CustomerAI.cs` is a coroutine state machine (a lighter alternative to a behaviour tree):

```
Browsing ──pick a Listed car (70% preferred category)──► walk to BrowsePoint ──► look 3–6 s
   │                                                                       │
   │                                    WantsCar()? ── no ──► next car (2–4 cars max)
   │                                         │ yes
   ▼                                         ▼
Leaving ◄── ignored 45 s (−1 rep) ── Interested "!" ── player presses E ──► Negotiating
   ▲                                                                       │
   └──────────────────────────── EndNegotiation(sold?) ◄────────────────────┘
```

`WantsCar()` interest score:
```
0.7  − 2 × max(0, ListedPrice/MarketValue − 1)     (overpriced penalty)
     + 0.25 if category matches
     + (Reputation − 50)/200
     − 0.5  if Condition < customer's MinCondition
     − 0.15 if Cleanliness < 40
Rejected immediately if ListedPrice > Budget × 1.15
```

## 7. Extending the game

### Add a new car model
1. Import a model. Make a prefab with **`CarVisual`** on its root, and drag the body renderers into *Paint Renderers*.
   - URP Lit colour property: `_BaseColor`. Built-in Standard: `_Color`.
   - Remove its colliders (the `CarDisplay` root has one). Front of the car = **+Z**.
2. **Create → CarSales → Car Definition**. Give it a unique `carId` and assign the prefab.
3. Add it to `Generated/Data/CarDatabase.asset`.
   > ⚠️ Running **Build Everything** recreates the `Generated` folder. Keep your own cars in another folder
   > (e.g. `Assets/CarSales/MyCars`) and add them to the database again after a rebuild, or stop using the wizard
   > once you start customising.

### Add more parking spots
Duplicate a `Spot_XX` under `Dealership/ParkingSpots`, give it a **unique `index`**, and add it to
`DealershipManager.spots` (or leave the list empty and it collects child spots automatically).

### Add a new interactable (e.g. a coffee machine)
```csharp
public class CoffeeMachine : MonoBehaviour, CarSales.IInteractable
{
    public bool CanInteract() => true;
    public string GetInteractionText() => "[E] Buy coffee ($5)";
    public void Interact(GameObject who) { CarSales.CarSalesServices.Economy.TrySpend(5); }
}
```
Put it on Layer 20 *Interactable* with a collider.

### Add a new UI panel
Create a panel under `CarSalesCanvas` (inactive). Its script calls `CarSalesServices.UI.Open(gameObject)` to show
and `CarSalesServices.UI.Close()` to hide. Add a public field to `UIManager` if other scripts need it.

### Use real customer characters
Replace the capsule in `Customer.prefab` with a humanoid (e.g. from Mixamo). Add an **Animator** with a float
parameter `Speed` (idle ↔ walk blend tree) and assign it to `CustomerAI.animator`.

## 8. Moving into a city project

**Before exporting**
- Same Unity version, same render pipeline.
- Right-click a prefab → **Select Dependencies**. Everything should be inside `Assets/CarSales`.

**Export / import**
1. Right-click `Assets/CarSales` → **Export Package…** → tick *Include dependencies* → Export.
2. City project → **Assets → Import Package → Custom Package…** → Import all.

**Settings that don't travel in a package**
| Setting | How |
|---|---|
| Packages (Input System, AI Navigation, uGUI) | The installer dialog appears automatically, or use *Tools → Car Sales → 1. Check & Install Packages* |
| **Layer 20 = Interactable** | Project Settings → Tags and Layers (must be the **same number**, because masks are stored as numbers) |
| TMP Essentials | Window → TextMeshPro → Import TMP Essential Resources |
| Active Input Handling | Use **Both** if the city's own scripts use the old `Input` class |

**Wire it into the city scene**
1. Drag in `Generated/Prefabs/CarSalesSystems.prefab` (once) and `Generated/Prefabs/Dealership.prefab`.
2. Move each `Spot_XX` onto real parking bays, and put `CustomerSpawn_*` and `CustomerExit` on sidewalks.
   Move the `Computer` and `ShopSign` into the building you chose.
3. On the **city's player**:
   - add `PlayerInteractor` → *Cam* = player camera, *Interact Mask* = Interactable,
     *Interact Action* = `CarSalesControls/CarSales/Interact`;
   - add `MenuInputBlocker` → drag in the city player's movement and camera scripts.
   - Delete the test `Player` (with `SimpleFirstPersonController`) if you copied it.
4. **NavMesh:** add a `NavMeshSurface` to an object around the dealership (*Collect Objects = Volume*,
   size it to the lot plus sidewalks) → **Bake**. Optionally add `RuntimeNavMeshBaker`.
5. Keep **one** `EventSystem`, using **Input System UI Input Module**.
6. If the city's scripts lock the cursor every frame, add them to `MenuInputBlocker` too.
7. Swap the placeholder car models for the city pack's vehicles (see *Add a new car model*).

## 9. Roadmap ideas
- Customer test drives (the customer drives, the player rides along)
- Cleaning mini-game (paint a dirt mask into a RenderTexture, read it in a Shader Graph)
- Loans, car auctions, and trade-ins (customer brings a car to sell)
- Staff (a salesperson NPC who negotiates automatically for commission)
- Lot upgrades (more spots → more customers), showroom decoration
- Hidden damage and inspection tools, car history reports
- Online reviews driven by reputation
- Real audio files (assign them in `AudioManager → Overrides`) and ambient city sound

## 10. v2 systems

All of these live on the **`CarSalesSystems`** prefab, so they travel with it into a city scene.

### Upgrades (`UpgradeManager`)
| Upgrade | Levels and cost | Effect per level |
|---|---|---|
| Lot Expansion | $15k, $35k | Unlocks 3 parking spots (`ParkingSpot.requiredLotLevel`) |
| Marketing | $8k, $20k, $45k | Spawn interval −15%, +1 max customer, budgets +10% |
| Mechanic Workshop | $10k, $25k, $50k | Repairs −15%. **Level 1+ shows true marketplace condition** |
| Detailing Bay | $4k, $12k | Cleaning −35% |
| Showroom Upgrade | $12k, $30k, $60k | +5% customer interest, +0.5 reputation per day |

Change names, costs and descriptions in the Inspector (`CarSalesSystems → UpgradeManager → Upgrades`).
Effects are the properties at the bottom of `UpgradeManager.cs`.

### Hidden condition (inspection)
`CarInstance.reportedCondition` is what the seller claims. 30% of sellers lie by +10–25%; the rest are within −5 to +8%.
The **asking price is based on the claim**, so lemons are overpriced. You learn the truth when you buy
(or before buying, with the Mechanic upgrade).

### Bank (`BankManager`)
- Limit = $25,000 + $1,000 × reputation. Interest is 2% of the loan per day, charged at closing time.
- Bankruptcy: `ProgressManager` counts days that **end** with negative cash. 3 in a row → game over
  (the save is deleted, so *Continue* can't load a bankrupt game).

### Goals & winning (`ProgressManager`)
13 sequential goals (tutorial first, then economy milestones) with cash rewards; the list is in `ProgressManager.Awake()`.
Goals use lifetime `GameStats`, so a later goal you already reached completes instantly when it comes up.
**Net worth** = cash + market value of owned cars − loan. Reaching `winNetWorth` (default $1,000,000) shows the win
screen. The player can keep playing afterwards.

### Test drive (`TestDriveManager` + `ArcadeCarController`)
The parked car is hidden, and a drivable copy (CharacterController-based arcade physics, no wheel colliders) spawns
in its place. The main camera becomes a chase camera and the walking player is frozen (`MenuInputBlocker.SetBlocked`).
It ends after 60 s, or when you press E. Side impacts above 7 m/s (~25 km/h) remove 2–12% condition.
If the day ends during a drive, the car is returned automatically.

### Menus, pause & settings
- `MainMenuUI` (MainMenu scene): *New Game* sets a PlayerPrefs flag that `SaveManager` reads, then loads the game scene.
- Esc with no menu open → `PauseMenuUI` (time is paused while Pause or Settings is open).
- `GameSettings` (static) stores mouse sensitivity and volume in PlayerPrefs.
- Modal panels (daily report, end screen) can't be closed with Esc.

### Audio (`AudioManager`)
Every sound is generated in code at startup (click, coin, purchase, error, notify, door chime, success, crash).
To use real files, add entries to **Overrides** (Sfx + AudioClip). Buttons play a click via `ButtonSound`.

### Day/night (`DayNightLighting`)
Rotates the scene's sun from morning (08:00) to sunset (18:00). In a city with its own lighting system,
untick **Drive Sun**.


## 11. v3 systems

### UI built from code
`UIManager.Awake()` creates **every** screen at runtime: HUD, computer (with its pages) and all windows.
Each screen is a `UIPanel` (or a `ComputerPage` inside the computer) with a `Build()` method that lays out its
widgets with `UIFactory` helpers (`Window`, `Panel`, `Btn`, `Text`, `HRow`, `VCol`, `ScrollList`, `ScrollGrid`,
`CarPicture`, `ProgressBar`, `Icon`, `Chip`, `SpecRow`, `KeyHint`...).

- **Re-theme everything**: change the colours at the top of `Scripts/UI/Framework/UIFactory.cs`.
- **Change a screen**: edit its `Build()` (e.g. `GarageUI.Build`). No prefabs to keep in sync.
- **Add a computer page**: create `class MyPage : ComputerPage`, then add `AddPage<MyPage>("My Page")` in `ComputerHubUI.Build`.
- Rounded corners, circles, the arrow and the check mark are generated textures, so there are no image assets to import.

### Car photos (`ThumbnailService`)
A tiny photo studio sits 500 m below the map on layer **21 (CarSalesStudio)**: floor, 3 lights and a camera.
When the UI asks for a car's picture, the model is spawned there with the right paint and dirt, rendered once
into a RenderTexture, and cached (re-rendered when paint or cleanliness changes). Normal cameras ignore layer 21.
To use real photos instead, assign `CarDefinition.thumbnail` (and adapt `UIFactory.SetCarPicture`).

### Minimap
`HUDController` creates an orthographic camera 80 m above the main camera, rendering into a circular mask.
`MinimapMarker` adds coloured dots on layer **22 (CarSalesMinimap)** that only the minimap sees:
blue = computer, green = car for sale, orange = needs repair, grey = other car, white/gold = customer (gold = interested).

### Per-part condition (`CarParts`)
Parts and weights: Engine 30%, Brakes 15%, Tires 10%, Body 20%, Interior 15%, Electronics 10%.
Overall condition = weighted average. Repair cost per part = (100 - part) × weight × basePrice × 0.004 × Mechanic discount.
A Full Service repairs everything for 10% less.

### Messages (`MessageManager`)
Every morning: 1-2 private-seller offers (honest condition, 60-80% of value, expire that day), sometimes a tip,
and a bank reminder if you have a loan. Goal completions are posted too. Messages are saved.


## 12. v4 systems

### Town layout (world metres, +Z = north)
| Place | Position | Built by |
|---|---|---|
| AUTO WORLD (main dealership, office computer) | (0, 0) | `BuildDealershipSite` |
| Downtown Motors / Luxury Lane | (-140, 0) / (140, 0) | `BuildDealershipSite` |
| Fuel Station / Mechanic Workshop / Car Wash / Used Car Market | (40,-38) / (-45,-38) / (100,-38) / (-100,-38) | south of the main road |
| Auction Center, Apartments, Office Tower | (-110,112), (-40,106), (-175,106) | north road |
| Starter Trailer, Suburban House, Hillside Villa, Parking Garage | (40,106), (95,106), (152,106), (200,106) | north road |

Each site is built in local space (front = local -Z towards the road) by `Editor/CarSalesWorldBuilder.cs`.
Move a whole site by moving its root object. Each dealership has its own `NavMeshSurface` (Volume) for customers.

### Where cars are (`VehicleRegistry`)
Every owned car has `location` = **Spot** (a dealership parking spot: `dealershipId` + `parkingSpotIndex`) or
**World** (`worldPosition` / `worldRotationY`). `VehicleRegistry` spawns all of them on load and handles
`PlaceAtSpot`, `PlaceInWorld`, `Refuel`, `CompleteSale`, `QuickSell`. Only cars **on a spot** and **listed** are seen by customers.
Driving a listed car off its spot un-lists it.

### Service zones (`ServiceZone`, `ServiceDesk`)
A zone is a box (Workshop, CarWash, FuelStation, Home, Auction). Cars inside it can be serviced at the desk
(or with E while driving inside it). Zone **bays** are where tow trucks drop cars.

### Driving (`TestDriveManager` + `ArcadeCarController`)
F enters the car you look at; F again parks it right there. Fuel: 4% per km (100% = 25 km), $0.90 per %.
Empty tank = no power → tow it. Context actions with E: park in a nearby free spot (4.5 m), refuel, wash, repair.

### Tow & taxi (`TowService`)
Tow: $120 + $0.80/m to a dealership spot or a zone bay. Taxi: $25 + $0.25/m and travel time (8 m per game minute).

### Day, energy & sleep (`DayCycleManager`)
1 game hour = 50 real seconds. Dealerships: 08:00–20:00. Energy = 100% at wake-up, falls linearly to 0% at 02:00.
Below 15% you walk slower. Sleep (front door of the home you live in) from 18:00, or earlier when energy < 25%.
Passing out at 02:00: $300 fee, wake with 60% energy. Sleeping pays rent ($500 + $50 per non-personal car + dealership rents),
loan interest, collects property income, generates new marketplace/lot/auction cars and messages, and autosaves.

### Properties (`PropertyManager`)
Edit the list in the Inspector (`CarSalesSystems → PropertyManager`). Types: Home (wake hour, daily reputation),
Dealership (daily rent; links to a `DealershipManager` by `propertyId`), Investment (daily income).
`PropertySign` objects in the world open the Real Estate page and disappear once bought.

### Auctions (`AuctionManager`, `AuctionUI`)
5 lots per day, 10:00–17:00. Starting bid = 35% of the claimed value. 2–4 AI bidders with secret maximums
(50–102% of the TRUE value). The 12 s timer resets to 6 s after every bid; you win if you're highest when it hits zero.
Won cars appear in the auction car park — drive or tow them home.

## 13. v5 vehicle framework (Phase 2)
- **Assemblies:** `CarSales.Foundation` (events, ledger, saves) ← `CarSales.Sim` (vehicle simulation, no Unity code)
  ← `CarSales.Runtime` (MonoBehaviours, UI). Logic in Foundation/Sim is covered by EditMode tests.
- **Driving:** `TestDriveManager.StartDrive` builds a driving object from the car's model. In Realistic mode it adds
  `WheelVehicleController.Setup(...)`: a Rigidbody, a box collider from the body, and a WheelCollider per wheel mesh
  (names containing wheel/tyre/tire; invisible wheels at the body corners if there are none). Each FixedUpdate:
  `DriveInput.Map` (W/S → throttle, brake, reverse) → `Powertrain.Step` (engine + gearbox → wheel torque) →
  TCS/ABS/ESC per wheel → steering limit → anti-roll bars → downforce. Arcade mode (or a failed setup) uses
  `ArcadeCarController`. Both implement `IVehicleDriver`.
- **Profiles:** `VehicleProfileResolver.For(CarDefinition)` → the definition's `physicsOverride` asset, else the preset
  `VehicleProfiles.For(class, powertrain)`. Class comes from the category (and price for hatchbacks/sedans),
  powertrain from `fuelType`. Tune with Vehicle Studio; profile assets live in `Assets/CarSales/Data/Physics/`.
- **Components:** `CarInstance.components` (list of `ComponentState {id, health}`) is the truth; `CarInstance.parts[6]`
  and `condition` are caches maintained by `CarParts` — always change condition through `CarParts`
  (`Set`, `Damage(car, zone, amount)`, `Wear`, `RepairAll`). `CarParts.Effects(car)` gives the `DrivingEffects`
  the physics uses. Component ids are saved as numbers: only add new ones (`ComponentCatalog`).
- **Dents:** `DentModel.ZoneDepths(components)` → depth per zone from the body panels; `CarDamageVisual.Apply(model, car)`
  copies the meshes (never the shared ones) and moves vertices near the damaged end/side. Called by `CarDisplay.Refresh`
  and when driving. Only meshes with Read/Write enabled can be dented.
- **Manual gearbox:** `GameSettings.ManualGearbox` → `Powertrain.Manual`; `WheelVehicleController` reads the `GearUp`/`GearDown`
  actions and calls `Powertrain.ShiftUp/ShiftDown`; `DriveInput.MapManual` makes W = throttle and S = brake.
- **Test track:** `CarSalesTestTrack.cs` (editor) builds the scene; `TestTrackController` (runtime) spawns cars on its own
  (sets `CarSalesServices.Database`, no other services) and measures performance.

## 14. Asset Studio (Phase 3)
- **Assemblies:** `CarSales.Studio.Core` (engine-free: licences, credits, scale/texture/validation heuristics, tested),
  `CarSales.Studio.Editor` (window, pipeline, build hooks). Wheel detection is in `CarSales.Sim.WheelDetection`
  and used by both the Studio and `WheelVehicleController`.
- **Folders:** `Assets/CarSales/Art/<Category>/<Name>/Source` (original files), `.../Materials` (extracted materials),
  `.../<Name>.prefab` (game-ready), `.../History` (previous versions); `Assets/CarSales/Data/Cars` (imported car
  definitions); `Assets/CarSales/Library/Resources/CarSalesAssetRegistry.asset` (records + licences).
- **Prefab layout:** root (name) → `Model` (the original model, scaled/turned/centred on the ground). Cars: `VehicleRig`
  (4 wheel transforms) and `CarVisual` (paint renderers + material slots) on the root; the game builds the physics
  around it at runtime.
- **Licences:** `LicencePolicy.Check` decides; `LicenceBuildCheck` (IPreprocessBuildWithReport) fails release builds with
  blocked assets and writes `CREDITS.txt` after each build.

## 15. UI design system (Phase 4)
- **Colours:** always `UIFactory.<Token>` (e.g. `UIFactory.CardBg`, `UIFactory.Good`, `UIFactory.HudBg`). They read
  `UITheme.Current` (`Scripts/UI/Framework/UITheme.cs`). To add a colour, add a field to `UIPalette`, give it a value
  in `Dark()` (Light/High contrast inherit it), and expose it in UIFactory. `UiLintTests` fails on literal colours.
- **Buttons:** `UIFactory.Btn` picks the label colour with `ContrastText`; `SetButtonColor` keeps it readable.
- **Windows:** `UIManager.Open` plays `UITransition` (fade + scale, unscaled time) and focuses the first button for gamepads.
- **Scale:** `GameSettings.UiScale` → CanvasScaler reference resolution (`GameSettings.UiReferenceResolution`).
- **Post-processing:** `CarSalesLooks.cs` (editor, compiled when URP is installed: `CARSALES_URP`).
- **Components:** `Scripts/UI/Framework/UIComponents.cs` — `UIFactory.Tabs`, `MakeTable` (rows = strings + sort keys),
  `Modal`/`Confirm`, `StatTile`, `Chart`, `Badge`, `EmptyState`. Build new screens from these.
- **Back:** `UIManager.Open` remembers the previous screen; `Back()` (Esc / B) asks the screen first (`IBackHandler`),
  then returns to the previous one. `Close()` / `CloseAll()` clears the history.
- **Computer:** `ComputerHubUI` = desktop + one app window; add an app with `AddPage<T>` and a `Tile(...)`.
- **Vehicle file:** `VehicleDetailsUI.Show(listing)`, `ShowPhysical(display)`, `ShowOwned(car)`; tabs are rebuilt on change.
- **Placeholders:** the wizard wraps replaceable primitives in `PlaceholderSlot` groups (`Slot(...)` in
  `CarSalesWorldBuilder.cs`); `Editor/CarSalesPlaceholders.cs` fits the chosen prefabs (`Data/PlaceholderReplacements.asset`).
- **Save thumbnails:** `SaveThumbnail.Capture(Camera.main, store.ThumbnailFile(slot))` after each save.

## 16. Inspection, faults, documents, workshop (Phase 5)
- **Data per car** (`CarInstance`): `identity`, `documents`, `history`, `faults`, `knowledge`. `CarRecords.Ensure` fills
  them once (seeded from the car's id) and is called from `CarParts.EnsureParts`/`Randomize`, so old saves upgrade themselves.
- **Rules** live in `Sim/` (engine-free, tested): `FaultCatalog`/`FaultModel`, `InspectionEngine`, `MotTest`,
  `VehicleRecords` (VIN, plate, generation, value multiplier), `WorkshopCatalog` (parts, jobs, prices, results).
- **Knowledge, not truth:** a fault's `known` flag; `history.checkedHistory`. `MarketValue(car, useReported: true)`
  only counts known problems. The UI lists only known faults.
- **WorkshopManager** (added by `CarSalesBootstrap` if missing): inspections, orders, garage jobs (advance with
  `DayCycle.OnTimeChanged`), DIY, MOT, paperwork, nightly fault progression, deliveries, repossession. Saved as module `workshop`.
- **Money:** new ledger categories Parts, Labour, Paperwork, Finance settlements.
- **Phase 5b:** `WorkStation` (bay box + console, `StationType`), `HandsOnSession`/`HandsOnSocket` (markers, hold E;
  steps from `Sim/Workshop/Board.cs`: `HandsOn`, `HandsOnInspection`), board cards and the mechanic in `WorkshopState`
  (`board`, `mechanicHired`; worked in `WorkshopManager.MechanicWork`), `PartsCounter` (sets `WorkshopManager.AtCounter`),
  world sites in `Editor/CarSalesServiceCentre.cs`.

## 17. Auctions (Phase 6)
- **Rules** (`Sim/Auction/AuctionEngine.cs`, tested): `AuctionRules` (fees, VAT, invoices, late fees, storage, reports,
  `GenerateBidders`, `MaxBid` (§8.3), `ResolveProxy`, `LiveAiBid`, `SoftClose`, `Hammer`, `SellerAcceptChance`).
- **AuctionManager** (runtime): `AuctionState` (save module `auction`); calendar (`EnsureSchedule`), lot cars generated
  and shaped per sale type, AI interest per lot (seeded), online lots updated twice a second, hall sales run live when
  `AuctionManager.PlayerInHall()` else instantly, invoices/payment/collection, nightly late fees and storage.
- **World:** `AuctionHall` (box, stage, screen), `AuctionYard` (bays; rebuilt when the yard lots change),
  `CarDisplay.InitializeForAuction` (lot number tag; E opens the lot). Overlay: `AuctionHallHUD`. Terminal: `AuctionUI`.

## 18. Market, customers, staff (Phase 7a)
- **Rules** (tested, `Sim/`): `MarketModel` (index, seasons, demand, supply, competitors, batches), `Buyers` (personas,
  `Score`, `MaxPrice`, `OpeningOffer`, trade-in, finance, reviews, fault spotting), `StaffRules` (wages, candidates,
  salesperson deal, mechanic speed, detailing, morale).
- **Runtime:** `MarketManager` (multiplier used by `CarValueCalculator`), `CrmManager` (records, reviews, complaints),
  `StaffManager` (team, wages on `WeekEnded`, detailers/drivers on the clock, salespeople via `CustomerAI`),
  `CustomerDeal` (quote + finishing a sale, shared by `NegotiationSession` and salespeople).
- All three managers are added by `CarSalesBootstrap` if missing and saved as modules `market`, `crm`, `staff`.
- **Phase 7b:** `Sim/Customers/Sourcing.cs` (`PrivateSellers`, `Listings`, `BuyerQuestions`, `AfterSale`), `SourcingManager`
  (ads + viewings at `SellerSpot`s with a `SellerNpc`, leads, warranties; module `sourcing`), `PrivateSellerUI`,
  `ListingEditorUI` (`CarInstance.listing`), `HandoverUI` (between the agreed price and `CustomerDeal.CompleteSale`),
  `NegotiationSession.Answer` / `TestDrive`, `CustomerAI.InspectAround`.

## 19. World (Phase 8a)
- **Rules** (tested): `Sim/World/WorldSim.cs` (weather Markov chain, grip/footfall/light/fog, bank holidays, Sunday hours,
  fuel walk, events), `TrafficRules` (gap keeping, lights, density), `Sim/World/RoadGraph.cs` (lanes from segments).
- **WorldManager** (module `world`): weather effects (`Grip` used by `WheelVehicleController`, `Footfall` by
  `CustomerSpawner`, `SunFactor` by `DayNightLighting`, `FuelIndex` by `VehicleRegistry`), events, open day.
- **TrafficSystem / TrafficCar / TrafficLight / PedestrianSystem**: added by `CarSalesBootstrap` if missing; read the
  town's `RoadNetwork` (falls back to `RoadGraph.DefaultTown()`).
- **World Builder:** `Editor/CarSalesWorldBuilderTool.cs` (`AddSiteToTown`, `BuildUserSite` → `BuildDealershipSite`
  with an optional imported building, `ValidateSite`), runtime `Site` (registers a buyable property) and `WorldLayout`.
- **Phase 8b:** `RoadEditing` (snap, split, scatter; tested), `WorldLayout.userRoads` + `Network()`, `BuildUserRoads` /
  `ApplyRoadsToTown` and the Scene-view drawing in `WorldBuilderWindow`, `SiteTemplate` builders in `BuildUserSite`,
  `PerformanceGovernor` (density scale + overlay), `GameSettings.TrafficLevel/ShowPerformance`.

## 20. Business (Phase 9)
- **Rules** (tested): `Sim/Business/Business.cs` (`BusinessRules`: margin VAT, returns, fines, credit rating, overdraft,
  insurance premiums/excess/payouts, security, utilities, rates, campaigns, diminishing returns, levels, development,
  analytics; `SaleRecord`, `VatReturn`).
- **BusinessManager** (module `business`): records every sale (`VehicleRegistry.RemoveSoldCar`; who bought via
  `SetSaleContext`), VAT returns on `QuarterEnded`, daily overdraft interest/utilities/marketing/VAT fines, weekly
  rates/insurance/security, claims (from `WorldManager` thefts and vandalism), levels, developments.
- **Hooks:** `EconomyManager.CanAfford` includes the overdraft; `ProgressManager` counts debt past the overdraft;
  `CustomerSpawner` (footfall, campaign personas, luxury gating), `SourcingManager` (leads), `StaffManager` (staff slots),
  `PropertyManager.IncomeOf` (developments).

## 21. Production readiness (Phase 10)
- **Settings** (`UI/SettingsUI.cs`): tabs General / Display / Audio / Driving / Controls. New `GameSettings`:
  `Subtitles`, `Language`, `GroupVolume(g)` / `SetGroupVolume`, `Difficulty` (0-4), theme 0-4 (`UIThemeChoice.ColourBlind`).
- **Rebinding:** `CarSalesInput.Asset` (the linked .inputactions, found from any resolved action), `Collection`,
  `AllActions()`, `SaveOverrides/LoadOverrides/ResetOverrides` (PlayerPrefs `CarSales.BindingOverrides`). Actions asked for
  by name (`Resolve(null, "Interact")`) are looked up in the asset first, so a rebind applies everywhere; the built-in
  fallback actions live in one map so they can be rebound too.
- **Strings:** `Foundation/Localization/Strings` (engine-free, tested) and `Core/Loc` (`T(key, english)`, `F(...)`,
  `Reload()`, `Available()`), table `Resources/Localization/<lang>.json`. Missing keys fall back to English, then to the
  text in code. `Phase10Tests.EnglishTable_HasEveryKeyTheGameUses_NoDuplicates` scans `Scripts/` for `Loc.T("key", "...")`.
- **Subtitles:** `UI/Subtitles.Say(speaker, text)`; called from `AuctionManager.LiveMessage` (player in the hall) and
  `CustomerAI.SetBubble` (camera within 12 m).
- **Audio:** `AudioManager.GroupOf(sfx)`, `ApplyGroupVolumes()`; `EngineAudio` uses the vehicles group;
  `AudioLibrary` (ScriptableObject in Resources, replaceable clips); `AmbienceManager` (added by `CarSalesBootstrap`)
  picks a zone every 0.5 s (`AuctionHall.Contains`, near a `WorkStation` / workshop `ServiceZone`, near a dealership,
  else street), crossfades two looping sources, adds rain from `WorldManager.Weather`.
- **Balance:** `Sim/Balance/Balance.cs` (`BalanceConfig` presets + JSON, `Sandbox.Run/Average`, `SandboxResult`);
  `Core/BalanceSettings` (resolve preset or `Resources/CarSalesBalance.json`); stored per save in
  `BusinessState.difficulty/balanceJson`. Used by `EconomyManager.ResetToStart` (starting money), `CustomerSpawner`
  (budgets, footfall) and `WorkshopManager.PartPrice/LabourCost` (repairs). Editor: `SimulationConfiguratorWindow`.
- **Credits:** `UI/CreditsUI` (`Text()` = game credits + `AssetRegistry.CreditsText`, `Show(parent, onClose)`).
- **Build:** `Editor/CarSalesBuild.cs` (`BuildWindowsRelease/Development`, CREDITS.txt, build_info.txt). The Asset Studio
  licence check still runs before every build.
- **Tests:** `Tests/PlayMode/GameFlowTests.cs` (needs the built scene; uses `SaveManager.SavesRootOverride`), and the
  save-migration matrix + balance + strings tests in `Tests/EditMode/Phase10Tests.cs`.
