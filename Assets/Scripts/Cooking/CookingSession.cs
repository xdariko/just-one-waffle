public class CookingSession
{
    public int EggsAdded { get; private set; }

    public void AddEgg() => EggsAdded++;

    public void Reset()
    {
        EggsAdded = 0;
    }
}

