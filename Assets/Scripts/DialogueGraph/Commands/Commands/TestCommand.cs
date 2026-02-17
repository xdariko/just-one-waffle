using UnityEngine;

[CreateAssetMenu(menuName = "Dialogue/Command")]
public class TestCommand : DialogueCommand
{
    public override string CommandName => "Test";

    public override void Execute(string[] args)
    {
        Debug.Log("TestCommand executed");
    }
}