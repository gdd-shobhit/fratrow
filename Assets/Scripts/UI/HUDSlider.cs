using UnityEngine;
using UnityEngine.UIElements;
using System;

public class HUDSlider : MonoBehaviour
{
    private VisualElement root;
    private VisualElement slider;
    private VisualElement dragger;
    private VisualElement bar;

    private void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        slider = root.Q<Slider>("Popularity");
        dragger = root.Q<VisualElement>("unity-dragger");
    }
}
