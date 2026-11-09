using System.Collections.Generic;
using System.Linq;
using CarSales.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarSales
{
    /// <summary>
    /// Listing editor (spec §6.14.2): take photos from different angles, pick honest highlights, set the price with
    /// help (market value, the cheapest rival), and put the car online for enquiries. Quality → leads per day.
    /// </summary>
    public class ListingEditorUI : UIPanel
    {
        CarDisplay car;
        readonly RawImage[] photos = new RawImage[Listings.MaxPhotos];
        readonly Dictionary<Highlight, Button> highlightButtons = new Dictionary<Highlight, Button>();
        TextMeshProUGUI titleText, headlineText, helperText, qualityText, priceText;
        Slider priceSlider;
        Button onlineButton;
        UIFactory.Bar qualityBar;
        ListingInfo draft;
        int price;
        bool online;

        public override void Build()
        {
            var body = UIFactory.Window(Root, "Listing editor", new Vector2(1400f, 860f), CloseAll, out titleText, out _);
            UIFactory.Horizontal(body, 0, 24f, TextAnchor.UpperLeft).childForceExpandHeight = true;

            var left = UIFactory.VCol(body, 8f, "Photos", 640f);
            UIFactory.SectionTitle(left, "Photos (more angles = more enquiries)");
            var grid = UIFactory.Rect("Grid", left);
            UIFactory.LE(grid, h: 420);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(205f, 130f);
            g.spacing = new Vector2(8f, 8f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 3;
            for (int i = 0; i < photos.Length; i++)
            {
                var frame = UIFactory.Panel(grid, "Photo" + i, UIFactory.PictureBg, 1.6f);
                frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                photos[i] = UIFactory.Rect("Img", frame.transform).gameObject.AddComponent<RawImage>();
                UIFactory.Stretch(photos[i].rectTransform);
                photos[i].raycastTarget = false;
                var label = UIFactory.Text(frame.transform, $"Photo {i + 1}", 14, UIFactory.TextDim, TextAlignmentOptions.Center);
                UIFactory.Stretch(label.rectTransform);
            }
            UIFactory.Btn(left, "Take the next photo", UIFactory.Accent, TakePhoto, 44f, 16f);
            headlineText = UIFactory.Text(left, "", 18, UIFactory.TextMain, TextAlignmentOptions.TopLeft, true);
            headlineText.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(headlineText, h: 60);

            var right = UIFactory.VCol(body, 8f, "Details");
            UIFactory.SectionTitle(right, "Highlights (only true ones can be ticked)");
            var hl = UIFactory.Rect("Highlights", right);
            UIFactory.LE(hl, h: 150);
            var hg = hl.gameObject.AddComponent<GridLayoutGroup>();
            hg.cellSize = new Vector2(330f, 40f);
            hg.spacing = new Vector2(8f, 8f);
            hg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            hg.constraintCount = 2;
            foreach (Highlight h in System.Enum.GetValues(typeof(Highlight)))
            {
                var hh = h;
                highlightButtons[h] = UIFactory.Btn(hl, Listings.HighlightName(h), UIFactory.CardLight, () => Toggle(hh), 40f, 14f);
            }
            UIFactory.SectionTitle(right, "Price");
            priceText = UIFactory.Text(right, "", 34, UIFactory.Gold, TextAlignmentOptions.Left, true);
            UIFactory.LE(priceText, h: 44);
            priceSlider = UIFactory.MakeSlider(right, UIFactory.Accent, 30f);
            priceSlider.wholeNumbers = true;
            priceSlider.onValueChanged.AddListener(v => { price = Mathf.RoundToInt(v / 50f) * 50; UpdateTexts(); });
            helperText = UIFactory.Text(right, "", 15, UIFactory.TextDim, TextAlignmentOptions.TopLeft);
            helperText.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.LE(helperText, h: 70);
            onlineButton = UIFactory.Btn(right, "", UIFactory.CardLight, () => { online = !online; UpdateTexts(); }, 42f, 15f);
            UIFactory.SectionTitle(right, "Listing quality");
            qualityBar = UIFactory.ProgressBar(right, UIFactory.Good, 12f);
            qualityText = UIFactory.Text(right, "", 15, UIFactory.TextMain, TextAlignmentOptions.Left);
            UIFactory.LE(qualityText, h: 40);
            UIFactory.Spacer(right);
            var row = UIFactory.HRow(right, 52, 10, "Buttons");
            UIFactory.Btn(row, "List for sale", UIFactory.Good, Save, 50f, 18f);
            UIFactory.Btn(row, "Take off sale", UIFactory.Neutral, Unlist, 50f, 15f);
        }

        public void Show(CarDisplay target)
        {
            car = target;
            if (car.Data.listing == null) car.Data.listing = new ListingInfo();
            var l = car.Data.listing;
            draft = new ListingInfo { online = l.online, photos = l.photos, highlights = new List<int>(l.highlights), postedDay = l.postedDay, leads = l.leads };
            online = draft.online;
            int value = CarValueCalculator.MarketValue(car.Data);
            priceSlider.minValue = Mathf.Max(100, Mathf.RoundToInt(value * 0.6f));
            priceSlider.maxValue = Mathf.RoundToInt(value * 1.5f);
            price = car.Data.listedPrice > 0 ? car.Data.listedPrice : Mathf.RoundToInt(value * 1.05f / 100f) * 100;
            priceSlider.SetValueWithoutNotify(price);
            // Highlights that aren't true any more drop off.
            draft.highlights.RemoveAll(h => !Allowed((Highlight)h));
            OpenSelf();
            titleText.text = $"Listing editor  -  {car.Data.Name}";
            UpdateTexts();
        }

        bool Allowed(Highlight h)
        {
            var c = car.Data;
            switch (h)
            {
                case Highlight.FullServiceHistory: return c.documents.service == ServiceHistory.Full;
                case Highlight.NewMot: return !VehicleRecords.NeedsMot(CarRecords.ModelYear(c), CarRecords.Today) || c.documents.motExpiryDay - CarRecords.Today >= 180;
                case Highlight.LowMileage: return c.mileage < 60000;
                case Highlight.OneOwner: return c.documents.keepers <= 1;
                case Highlight.Valeted: return c.cleanliness >= 90f;
                case Highlight.FaultsListed: return InspectionEngine.KnownCount(c.faults) > 0;
                default: return true;
            }
        }

        void Toggle(Highlight h)
        {
            if (!Allowed(h)) { CarSalesServices.UI.Error(h == Highlight.Valeted ? "Clean the car first (90%+)." : "That isn't true for this car."); return; }
            if (draft.highlights.Contains((int)h)) draft.highlights.Remove((int)h); else draft.highlights.Add((int)h);
            UpdateTexts();
        }

        void TakePhoto()
        {
            if (draft.photos >= Listings.MaxPhotos) { CarSalesServices.UI.Notify("You have all six angles."); return; }
            draft.photos++;
            CarSalesServices.PlaySfx(Sfx.Click, 0.7f);
            UpdateTexts();
        }

        void UpdateTexts()
        {
            var c = car.Data;
            var def = car.Definition;
            for (int i = 0; i < photos.Length; i++)
            {
                bool has = i < draft.photos;
                var tex = has && ThumbnailService.Instance ? ThumbnailService.Instance.Get(c, i) : null;
                photos[i].texture = tex;
                photos[i].color = tex ? Color.white : UIFactory.Clear;
            }
            foreach (var pair in highlightButtons)
            {
                bool on = draft.highlights.Contains((int)pair.Key);
                UIFactory.SetButtonColor(pair.Value, on ? UIFactory.Good : Allowed(pair.Key) ? UIFactory.CardLight : UIFactory.Neutral);
            }
            int value = CarValueCalculator.MarketValue(c);
            var rival = CarSalesServices.Market ? CarSalesServices.Market.CheapestRival(def) : null;
            int cost = c.purchasePrice + c.repairSpent;
            priceText.text = UIFactory.Money(price) + $"  <size=50%><color={UIFactory.Hex(UIFactory.TextDim)}>{price / (float)Mathf.Max(1, value) * 100f:0}% of value  -  profit {UIFactory.Money(price - cost)}</color></size>";
            helperText.text = $"Market value {UIFactory.Money(value)}.  " +
                              (rival != null ? $"Cheapest rival: {rival.dealer}, {CarSalesServices.Database.Get(rival.carId)?.displayName} at {UIFactory.Money(rival.price)} ({rival.condition:0}%, {rival.mileage / 1000}k km).  " : "") +
                              $"Most buyers look at cars around {UIFactory.Money(Mathf.RoundToInt(value * 0.95f / 50f) * 50)}-{UIFactory.Money(Mathf.RoundToInt(value * 1.08f / 50f) * 50)}.";
            onlineButton.GetComponentInChildren<TextMeshProUGUI>().text = online ? "Advertised online: ON (enquiries come by message)" : "Advertise online: OFF (walk-ins only)";
            UIFactory.SetButtonColor(onlineButton, online ? UIFactory.Accent : UIFactory.CardLight);
            float q = Listings.Quality(draft.photos, draft.highlights.Count(h => h != (int)Highlight.FaultsListed), draft.highlights.Contains((int)Highlight.FaultsListed),
                                       price / (float)Mathf.Max(1, value), CarSalesServices.Crm ? CarSalesServices.Crm.AverageStars : 0f);
            qualityBar.Set(q);
            qualityBar.SetColor(q >= 0.6f ? UIFactory.Good : q >= 0.35f ? UIFactory.Warn : UIFactory.Bad);
            bool recep = CarSalesServices.Staff && CarSalesServices.Staff.Working(StaffRole.Receptionist).Any();
            qualityText.text = $"{q * 100f:0}%  -  " + (online ? $"about {Listings.LeadsPerDay(q, recep):0.0} enquiries a day" : "put it online to get enquiries") +
                               (draft.leads > 0 ? $"  -  {draft.leads} so far" : "");
            headlineText.text = Listings.Headline(def.displayName, def.year, c.mileage, draft.highlights);
        }

        void Save()
        {
            var c = car.Data;
            if (c.personal) { CarSalesServices.UI.Error("This is your personal car."); return; }
            if (c.location != CarLocation.Spot) { CarSalesServices.UI.Error("Park the car on one of your dealership spots to sell it."); return; }
            if (c.state != CarState.Listed && CarSalesServices.Progress) CarSalesServices.Progress.Stats.carsListed++;
            draft.online = online;
            draft.headline = headlineText.text;
            if (draft.postedDay == 0) draft.postedDay = CarRecords.Today;
            c.listing = draft;
            c.listedPrice = price;
            c.state = CarState.Listed;
            car.Refresh();
            CarSalesServices.Inventory.NotifyChanged();
            CarSalesServices.UI.Success("Listed for sale", $"{c.Name} at {UIFactory.Money(price)}{(online ? " - advertised online" : "")}.");
            CloseAll();
        }

        void Unlist()
        {
            var c = car.Data;
            c.state = CarValueCalculator.DefaultState(c);
            if (c.listing != null) c.listing.online = false;
            car.Refresh();
            CarSalesServices.Inventory.NotifyChanged();
            CloseAll();
        }
    }
}
