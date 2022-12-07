using System.Linq;
using Content.Shared.Humanoid.Markings;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Client.Utility;
using Robust.Shared.Network;


using Content.Client.White.Sponsors;



namespace Content.Client.Humanoid;

[GenerateTypedNameReferences]
public sealed partial class SingleMarkingPicker : BoxContainer
{
    [Dependency] private MarkingManager _markingManager = default!;
    [Dependency] private readonly SponsorsManager _sponsorsManager = default!;

    /// <summary>
    ///     What happens if a marking is selected.
    ///     It will send the 'slot' (marking index)
    ///     and the selected marking's ID.
    /// </summary>
    public Action<(int slot, string id)>? OnMarkingSelect;
    /// <summary>
    ///     What happens if a slot is removed.
    ///     This will send the 'slot' (marking index).
    /// </summary>
    public Action<int>? OnSlotRemove;

    /// <summary>
    ///     What happens when a slot is added.
    /// </summary>
    public Action? OnSlotAdd;

    /// <summary>
    ///     What happens if a marking's color is changed.
    ///     Sends a 'slot' number, and the marking in question.
    /// </summary>
    public Action<(int slot, Marking marking)>? OnColorChanged;

    // current selected slot
    private int _slot = -1;
    private int Slot
    {
        get
        {
            if (_markings == null || _markings.Count == 0)
            {
                _slot = -1;
            }
            else if (_slot == -1)
            {
                _slot = 0;
            }

            return _slot;
        }
        set
        {
            if (_markings == null || _markings.Count == 0)
            {
                _slot = -1;
                return;
            }

            _slot = value;
            _ignoreItemSelected = true;

            foreach (var item in MarkingList)
            {
                item.Selected = (string) item.Metadata! == _markings[_slot].MarkingId;
            }

            _ignoreItemSelected = false;
            PopulateColors();
        }
    }

    // amount of slots to show
    private int _totalPoints;

    private bool _ignoreItemSelected;

    private MarkingCategories _category;
    public MarkingCategories Category
    {
        get => _category;
        set
        {
            _category = value;
            CategoryName.Text = Loc.GetString($"markings-category-{_category}");

            if (!string.IsNullOrEmpty(_species))
            {
                PopulateList();
            }
        }
    }
    private IReadOnlyDictionary<string, MarkingPrototype>? _markingPrototypeCache;

    private string? _species;
    private List<Marking>? _markings;

    private int PointsLeft
    {
        get
        {
            if (_markings == null)
            {
                return 0;
            }

            if (_totalPoints < 0)
            {
                return -1;
            }

            return _totalPoints - _markings.Count;
        }
    }

    private int PointsUsed => _markings?.Count ?? 0;

    public SingleMarkingPicker()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        MarkingList.OnItemSelected += SelectMarking;
        AddButton.OnPressed += _ =>
        {
            OnSlotAdd!();
        };

        SlotSelector.OnItemSelected += args =>
        {
            Slot = args.Button.SelectedId;
        };

        RemoveButton.OnPressed += _ =>
        {
            OnSlotRemove!(_slot);
        };
    }

    public void UpdateData(List<Marking> markings, string species, int totalPoints)
    {
        _markings = markings;
        _species = species;
        _totalPoints = totalPoints;

        _markingPrototypeCache = _markingManager.MarkingsByCategoryAndSpecies(Category, _species);

        Visible = _markingPrototypeCache.Count != 0;
        if (_markingPrototypeCache.Count == 0)
        {
            return;
        }

        PopulateList();
        PopulateColors();
        PopulateSlotSelector();
    }

    public void PopulateList()
    {
        if (string.IsNullOrEmpty(_species))
        {
            throw new ArgumentException("Tried to populate marking list without a set species!");
        }

        _markingPrototypeCache ??= _markingManager.MarkingsByCategoryAndSpecies(Category, _species);

        MarkingSelectorContainer.Visible = _markings != null && _markings.Count != 0;
        if (_markings == null || _markings.Count == 0)
        {
            return;
        }

        MarkingList.Clear();

        foreach (var (id, marking) in _markingPrototypeCache)
        {
            var item = MarkingList.AddItem(Loc.GetString($"marking-{id}"), marking.Sprites[0].Frame0());
            item.Metadata = marking.ID;

            if (marking.SponsorOnly)
            {
                item.Disabled = true;
                if (_sponsorsManager.TryGetInfo(out var sponsor))
                {
                    item.Disabled = !sponsor.AllowedMarkings.Contains(marking.ID);
                }
            }

            if (_markings[Slot].MarkingId == id)
            {
                _ignoreItemSelected = true;
                item.Selected = true;
                _ignoreItemSelected = false;
            }
        }
    }

    private void PopulateColors()
    {
        if (_markings == null
            || _markings.Count == 0
            || !_markingManager.TryGetMarking(_markings[Slot], out var proto))
        {
            return;
        }

        var marking = _markings[Slot];

        ColorSelectorContainer.DisposeAllChildren();
        ColorSelectorContainer.RemoveAllChildren();

        if (marking.MarkingColors.Count != proto.Sprites.Count)
        {
            marking = new Marking(marking.MarkingId, proto.Sprites.Count);
        }

        for (var i = 0; i < marking.MarkingColors.Count; i++)
        {
            var selector = new ColorSelectorSliders
            {
                HorizontalExpand = true
            };
            selector.Color = marking.MarkingColors[i];

            var colorIndex = i;
            selector.OnColorChanged += color =>
            {
                marking.SetColor(colorIndex, color);
                OnColorChanged!((_slot, marking));
            };

            ColorSelectorContainer.AddChild(selector);
        }
    }

    private void SelectMarking(ItemList.ItemListSelectedEventArgs args)
    {
        if (_ignoreItemSelected)
        {
            return;
        }

        var id = (string) MarkingList[args.ItemIndex].Metadata!;
        if (!_markingManager.Markings.TryGetValue(id, out var proto))
        {
            throw new ArgumentException("Attempted to select non-existent marking.");
        }

        var oldMarking = _markings![Slot];
        _markings[Slot] = proto.AsMarking();

        for (var i = 0; i < _markings[Slot].MarkingColors.Count && i < oldMarking.MarkingColors.Count; i++)
        {
            _markings[Slot].SetColor(i, oldMarking.MarkingColors[i]);
        }

        PopulateColors();

        OnMarkingSelect!((_slot, id));
    }

    // Slot logic

    private void PopulateSlotSelector()
    {
        SlotSelector.Visible = Slot >= 0;
        AddButton.HorizontalExpand = Slot < 0;
        RemoveButton.HorizontalExpand = Slot < 0;
        AddButton.Disabled = PointsLeft == 0 && _totalPoints > -1 ;
        RemoveButton.Disabled = PointsUsed == 0;
        SlotSelector.Clear();

        if (Slot < 0)
        {
            return;
        }

        for (var i = 0; i < PointsUsed; i++)
        {
            SlotSelector.AddItem($"Slot {i + 1}", i);

            if (i == _slot)
            {
                SlotSelector.SelectId(i);
            }
        }
    }
}
