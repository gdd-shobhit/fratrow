using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class GameplayUIManager : MonoBehaviour
{
    private VisualElement root;

    private Slider _popularitySlider;
    private Slider _troubleMakerSlider;
    private Slider _cashSlider;
    private Label _cashValueLabel;

    private Label _dayLabel;
    private Label _fratNameLabel;
    private Label _fratGradeLabel;
    private Label _fratRatingLabel;
    private NetworkedParty _partyDetails;
    Coroutine _fratBindRoutine;

    void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        _popularitySlider = root.Q<Slider>("Popularity");
        _troubleMakerSlider = root.Q<Slider>("TroubleMaker");
        _cashSlider = root.Q<Slider>("Cash");
        _cashValueLabel = root.Q<Label>("CashValueText");
        _dayLabel = root.Q<Label>("DayNumber");
        _fratNameLabel = root.Q<Label>("FratName");
        _fratGradeLabel = root.Q<Label>("FratGrade");
        _fratRatingLabel = root.Q<Label>("FratRating");
        _partyDetails = NetworkedPartyManager.Instance != null
            ? NetworkedPartyManager.Instance.NetworkedParty
            : null;

        if (_dayLabel != null && _partyDetails != null)
        {
            _dayLabel.dataSource = _partyDetails;
        }

        SetupStatSliders();
        RefreshCashValueLabelFromManager();
        BindFraternityUi();
        if (_fratBindRoutine != null)
        {
            StopCoroutine(_fratBindRoutine);
        }
        _fratBindRoutine = StartCoroutine(BindFraternityWhenReady());
    }

    void OnEnable()
    {
        BindFraternityUi();
        if (NetworkedGameManager.Instance == null)
        {
            return;
        }

        NetworkedGameManager.Instance.OnPopularityChanged += UpdatePopularitySlider;
        NetworkedGameManager.Instance.OnTroubleChanged += UpdateTroubleSlider;
        NetworkedGameManager.Instance.OnCashValueChanged += UpdateCashSlider;

        if (_partyDetails != null)
        {
            _partyDetails.OnDayNumberChangedEvent += UpdateDayNumber;
        }
    }

    void OnDisable()
    {
        if (_fratBindRoutine != null)
        {
            StopCoroutine(_fratBindRoutine);
            _fratBindRoutine = null;
        }

        if (NetworkedGameManager.Instance != null)
        {
            NetworkedGameManager.Instance.OnPopularityChanged -= UpdatePopularitySlider;
            NetworkedGameManager.Instance.OnTroubleChanged -= UpdateTroubleSlider;
            NetworkedGameManager.Instance.OnCashValueChanged -= UpdateCashSlider;
            NetworkedGameManager.Instance.OnFraternityChanged -= OnFraternityChanged;
        }

        if (_partyDetails != null)
        {
            _partyDetails.OnDayNumberChangedEvent -= UpdateDayNumber;
        }
    }

    IEnumerator BindFraternityWhenReady()
    {
        for (var i = 0; i < 120; i++)
        {
            if (BindFraternityUi())
            {
                yield break;
            }

            yield return null;
        }
    }

    bool BindFraternityUi()
    {
        var gm = NetworkedGameManager.Instance;
        if (gm == null)
        {
            return false;
        }

        gm.OnFraternityChanged -= OnFraternityChanged;
        gm.OnFraternityChanged += OnFraternityChanged;
        RefreshFraternityPanel();
        return gm.IsSpawned;
    }

    void UpdateDayNumber(int value)
    {
        if (_dayLabel != null)
        {
            _dayLabel.text = value.ToString();
        }
    }

    void SetupStatSliders()
    {
        if (_popularitySlider != null)
        {
            _popularitySlider.lowValue = 0f;
            _popularitySlider.highValue = 100f;
            _popularitySlider.value = 0f;
        }
        if (_troubleMakerSlider != null)
        {
            _troubleMakerSlider.lowValue = 0f;
            _troubleMakerSlider.highValue = 100f;
            _troubleMakerSlider.value = 0f;
        }
        if (_cashSlider != null)
        {
            _cashSlider.lowValue = 0f;
            _cashSlider.highValue = 100f;
            _cashSlider.value = 0f;
        }
    }

    void OnFraternityChanged(FraternityData data) => ApplyFraternityPanel(data);

    void RefreshFraternityPanel()
    {
        var gm = NetworkedGameManager.Instance;
        if (gm == null)
        {
            return;
        }

        var data = gm.IsSpawned ? gm.myFraternity.Value : gm.fraternityLocal;
        ApplyFraternityPanel(data);
    }

    void ApplyFraternityPanel(FraternityData data)
    {
        // Default struct is Grade A + rating 0 — show starting C/50 until server seeds.
        if (FraternityGradeCalculator.IsUninitialized(data))
        {
            data = FraternityGradeCalculator.CreateStarting(
                string.IsNullOrWhiteSpace(data.fratName) ? "Frat Test Name" : data.fratName);
        }

        if (_fratNameLabel != null)
        {
            _fratNameLabel.text = string.IsNullOrWhiteSpace(data.fratName) ? "Fraternity" : data.fratName;
        }

        if (_fratGradeLabel != null)
        {
            _fratGradeLabel.text = FraternityGradeCalculator.ToLetter(data.grade);
        }

        if (_fratRatingLabel != null)
        {
            _fratRatingLabel.text = $"Rating {data.rating}";
        }
    }

    void UpdatePopularitySlider(float newValue)
    {
        if (_popularitySlider != null)
        {
            _popularitySlider.value = InvertForSlider(newValue, _popularitySlider.lowValue, _popularitySlider.highValue);
        }
    }

    void UpdateCashSlider(float newValue)
    {
        if (_cashSlider != null)
        {
            _cashSlider.value = InvertForSlider(newValue, _cashSlider.lowValue, _cashSlider.highValue);
        }
        SetCashValueLabelText(newValue);
    }

    void RefreshCashValueLabelFromManager()
    {
        if (NetworkedGameManager.Instance != null)
        {
            SetCashValueLabelText(NetworkedGameManager.Instance.TotalCashValue);
        }
    }

    void SetCashValueLabelText(float cashAmount)
    {
        if (_cashValueLabel == null)
        {
            return;
        }
        _cashValueLabel.text = $"Cash: {cashAmount:F2}";
    }

    void UpdateTroubleSlider(float newValue)
    {
        if (_troubleMakerSlider != null)
        {
            _troubleMakerSlider.value = InvertForSlider(newValue, _troubleMakerSlider.lowValue, _troubleMakerSlider.highValue);
        }
    }

    static float InvertForSlider(float actualValue, float lowValue, float highValue)
    {
        return highValue + lowValue - actualValue;
    }
}
