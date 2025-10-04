namespace _GAME.Scripts.Core
{
    public enum GameMode
    {
        PersonToPerson,    // Case 1: Person-Person  
        PersonToObject     // Case 2:   Person-Object
    }
    
    public enum GameState
    {
        Waiting = 0,
        PreparingGame = 1,
        Playing = 2,
        GameEnded = 3
    }
}