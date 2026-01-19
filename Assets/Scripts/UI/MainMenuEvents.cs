using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuEvents : MonoBehaviour
{
    private UIDocument document;
    private Button button;

    private List<Button> menuButtons = new List<Button>();

    private void Awake()
    {
        document = GetComponent<UIDocument>();
        button = document.rootVisualElement.Q("Start") as Button;
        button.RegisterCallback<ClickEvent>(OnPlayGameClicked);

        menuButtons = document.rootVisualElement.Query<Button>().ToList();
        for(int i = 0; i < menuButtons.Count; ++i)
        {
            menuButtons[i].RegisterCallback<ClickEvent>(OnAllButtonsClicked); 
        }
    }

    private void OnDisable()
    {
        button.UnregisterCallback<ClickEvent>(OnPlayGameClicked);
        for (int i = 0; i < menuButtons.Count; ++i)
        {
            menuButtons[i].UnregisterCallback<ClickEvent>(OnAllButtonsClicked);
        }
    }

    private void OnPlayGameClicked(ClickEvent evt)
    {
        Debug.Log("Started the Game");
        PartyManager.Instance.StartParty();
    }

    private void OnAllButtonsClicked(ClickEvent evt)
    {
        // For each button like transition/ sounds etc
    }
}
