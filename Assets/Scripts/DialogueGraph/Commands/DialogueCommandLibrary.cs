using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Dialogue/Command Library")]
public class DialogueCommandLibrary : ScriptableObject
{
    [SerializeField]
    private List<DialogueCommand> commands = new();

    private Dictionary<string, DialogueCommand> lookup;

    public void Initialize()
    {
        lookup = new Dictionary<string, DialogueCommand>();

        foreach (var cmd in commands)
        {
            if (cmd == null) continue;
            lookup[cmd.CommandName] = cmd;
        }
    }

    public bool Execute(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        var parts = commandLine.Split(' ');
        var name = parts[0];
        var args = parts.Length > 1 ? parts[1..] : System.Array.Empty<string>();

        if (lookup.TryGetValue(name, out var cmd))
        {
            cmd.Execute(args);
            return true;
        }

        Log.Warn($"Command not found: {name}");
        return false;
    }
}