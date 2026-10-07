using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Показ диалогов на панели: граф Graph Toolkit (Play) или отдельная реплика из кода (ShowLine)
// с кнопками — так стойка показывает заказ клиента, "Начать готовить", "Уточнить" и реакцию.
public class DialogueManager : MonoBehaviour
{
    public RuntimeDialogueGraph RuntimeGraph;

    [Tooltip("Сыграть RuntimeGraph при старте сцены. Выключено — диалоги запускает код (CounterController)")]
    public bool PlayGraphOnStart = true;

    [SerializeField]
    private DialogueCommandLibrary commandLibrary;

    [Header("UI Components")]
    public GameObject DialoguePanel;
    public TextMeshProUGUI SpeakerNameText;
    public TextMeshProUGUI DialogueText;

    [Header("Choice Button UI")]
    public Button ChoiceButtonPrefab;
    public Transform ChoiceButtonContainer;

    private Dictionary<string, RuntimeDialogueNode> nodeLookup = new Dictionary<string, RuntimeDialogueNode>();
    private RuntimeDialogueNode currentNode;
    private Action onGraphFinished;

    // Реплика из кода: без кнопок — продолжается кликом (onAdvance), с кнопками — только кнопками
    private Action lineAdvance;
    private bool lineHasButtons;

    public bool IsBusy => currentNode != null || lineAdvance != null || lineHasButtons;

    private void Start()
    {
        // Сначала команды: граф может начаться с командного узла
        if (commandLibrary != null) commandLibrary.Initialize();

        if (PlayGraphOnStart && RuntimeGraph != null)
            Play(RuntimeGraph, null);
        else if (!IsBusy)
            Hide();
    }

    private void Update()
    {
        var pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame) return;

        if (currentNode != null && currentNode.Choices.Count == 0)
        {
            if (!string.IsNullOrEmpty(currentNode.NextNodeID))
                ShowNode(currentNode.NextNodeID);
            else
                EndDialogue();
        }
        else if (lineAdvance != null && !lineHasButtons)
        {
            var advance = lineAdvance;
            lineAdvance = null;
            advance();
        }
    }

    // ---------- Граф ----------

    public void Play(RuntimeDialogueGraph graph, Action onFinished)
    {
        ClearLine();
        onGraphFinished = onFinished;
        nodeLookup.Clear();

        if (graph == null)
        {
            EndDialogue();
            return;
        }

        foreach (var node in graph.AllNodes)
        {
            nodeLookup[node.NodeID] = node;
        }

        if (!string.IsNullOrEmpty(graph.EntryNodeID))
        {
            ShowNode(graph.EntryNodeID);
        }
        else
        {
            EndDialogue();
        }
    }

    private void ShowNode(string nodeID)
    {
        if (!nodeLookup.ContainsKey(nodeID))
        {
            EndDialogue();
            return;
        }

        currentNode = nodeLookup[nodeID];

        if (!string.IsNullOrEmpty(currentNode.Command))
        {
            ExecuteCommand(currentNode.Command);

            if (!string.IsNullOrEmpty(currentNode.NextNodeID))
                ShowNode(currentNode.NextNodeID);
            else
                EndDialogue();

            return;
        }

        DialoguePanel.SetActive(true);
        SpeakerNameText.SetText(currentNode.SpeakerName);
        DialogueText.SetText(currentNode.DialogueText);

        ClearButtons();

        if (currentNode.Choices.Count > 0)
        {
            foreach (var choice in currentNode.Choices)
            {
                AddButton(choice.ChoiceText, () =>
                {
                    if (!string.IsNullOrEmpty(choice.DestinationNodeID))
                    {
                        ShowNode(choice.DestinationNodeID);
                    }
                    else
                    {
                        EndDialogue();
                    }
                });
            }
        }
    }

    private void EndDialogue()
    {
        currentNode = null;
        Hide();

        var finished = onGraphFinished;
        onGraphFinished = null;
        finished?.Invoke();
    }

    // ---------- Реплика из кода ----------

    // buttons пусто — реплика продолжается кликом в любом месте (onAdvance)
    public void ShowLine(string speaker, string text, IReadOnlyList<(string label, Action onClick)> buttons, Action onAdvance = null)
    {
        currentNode = null;
        onGraphFinished = null;
        ClearButtons();

        DialoguePanel.SetActive(true);
        SpeakerNameText.SetText(speaker ?? "");
        DialogueText.SetText(text ?? "");

        lineHasButtons = buttons != null && buttons.Count > 0;
        lineAdvance = lineHasButtons ? null : onAdvance;

        if (!lineHasButtons) return;
        foreach (var (label, onClick) in buttons)
            AddButton(label, onClick);
    }

    public void Hide()
    {
        ClearLine();
        if (DialoguePanel != null) DialoguePanel.SetActive(false);
        ClearButtons();
    }

    private void ClearLine()
    {
        lineAdvance = null;
        lineHasButtons = false;
    }

    // ---------- Кнопки ----------

    private readonly List<Button> buttons = new();

    private void AddButton(string label, Action onClick)
    {
        Button button = Instantiate(ChoiceButtonPrefab, ChoiceButtonContainer);
        // Текст лежит в дочернем объекте префаба
        var buttonText = button.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            buttonText.text = label;
        }

        button.onClick.AddListener(() =>
        {
            // Защита от двойного нажатия: после первого клика все кнопки этой реплики выключаются
            if (!button.interactable) return;
            UiEvents.RaiseButtonClicked();
            foreach (var b in buttons)
                if (b != null) b.interactable = false;
            onClick?.Invoke();
        });
        buttons.Add(button);
    }

    private void ClearButtons()
    {
        buttons.Clear();
        if (ChoiceButtonContainer == null) return;
        foreach (Transform child in ChoiceButtonContainer)
        {
            Destroy(child.gameObject);
        }
    }

    private void ExecuteCommand(string command)
    {
        if (commandLibrary == null || !commandLibrary.Execute(command))
        {
            Debug.LogWarning($"Failed to execute command: {command}");
        }
    }
}
