using GLTFast.Schema;
using System;
using System.Collections.Generic;
using System.Drawing;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEditor.Recorder.OutputPath;

public class GameplayUIManager : MonoBehaviour
{
    private VisualElement root;

    // Stat sliders (read-only, show selected class stats)
    private Slider _popularitySlider;
    private Slider _troubleMakerSlider;
    private Slider _cashSlider;
    private Label _cashValueLabel;

    private Label _dayLabel;
    private Label _FratNameLabel;
    private NetworkedParty _partyDetails;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        _popularitySlider = root.Q<Slider>("Popularity");
        _troubleMakerSlider = root.Q<Slider>("TroubleMaker");
        _cashSlider = root.Q<Slider>("Cash");
        _cashValueLabel = root.Q<Label>("CashValueText");
        _dayLabel = root.Q<Label>("DayNumber");
        _FratNameLabel = root.Q<Label>("FratName");
        _partyDetails = NetworkedPartyManager.Instance.NetworkedParty;
        _dayLabel.dataSource = _partyDetails;
        _FratNameLabel.dataSource = NetworkedGameManager.Instance.fraternityLocal;

        SetupStatSliders();
        RefreshCashValueLabelFromManager();
    }

    private void OnEnable()
    {
        // Game manager
        NetworkedGameManager.Instance.OnPopularityChanged += UpdatePopularitySlider;
        NetworkedGameManager.Instance.OnTroubleChanged += UpdateTroubleSlider;
        NetworkedGameManager.Instance.OnCashValueChanged += UpdateCashSlider;

        // Party Manager
        // NetworkedPartyManager.Instance.NetworkedParty.OnDayNumberChangedEvent += UpdateDayNumber;

        // For late joiners
        if (NetworkedGameManager.Instance != null)
        {
            //UpdatePopularitySlider(NetworkedGameManager.Instance.TotalPopularity);
        }
    }

    private void UpdateDayNumber(int value)
    {

        _dayLabel.text = value.ToString();
    }

    private void OnDisable()
    {
        NetworkedGameManager.Instance.OnPopularityChanged -= UpdatePopularitySlider;
        NetworkedGameManager.Instance.OnTroubleChanged -= UpdateTroubleSlider;
        NetworkedGameManager.Instance.OnCashValueChanged -= UpdateCashSlider;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void SetupStatSliders()
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

    private void UpdatePopularitySlider(float newValue)
    {
        _popularitySlider.value = InvertForSlider(newValue, _popularitySlider.lowValue, _popularitySlider.highValue);
    }
    private void UpdateCashSlider(float newValue)
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

    private void UpdateTroubleSlider(float newValue)
    {
        _troubleMakerSlider.value = InvertForSlider(newValue, _troubleMakerSlider.lowValue, _troubleMakerSlider.highValue);
    }

    /// <summary>
    /// Inverts value for display on inverted sliders: actual 20 should show visually as 20.
    /// </summary>
    private static float InvertForSlider(float actualValue, float lowValue, float highValue)
    {
        return highValue + lowValue - actualValue;
    }
}
