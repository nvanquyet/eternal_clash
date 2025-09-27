﻿using System.Collections.Generic;

namespace _GAME.Scripts.HideAndSeek.Util
{
    public static class ListUltil
    {
        public static void ShuffleList<T>(this List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int randomIndex = UnityEngine.Random.Range(0, i + 1); 
                (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
            }
        }

    }
}