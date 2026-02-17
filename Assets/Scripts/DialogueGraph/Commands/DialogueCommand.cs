using UnityEngine;

public abstract class DialogueCommand : ScriptableObject
{
    public abstract string CommandName { get; }

    public abstract void Execute(string[] args);
}