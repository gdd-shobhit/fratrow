using GLTFast.Schema;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;
using UnityEngine.UIElements;

public class GameplayUI : MonoBehaviour
{
    private VisualElement root;

    // Stat sliders (read-only, show selected class stats)
    private Slider _popularitySlider;
    private Slider _troubleMakerSlider;
    private Slider _cashSlider;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        _popularitySlider = root.Q<Slider>("Popularity");
        _troubleMakerSlider = root.Q<Slider>("TroubleMaker");
        _cashSlider = root.Q<Slider>("Cash");
        SetupStatSliders();
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
            _popularitySlider.RegisterValueChangedCallback(OnStatSliderValueChanged);
        }
        if (_troubleMakerSlider != null)
        {
            _troubleMakerSlider.lowValue = -100f;
            _troubleMakerSlider.highValue = 100f;
            _troubleMakerSlider.RegisterValueChangedCallback(OnStatSliderValueChanged);
        }
        if (_cashSlider != null)
        {
            _cashSlider.lowValue = 0f;
            _cashSlider.highValue = 100f;
            _cashSlider.RegisterValueChangedCallback(OnStatSliderValueChanged);
        }
    }

    private void OnStatSliderValueChanged(ChangeEvent<float> evt)
    {
        
    }

    /// <summary>
    /// Inverts value for display on inverted sliders: actual 20 should show visually as 20.
    /// </summary>
    private static float InvertForSlider(float actualValue, float lowValue, float highValue)
    {
        return highValue + lowValue - actualValue;
    }
}
