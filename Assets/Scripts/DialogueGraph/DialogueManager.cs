using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public RuntimeDialogueGraph RuntimeGraph;

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

    private void Start()
    {
        foreach (var node in RuntimeGraph.AllNodes) 
        {
            nodeLookup[node.NodeID] = node;
        }

        if (!string.IsNullOrEmpty(RuntimeGraph.EntryNodeID))
        {
            ShowNode(RuntimeGraph.EntryNodeID);
        }
        else
        {
            EndDialogue();
        }

        commandLibrary.Initialize();
    }

    private void Update()
    {
        if(Mouse.current.leftButton.wasPressedThisFrame && currentNode != null && currentNode.Choices.Count == 0)
        {
            if (!string.IsNullOrEmpty(currentNode.NextNodeID))
            {
                ShowNode(currentNode.NextNodeID);
            }
            else
            {
                EndDialogue();
            }
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

        foreach(Transform child in ChoiceButtonContainer)
        {
            Destroy(child.gameObject);
        }

        if (currentNode.Choices.Count > 0)
        {
            foreach (var choice in currentNode.Choices) 
            {
                Button button = Instantiate(ChoiceButtonPrefab, ChoiceButtonContainer);
                TextMeshProUGUI buttonText = button.GetComponent<TextMeshProUGUI>();
                if(buttonText != null)
                {
                    buttonText.text = choice.ChoiceText;
                }

                if(button != null)
                {
                    button.onClick.AddListener(() =>
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
    }

    private void EndDialogue()
    {
        DialoguePanel.SetActive(false);
        currentNode = null;

        foreach (Transform child in ChoiceButtonContainer)
        {
            Destroy(child.gameObject);
        }
    }

    private void ExecuteCommand(string command)
    {
        if (!commandLibrary.Execute(command))
        {
            Debug.LogWarning($"Failed to execute command: {command}");
        }
    }
}
