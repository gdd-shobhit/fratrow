using System;
using System.Collections.Generic;
using StarterAssets;
using Systems.Inventory;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Systems.Shop
{
    /// <summary>
    /// Add to a GameObject with <see cref="UIDocument"/>; assign <c>Shop.uxml</c> as the Panel Settings / Source Asset.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ShopView : MonoBehaviour
    {
        [SerializeField] UIDocument document;

        VisualElement _root;
        ScrollView _scroll;
        VisualElement _filterStrip;
        Button _closeButton;
        ShopOwner _activeOwner;
        readonly List<VisualElement> _rows = new List<VisualElement>();

        /// <summary><c>null</c> = show all types.</summary>
        ItemType? _activeTypeFilter;

        void Awake()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }
        }

        void OnEnable()
        {
            if (document != null && document.rootVisualElement != null)
            {
                Bind(document.rootVisualElement);
            }
        }

        void Start()
        {
            if (document != null)
            {
                Bind(document.rootVisualElement);
            }
        }

        void Bind(VisualElement root)
        {
            if (root == null || _root == root)
            {
                return;
            }
            _root = root.Q<VisualElement>("shop-root");
            _scroll = root.Q<ScrollView>("ShopItemList");
            _filterStrip = root.Q<VisualElement>("shop-filters");
            _closeButton = root.Q<Button>("shop-close");
            if (_closeButton != null)
            {
                _closeButton.clicked -= OnCloseClicked;
                _closeButton.clicked += OnCloseClicked;
            }
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        public void Show(ShopOwner owner)
        {
            if (document != null && document.rootVisualElement != null)
            {
                Bind(document.rootVisualElement);
            }
            if (_root == null || _scroll == null || owner == null)
            {
                Debug.LogWarning("[ShopView] Missing UI or owner.");
                return;
            }

            UnsubscribeOwner();
            _activeOwner = owner;
            _activeOwner.OnStockChanged += RefreshStocks;
            _root.style.display = DisplayStyle.Flex;
            _activeTypeFilter = null;
            BuildFilterButtons();
            RebuildRows();
            ApplyCursor(true);
        }

        public void Hide()
        {
            UnsubscribeOwner();
            if (_root != null)
            {
                _root.style.display = DisplayStyle.None;
            }
            ApplyCursor(false);
        }

        void UnsubscribeOwner()
        {
            if (_activeOwner != null)
            {
                _activeOwner.OnStockChanged -= RefreshStocks;
                _activeOwner = null;
            }
        }

        void OnCloseClicked() => Hide();

        void BuildFilterButtons()
        {
            if (_filterStrip == null)
            {
                return;
            }

            _filterStrip.Clear();

            var allBtn = new Button(() => SetTypeFilter(null))
            {
                text = "All"
            };
            allBtn.AddToClassList("shop-filter-btn");
            allBtn.AddToClassList("shop-filter-btn--all");
            _filterStrip.Add(allBtn);

            foreach (ItemType t in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                var captured = t;
                var btn = new Button(() => SetTypeFilter(captured))
                {
                    text = ItemTypeDisplayName(t)
                };
                btn.AddToClassList("shop-filter-btn");
                btn.AddToClassList(ItemTypeClass(t));
                _filterStrip.Add(btn);
            }

            UpdateFilterButtonStyles();
        }

        void SetTypeFilter(ItemType? type)
        {
            _activeTypeFilter = type;
            UpdateFilterButtonStyles();
            RebuildRows();
        }

        void UpdateFilterButtonStyles()
        {
            if (_filterStrip == null)
            {
                return;
            }

            var index = 0;
            foreach (var child in _filterStrip.Children())
            {
                if (child is not Button btn)
                {
                    continue;
                }

                btn.RemoveFromClassList("shop-filter-btn--active");
                var isActive = index == 0
                    ? !_activeTypeFilter.HasValue
                    : _activeTypeFilter == (ItemType)(index - 1);
                if (isActive)
                {
                    btn.AddToClassList("shop-filter-btn--active");
                }

                index++;
            }
        }

        void OnDestroy()
        {
            UnsubscribeOwner();
            if (_closeButton != null)
            {
                _closeButton.clicked -= OnCloseClicked;
            }
        }

        void RebuildRows()
        {
            _scroll.Clear();
            _rows.Clear();
            if (_activeOwner == null)
            {
                return;
            }

            IReadOnlyList<ItemDetails> items = _activeOwner.Catalog;
            var displayOrder = new List<int>();
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    displayOrder.Add(i);
                }
            }

            displayOrder.Sort((i, j) => ShopOwner.CompareCatalogOrder(items[i], items[j]));

            var filteredOrder = new List<int>();
            foreach (var i in displayOrder)
            {
                if (_activeTypeFilter.HasValue && items[i].Type != _activeTypeFilter.Value)
                {
                    continue;
                }

                filteredOrder.Add(i);
            }

            foreach (var i in filteredOrder)
            {
                var def = items[i];

                var row = new VisualElement();
                row.AddToClassList("shop-item-row");
                row.userData = i;

                var iconWrap = new VisualElement();
                iconWrap.AddToClassList("shop-item-icon-wrap");
                var iconImage = new Image();
                iconImage.AddToClassList("shop-item-icon");
                var rowIcon = def.Icon;
                if (rowIcon != null)
                {
                    iconImage.sprite = rowIcon;
                    iconImage.scaleMode = ScaleMode.ScaleToFit;
                }
                else
                {
                    iconWrap.style.display = DisplayStyle.None;
                }
                iconWrap.Add(iconImage);

                var main = new VisualElement();
                main.AddToClassList("shop-item-main");

                var nameLabel = new Label(def.DisplayName);
                nameLabel.AddToClassList("shop-item-name");

                var tagsRow = new VisualElement();
                tagsRow.AddToClassList("shop-item-tags-row");

                var rarityLabel = new Label(def.Rarity.ToString());
                rarityLabel.AddToClassList("shop-item-rarity");
                ApplyRarityClass(rarityLabel, def.Rarity);

                var typeLabel = new Label(ItemTypeDisplayName(def.Type));
                typeLabel.AddToClassList("shop-item-type");
                ApplyItemTypeClass(typeLabel, def.Type);

                tagsRow.Add(rarityLabel);
                tagsRow.Add(typeLabel);

                var economyRow = new VisualElement();
                economyRow.AddToClassList("shop-item-economy-row");

                var costLabel = new Label($"Cost: {def.ShopCost}");
                costLabel.AddToClassList("shop-item-cost");

                var stockLabel = new Label
                {
                    name = $"stock-{i}",
                    text = StockText(_activeOwner.GetStock(i))
                };
                stockLabel.AddToClassList("shop-item-stock");

                economyRow.Add(costLabel);
                economyRow.Add(stockLabel);

                main.Add(nameLabel);
                main.Add(tagsRow);
                main.Add(economyRow);

                var catalogIndex = i;
                var buy = new Button(() => OnBuyClicked(catalogIndex))
                {
                    text = "Buy",
                    name = $"buy-{catalogIndex}"
                };
                buy.AddToClassList("button");
                buy.AddToClassList("shop-buy");
                buy.SetEnabled(_activeOwner.GetStock(i) > 0);

                row.Add(iconWrap);
                row.Add(main);
                row.Add(buy);
                _scroll.contentContainer.Add(row);
                _rows.Add(row);
            }
        }

        void OnBuyClicked(int index)
        {
            if (_activeOwner != null)
            {
                _activeOwner.RequestPurchase(index);
            }
        }

        void RefreshStocks()
        {
            if (_activeOwner == null)
            {
                return;
            }
            foreach (var row in _rows)
            {
                if (row.userData is not int catalogIndex)
                {
                    continue;
                }
                var stock = _activeOwner.GetStock(catalogIndex);
                var stockLabel = row.Q<Label>($"stock-{catalogIndex}");
                if (stockLabel != null)
                {
                    stockLabel.text = StockText(stock);
                }
                var buy = row.Q<Button>($"buy-{catalogIndex}");
                if (buy != null)
                {
                    buy.SetEnabled(stock > 0);
                }
            }
        }

        static string StockText(int amount) => amount <= 0 ? "Out of stock" : $"Stock: {amount}";

        static void ApplyRarityClass(Label label, ItemRarity rarity)
        {
            label.RemoveFromClassList("rarity-common");
            label.RemoveFromClassList("rarity-uncommon");
            label.RemoveFromClassList("rarity-rare");
            label.RemoveFromClassList("rarity-epic");
            label.RemoveFromClassList("rarity-legendary");
            label.AddToClassList(RarityClass(rarity));
        }

        static string RarityClass(ItemRarity r)
        {
            return r switch
            {
                ItemRarity.Uncommon => "rarity-uncommon",
                ItemRarity.Rare => "rarity-rare",
                ItemRarity.Epic => "rarity-epic",
                ItemRarity.Legendary => "rarity-legendary",
                _ => "rarity-common"
            };
        }

        static void ApplyItemTypeClass(Label label, ItemType itemType)
        {
            label.RemoveFromClassList("item-type-alcohol");
            label.RemoveFromClassList("item-type-food");
            label.RemoveFromClassList("item-type-mixer");
            label.RemoveFromClassList("item-type-drinks");
            label.RemoveFromClassList("item-type-misc");
            label.RemoveFromClassList("item-type-fruits");
            label.RemoveFromClassList("item-type-herbs-spices-aromatics");
            label.RemoveFromClassList("item-type-sweeteners");
            label.RemoveFromClassList("item-type-liqueurs");
            label.AddToClassList(ItemTypeClass(itemType));
        }

        static string ItemTypeClass(ItemType t)
        {
            return t switch
            {
                ItemType.Alcohol => "item-type-alcohol",
                ItemType.Food => "item-type-food",
                ItemType.Mixer => "item-type-mixer",
                ItemType.Drinks => "item-type-drinks",
                ItemType.Misc => "item-type-misc",
                ItemType.Fruits => "item-type-fruits",
                ItemType.HerbsSpicesAromatics => "item-type-herbs-spices-aromatics",
                ItemType.Sweeteners => "item-type-sweeteners",
                ItemType.Liqueurs => "item-type-liqueurs",
                _ => "item-type-misc"
            };
        }

        /// <summary>Friendly shop labels (enum names are identifiers only).</summary>
        static string ItemTypeDisplayName(ItemType t)
        {
            return t switch
            {
                ItemType.HerbsSpicesAromatics => "Herbs, Spices & Aromatics",
                _ => t.ToString()
            };
        }

        static void ApplyCursor(bool shopOpen)
        {
            var inputs = FindLocalPlayerInputs();
            inputs?.SetInventoryUIMode(shopOpen);
        }

        static StarterAssetsInputs FindLocalPlayerInputs()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var player = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (player != null)
                {
                    var inp = player.GetComponent<StarterAssetsInputs>();
                    if (inp != null)
                    {
                        return inp;
                    }
                }
            }
            return FindFirstObjectByType<StarterAssetsInputs>();
        }
    }
}
